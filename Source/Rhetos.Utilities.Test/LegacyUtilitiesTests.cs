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

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhetos.Extensibility;
using Rhetos.TestCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rhetos.Utilities.Test
{
    [TestClass]
    public class LegacyUtilitiesTests
    {
        public LegacyUtilitiesTests()
        {
            var configurationProvider = new ConfigurationBuilder()
                .AddRhetosAppConfiguration(AppDomain.CurrentDomain.BaseDirectory)
                .AddConfigurationManagerConfiguration()
                .Build();

            LegacyUtilities.Initialize(configurationProvider);
        }

        [TestMethod]
        public void SqlUtilityWorksCorrectly()
        {
            Assert.AreEqual("MsSql", SqlUtility.DatabaseLanguage);
            Assert.IsFalse(string.IsNullOrEmpty(SqlUtility.ConnectionString));
            Assert.AreEqual(31, SqlUtility.SqlCommandTimeout);
        }

        [TestMethod]
        public void PathThrowsOnNullEnvironment()
        {
            Paths.Initialize(null, null, null);
            TestUtility.ShouldFail<FrameworkException>(() => Console.WriteLine(Paths.RhetosServerRootPath), "Rhetos server is not initialized (Paths class)");
            TestUtility.ShouldFail<FrameworkException>(() => Console.WriteLine(Paths.BinFolder), "Rhetos server is not initialized (Paths class)");
            TestUtility.ShouldFail<FrameworkException>(() => Console.WriteLine(Paths.GeneratedFilesCacheFolder), "Rhetos server is not initialized (Paths class)");
            TestUtility.ShouldFail<FrameworkException>(() => Console.WriteLine(Paths.GeneratedFolder), "Rhetos server is not initialized (Paths class)");
            TestUtility.ShouldFail<FrameworkException>(() => Console.WriteLine(Paths.PackagesCacheFolder), "Rhetos server is not initialized (Paths class)");
            TestUtility.ShouldFail<FrameworkException>(() => Console.WriteLine(Paths.PluginsFolder), "Rhetos server is not initialized (Paths class)");
            TestUtility.ShouldFail<FrameworkException>(() => Console.WriteLine(Paths.ResourcesFolder), "Rhetos server is not initialized (Paths class)");

            Paths.Initialize(null, new RhetosAppOptions(), new BuildOptions());
            TestUtility.ShouldFail<FrameworkException>(() => Console.WriteLine(Paths.GeneratedFolder), "One of the following value should be set");

            Paths.Initialize(null, new RhetosAppOptions { AssetsFolder = @"C:\My folder 1"}, new BuildOptions { GeneratedAssetsFolder = @"C:\My folder 2" });
            TestUtility.ShouldFail<FrameworkException>(() => Console.WriteLine(Paths.GeneratedFolder), "Invalid initialization of class Paths.");
        }
    }
}
