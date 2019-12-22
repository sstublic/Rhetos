using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rhetos.Extensibility
{
    public class CachedPluginScannerOptions
    {
        public string BinFolder { get; set; }
        public string PluginScannerCacheFilename { get; set; } = "Rhetos.CachedPluginScanner.Cache.json";
    }
}
