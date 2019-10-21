﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Web;
using System.ServiceModel.Dispatcher;
using System.Xml;
using Rhetos;
using Rhetos.Logging;
using System.ServiceModel.Description;
using System.ServiceModel.Channels;
using Autofac;
using Autofac.Integration.Wcf;
using Rhetos.Utilities;
using System.Runtime.Serialization;

namespace Rhetos.Web
{
    /// <summary>
    /// Converts exceptions to a HTTP WEB response that contains JSON-serialized string error message.
    /// Convenient for RESTful JSON web service.
    /// </summary>
    public class JsonErrorHandler : IErrorHandler
    {
        public bool HandleError(Exception error)
        {
            return false;
        }

        public class ResponseMessage
        {
            public string UserMessage;
            public string SystemMessage;
            public override string ToString()
            {
                return "SystemMessage: " + (SystemMessage ?? "<null>") + ", UserMessage: " + (UserMessage ?? "<null>");
            }
        }

        public void ProvideFault(
            Exception error,
            MessageVersion version,
            ref Message fault)
        {
            if (error is WebFaultException)
                return;

            object responseMessage;
            HttpStatusCode responseStatusCode;
            var localizer = AutofacServiceHostFactory.Container.Resolve<ILocalizer>();

            if (error is UserException)
            {
                var userError = (UserException)error;

                responseStatusCode = HttpStatusCode.BadRequest;
                responseMessage = new ResponseMessage { UserMessage = localizer[userError.Message, userError.MessageParameters], SystemMessage = userError.SystemMessage };
            }
            else if (error is LegacyClientException)
            {
                responseStatusCode = ((LegacyClientException)error).HttpStatusCode;
                responseMessage = error.Message;
            }
            else if (error is ClientException)
            {
                responseStatusCode = HttpStatusCode.BadRequest;
                responseMessage = new ResponseMessage { SystemMessage = error.Message };
            }
            else if (error is InvalidOperationException && error.Message.StartsWith("The incoming message has an unexpected message format 'Raw'"))
            {
                responseStatusCode = HttpStatusCode.BadRequest;
                responseMessage = new ResponseMessage
                {
                    SystemMessage = "The incoming message has an unexpected message format 'Raw'. Set the Content-Type to 'application/json'." +
                        " " + FrameworkException.SeeLogMessage(error)
                };
            }
            else if (error is SerializationException && !error.StackTrace.ToString().Contains("Rhetos"))
            {
                responseStatusCode = HttpStatusCode.BadRequest;
                responseMessage = new ResponseMessage
                {
                    SystemMessage = "Serialization error: Please check if the request body has a valid JSON format."
                        + " " + FrameworkException.SeeLogMessage(error)
                };
            }
            else
            {
                responseStatusCode = HttpStatusCode.InternalServerError;
                responseMessage = new ResponseMessage { SystemMessage = FrameworkException.GetInternalServerErrorMessage(localizer, error) };
            }

            fault = Message.CreateMessage(version, "", responseMessage,
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(responseMessage.GetType()));

            fault.Properties.Add(WebBodyFormatMessageProperty.Name,
                new WebBodyFormatMessageProperty(WebContentFormat.Json));

            fault.Properties.Add(HttpResponseMessageProperty.Name,
                new HttpResponseMessageProperty {StatusCode = responseStatusCode});

            var response = WebOperationContext.Current.OutgoingResponse;
            response.ContentType = "application/json; charset=" + response.BindingWriteEncoding.WebName;
            response.StatusCode = responseStatusCode;
        }
    }
}