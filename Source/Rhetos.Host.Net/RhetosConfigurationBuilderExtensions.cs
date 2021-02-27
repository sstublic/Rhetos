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
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Rhetos
{
    public static class RhetosConfigurationBuilderExtensions
    {
        public static IConfigurationBuilder MapNetCoreConfiguration(this IConfigurationBuilder builder, IConfiguration configurationToMap)
        {
            if (configurationToMap != null)
                foreach (var configurationItem in configurationToMap.AsEnumerable().Where(a => a.Value != null))
                {
                    var key = configurationItem.Key;
                    if (configurationToMap is IConfigurationSection configurationSection && !string.IsNullOrEmpty(configurationSection.Path))
                    {
                        var regex = new Regex($"^{configurationSection.Path}:");
                        key = regex.Replace(key, "");
                    }
                    builder.AddKeyValue(key, configurationItem.Value);
                }

            return builder;
        }
    }
}
