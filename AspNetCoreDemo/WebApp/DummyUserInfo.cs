using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rhetos.Utilities;

namespace WebApp
{
    public class DummyUserInfo : IUserInfo
    {
        public string Report()
        {
            return "DummyUser";
        }

        public bool IsUserRecognized => true;
        public string UserName => "ILLIDAN\\Sasa";
        public string Workstation => "ILLIDAN";
    }
}
