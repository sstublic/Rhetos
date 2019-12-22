/*
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

using Rhetos.Logging;
using Rhetos.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.ReflectionModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace Rhetos.Extensibility
{
    public class CachedPluginScanner : IPluginScanner
    {
        /// <summary>
        /// The key is FullName of the plugin's export type (it is usually the interface it implements).
        /// </summary>
        private MultiDictionary<string, PluginInfo> _pluginsByExport = null;
        private object _pluginsLock = new object();
        private readonly ILogger _logger;
        private readonly ILogger _performanceLogger;
        private readonly Func<IEnumerable<string>> _findAssemblies;
        private readonly CachedPluginScannerOptions _options;

        /// <summary>
        /// It searches for type implementations in the provided list of assemblies.
        /// </summary>
        /// <param name="findAssemblies">The findAssemblies function should return a list of assembly file paths that will be searched for plugins when invoking the method <see cref="CachedPluginScanner.FindPlugins"/></param>
        public CachedPluginScanner(Func<IEnumerable<string>> findAssemblies, CachedPluginScannerOptions options, ILogProvider logProvider)
        {
            _findAssemblies = findAssemblies;
            _options = options;
            _performanceLogger = logProvider.GetLogger("Performance");
            _logger = logProvider.GetLogger("Plugins");
        }

        /// <summary>
        /// Returns plugins that are registered for the given interface, sorted by dependencies (MefPovider.DependsOn).
        /// </summary>
        public IEnumerable<PluginInfo> FindPlugins(Type pluginInterface)
        {
            lock (_pluginsLock)
            {
                if (_pluginsByExport == null)
                {
                    var assemblies = ListAssemblies();

                    try
                    {
                        _pluginsByExport = LoadPlugins(assemblies);
                    }
                    catch (Exception ex)
                    {
                        string typeLoadReport = CsUtility.ReportTypeLoadException(ex, "Cannot load plugins.", assemblies);
                        if (typeLoadReport != null)
                            throw new FrameworkException(typeLoadReport, ex);
                        else
                            ExceptionsUtility.Rethrow(ex);
                    }
                }
                return _pluginsByExport.Get(pluginInterface.FullName);
            }
        }

        private List<string> ListAssemblies()
        {
            var stopwatch = Stopwatch.StartNew();

            var assemblies = _findAssemblies().ToList();

            foreach (var assembly in assemblies)
                if (!File.Exists(assembly))
                    throw new FrameworkException($"{nameof(CachedPluginScanner)}: The given assembly file path does not exist: '{assembly}'.");
                else
                    _logger.Trace(() => $"Searching for plugins in '{assembly}'");

            _performanceLogger.Write(stopwatch, $"{nameof(CachedPluginScanner)}: Listed assemblies ({assemblies.Count}).");
            return assemblies;
        }

        private MultiDictionary<string, PluginInfo> LoadPlugins(List<string> assemblyPaths)
        {
            _logger.Info("START LoadPlugins CACHED");
            var stopwatch = Stopwatch.StartNew();

            var cacheFilename = Path.Combine(_options.BinFolder, _options.PluginScannerCacheFilename);
            var cache = File.Exists(cacheFilename)
                ? JsonConvert.DeserializeObject<CachedPluginsData>(File.ReadAllText(cacheFilename))
                : new CachedPluginsData();

            var newCache = new CachedPluginsData();
            var cachedAssemblies = 0;
            var pluginsByExport = new MultiDictionary<string, PluginInfo>();
            var pluginsCount = 0;
            
            foreach (var assemblyPath in assemblyPaths)
            {
                var assemblyModifiedToken = new FileInfo(assemblyPath).LastWriteTimeUtc.ToString("O");
                Dictionary<Type, List<PluginInfo>> exports;
                if (cache.Assemblies.TryGetValue(assemblyPath, out var cachedFileData) && cachedFileData.ModifiedTime == assemblyModifiedToken)
                {
                    exports = GetMefExportsForAssembly(assemblyPath, cachedFileData.TypesWithExports);
                    cachedAssemblies++;
                }
                else
                {
                    exports = GetMefExportsForAssembly(assemblyPath);
                }

                foreach (var export in exports)
                {
                    foreach (var plugin in export.Value)
                    {
                        pluginsByExport.Add(export.Key.ToString(), plugin);
                        pluginsCount++;
                    }
                }

                newCache.Assemblies.Add(assemblyPath, new CachedFileData()
                {
                    ModifiedTime = assemblyModifiedToken,
                    TypesWithExports = exports.SelectMany(export => export.Value.Select(plugin => plugin.Type.ToString())).Distinct().ToList()
                });
            }

            _logger.Info($"Used cached data for {cachedAssemblies} out of total {assemblyPaths.Count} assemblies.");

            _logger.Info($"Writing new cache data to '{cacheFilename}'.");
            File.WriteAllText(cacheFilename, JsonConvert.SerializeObject(newCache, Formatting.Indented));
            
            foreach (var pluginsGroup in pluginsByExport)
                SortByDependency(pluginsGroup.Value);

            _logger.Info($"TOTAL NEW: {stopwatch.ElapsedMilliseconds} ms for {pluginsCount} plugins.");
            _performanceLogger.Write(stopwatch, $"{nameof(CachedPluginScanner)}: Loaded plugins NEW ({pluginsCount}).");

            return pluginsByExport;
        }

        private static Dictionary<Type, List<PluginInfo>> GetMefExportsForAssembly(string assemblyPath, List<string> typesToCheck = null)
        {
            if (typesToCheck != null && typesToCheck.Count == 0) return new Dictionary<Type, List<PluginInfo>>();

            var assembly = Assembly.LoadFrom(assemblyPath);
            var types = typesToCheck == null
                ? assembly.GetTypes()
                : typesToCheck.Select(type => assembly.GetType(type)).ToArray();

            return GetMefExportsForTypes(types);
        }

        private static Dictionary<Type, List<PluginInfo>> GetMefExportsForTypes(Type[] types)
        {
            var allAttributes = types.Select(a => new
            {
                type = a,
                exports = a.GetCustomAttributesData().Where(b => b.AttributeType == typeof(ExportAttribute)).ToList(),
                metadata = a.GetCustomAttributesData().Where(b => b.AttributeType == typeof(ExportMetadataAttribute)).ToList()
            });

            var byExport = allAttributes.SelectMany(a => a.exports.Select(b => new
            {
                exportType = (Type)b.ConstructorArguments[0].Value,
                pluginInfo = new PluginInfo()
                {
                    Type = a.type,
                    Metadata = a.metadata
                        .Select(m => new KeyValuePair<string, object>((string)m.ConstructorArguments[0].Value, m.ConstructorArguments[1].Value))
                        .Concat(new[] { new KeyValuePair<string, object>("ExportTypeIdentity", b.ConstructorArguments[0].Value) })
                        .ToDictionary(c => c.Key, c => c.Value)
                }
            }));

            var groupedInfo = byExport.GroupBy(a => a.exportType).ToDictionary(a => a.Key, a => a.Select(b => b.pluginInfo).ToList());
            return groupedInfo;
        }

        private string PluginInfoToString(PluginInfo pluginInfo)
        {
            var metadata = pluginInfo.Metadata.Select(a => $"[{a.Key}: {a.Value}]");
            var metadataInfo = string.Join(", ", metadata);
            return $"{pluginInfo.Type.Name} => {metadataInfo}";
        }

        private void SortByDependency(List<PluginInfo> plugins)
        {
            var dependencies = plugins
                .Where(plugin => plugin.Metadata.ContainsKey(MefProvider.DependsOn))
                .Select(plugin => Tuple.Create((Type)plugin.Metadata[MefProvider.DependsOn], plugin.Type))
                .ToList();

            var pluginTypes = plugins.Select(plugin => plugin.Type).ToList();
            Graph.TopologicalSort(pluginTypes, dependencies);
            Graph.SortByGivenOrder(plugins, pluginTypes, plugin => plugin.Type);
        }
    }
}
