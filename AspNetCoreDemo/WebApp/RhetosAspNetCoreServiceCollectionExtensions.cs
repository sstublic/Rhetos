using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Rhetos.Utilities;
using WebApp;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class RhetosAspNetCoreServiceCollectionExtensions
    {
        public static IServiceCollection AddRhetos(this IServiceCollection serviceCollection, string rhetosApplicationFolder)
        {
            var logProvider = new ConsoleLogProvider();
            var host = Rhetos.Host.Find(rhetosApplicationFolder, logProvider);
            var rhetosConfiguration = host.RhetosRuntime.BuildConfiguration(logProvider, rhetosApplicationFolder, _ => { });
            var rhetosContainer = host.RhetosRuntime.BuildContainer(logProvider, rhetosConfiguration, builder =>
            {
                builder.RegisterInstance(new DummyUserInfo()).As<IUserInfo>().SingleInstance();
            });
            serviceCollection.AddSingleton(rhetosContainer);

            return serviceCollection;
        }
    }
}
