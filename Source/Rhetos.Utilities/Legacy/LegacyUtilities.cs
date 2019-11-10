﻿using Rhetos.Utilities.ApplicationConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rhetos.Utilities
{
    public static class LegacyUtilities
    {
#pragma warning disable CS0618 // Type or member is obsolete
        public static void Initialize(IConfigurationProvider configurationProvider)
        {
            var rhetosAppOptions = configurationProvider.GetOptions<RhetosAppOptions>();
            var rhetosAppEnvironment = new RhetosAppEnvironment(rhetosAppOptions.RootPath);
            Paths.Initialize(rhetosAppEnvironment);
            ConfigUtility.Initialize(configurationProvider);
            
            var connectionStringOptions = configurationProvider.GetOptions<ConnectionStringOptions>("ConnectionStrings:ServerConnectionString");
            SqlUtility.Initialize(rhetosAppOptions, connectionStringOptions);
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
