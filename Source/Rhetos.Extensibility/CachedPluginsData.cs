using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rhetos.Extensibility
{
    internal class CachedFileData
    {
        public string ModifiedTime { get; set; }
        public List<string> TypesWithExports { get; set; } = new List<string>();
    }

    internal class CachedPluginsData
    {
        public Dictionary<string, CachedFileData> Assemblies { get; set; } = new Dictionary<string, CachedFileData>();
    }
}
