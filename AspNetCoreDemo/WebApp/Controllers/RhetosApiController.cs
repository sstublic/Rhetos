using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Newtonsoft.Json;
using Rhetos.Processing;
using Rhetos.Processing.DefaultCommands;

namespace WebApp.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class RhetosApiController : ControllerBase
    {
        private readonly IContainer rhetosContainer;

        public RhetosApiController(IContainer rhetosContainer)
        {
            this.rhetosContainer = rhetosContainer;
        }

        [HttpGet]
        public async Task<string> Alive()
        {
            return bool.TrueString;
        }

        [HttpGet]
        public async Task<string> Claims()
        {
            var processingEngine = rhetosContainer.Resolve<IProcessingEngine>();

            var readCommand = new ReadCommandInfo() {DataSource = "Common.Claim", ReadRecords = true};
            var result = processingEngine.Execute(new List<ICommandInfo>() {readCommand});

            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }

    }
}
