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

using Autofac;
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

namespace Rhetos.Extensibility
{
    public class MefPluginScanner : IPluginScanner
    {
        /// <summary>
        /// The key is FullName of the plugin's export type (it is usually the interface it implements).
        /// </summary>
        private MultiDictionary<string, PluginInfo> _pluginsByExport = null;
        private object _pluginsLock = new object();
        private readonly ILogger _logger;
        private readonly ILogger _mefLog;
        private readonly ILogger _performanceLogger;
        private readonly Func<IEnumerable<string>> _findAssemblies;

        /// <summary>
        /// It searches for type implementations in the provided list of assemblies.
        /// </summary>
        /// <param name="findAssemblies">The findAssemblies function should return a list of assembly file paths that will be searched for plugins when invoking the method <see cref="MefPluginScanner.FindPlugins"/></param>
        public MefPluginScanner(Func<IEnumerable<string>> findAssemblies, ILogProvider logProvider)
        {
            _performanceLogger = logProvider.GetLogger("Performance");
            _logger = logProvider.GetLogger("Plugins");
            _mefLog = logProvider.GetLogger("MefLog");
            _findAssemblies = findAssemblies;
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
                        _pluginsByExport = LoadPluginsNew(assemblies);
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
                    throw new FrameworkException($"{nameof(MefPluginScanner)}: The given assembly file path does not exist: '{assembly}'.");
                else
                    _logger.Trace(() => $"Searching for plugins in '{assembly}'");

            _performanceLogger.Write(stopwatch, $"{nameof(MefPluginScanner)}: Listed assemblies ({assemblies.Count}).");
            return assemblies;
        }

        private MultiDictionary<string, PluginInfo> LoadPlugins(List<string> assemblies)
        {
            _mefLog.Info("START LoadPlugins OLD");
            var stopwatch = Stopwatch.StartNew();

            var sw = Stopwatch.StartNew();
            var assemblyCatalogs = assemblies.Select(name => new AssemblyCatalog(name));
            _mefLog.Info($"Assembly catalog: {sw.ElapsedMilliseconds} ms for {assemblies.Count} assemblies.");
            sw.Restart();

            var container = new CompositionContainer(new AggregateCatalog(assemblyCatalogs));
            _mefLog.Info($"Composition container: {sw.ElapsedMilliseconds} ms.");
            sw.Restart();

            var allParts = container.Catalog.Parts.ToList();
            _mefLog.Info($"Enumerate all parts: {sw.ElapsedMilliseconds} ms for {allParts.Count} parts.");
            sw.Restart();

            var mefPlugins = container.Catalog.Parts
                .Select(part => new
                {
                    PluginType = ReflectionModelServices.GetPartType(part).Value,
                    part.ExportDefinitions
                })
                .SelectMany(part =>
                    part.ExportDefinitions.Select(exportDefinition => new
                    {
                        exportDefinition.ContractName,
                        exportDefinition.Metadata,
                        part.PluginType
                    })).ToList();
            _mefLog.Info($"Extract parts: {sw.ElapsedMilliseconds} ms for total {mefPlugins.Count} parts.");
            sw.Restart();

            var pluginsByExport = new MultiDictionary<string, PluginInfo>();
            int pluginsCount = 0;
            foreach (var mefPlugin in mefPlugins)
            {
                pluginsCount++;
                pluginsByExport.Add(
                    mefPlugin.ContractName,
                    new PluginInfo
                    {
                        Type = mefPlugin.PluginType,
                        Metadata = mefPlugin.Metadata.ToDictionary(m => m.Key, m => m.Value)
                    });
            }

            /*
            foreach (var pluginsGroup in pluginsByExport)
            {
                Console.WriteLine($"[{pluginsGroup.Key}]");
                foreach (var info in pluginsGroup.Value)
                {
                    Console.WriteLine($"\t{PluginInfoToString(info)}");
                }
            }
            */

            foreach (var pluginsGroup in pluginsByExport)
                SortByDependency(pluginsGroup.Value);

            _mefLog.Info($"Wrapping up: {sw.ElapsedMilliseconds} ms.");

            _mefLog.Info($"TOTAL OLD MEF: {stopwatch.ElapsedMilliseconds} ms for {pluginsCount} plugins.");
            _performanceLogger.Write(stopwatch, $"{nameof(MefPluginScanner)}: Loaded plugins ({pluginsCount}).");
            return pluginsByExport;
        }

        private MultiDictionary<string, PluginInfo> LoadPluginsNew(List<string> assemblies)
        {
            _mefLog.Info("START LoadPluginsNew");
            var stopwatch = Stopwatch.StartNew();

            var sw = Stopwatch.StartNew();
            var allTypes = assemblies.SelectMany(path => Assembly.LoadFrom(path).GetTypes()).ToArray();
            _mefLog.Info($"Collect all types: {sw.ElapsedMilliseconds} ms for {allTypes.Length} types.");
            sw.Restart();

            var mefExports = GetMefExportsForTypes(allTypes);
            _mefLog.Info($"GetMefExportsForType: {sw.ElapsedMilliseconds} ms for {mefExports.Count} exports.");
            sw.Restart();

            var pluginsByExport = new MultiDictionary<string, PluginInfo>();
            int pluginsCount = 0;
            foreach (var export in mefExports)
            {
                foreach (var plugin in export.Value)
                {
                    pluginsCount++;
                    pluginsByExport.Add(
                        export.Key,
                        plugin);
    
                }
            }
            /*
            foreach (var pluginsGroup in pluginsByExport)
            {
                Console.WriteLine($"[{pluginsGroup.Key}]");
                foreach (var info in pluginsGroup.Value)
                {
                    Console.WriteLine($"\t{PluginInfoToString(info)}");
                }
            }
            */
            foreach (var pluginsGroup in pluginsByExport)
                SortByDependency(pluginsGroup.Value);

            _mefLog.Info($"Wrapping up: {sw.ElapsedMilliseconds} ms.");

            _mefLog.Info($"TOTAL NEW: {stopwatch.ElapsedMilliseconds} ms for {pluginsCount} plugins.");
            _performanceLogger.Write(stopwatch, $"{nameof(MefPluginScanner)}: Loaded plugins NEW ({pluginsCount}).");
            return pluginsByExport;
        }


        private static Dictionary<string, List<PluginInfo>> GetMefExportsForTypes(Type[] types)
        {
            var allAttributes = types.Select(a => new
            {
                type = a,
                exports = a.GetCustomAttributesData().Where(b => b.AttributeType == typeof(ExportAttribute)).ToList(),
                metadata = a.GetCustomAttributesData().Where(b => b.AttributeType == typeof(ExportMetadataAttribute)).ToList()
            });

            var byExport = allAttributes.SelectMany(a => a.exports.Select(b => new
            {
                exportType = b.ConstructorArguments[0].Value.ToString(),
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
