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

#if RHETOS_EF6 || RHETOS_MSSQL
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhetos.Dom.DefaultConcepts;
using System;
using System.Linq;

namespace CommonConcepts.Test
{
    [TestClass]
    public class StorageParameterSizeTest
    {
        [TestMethod]
        public void MapperParameterSizes()
        {
            var tests = new (TestStorage.AllProperties Entity, string ExpectedSizes)[]
            {
                (new TestStorage.AllProperties(),
                    "ShortStringProperty:256, LongStringProperty:-1, BinaryProperty:-1"),
                (new TestStorage.AllProperties { ShortStringProperty = "", LongStringProperty = "", BinaryProperty = Array.Empty<byte>() },
                    "ShortStringProperty:256, LongStringProperty:-1, BinaryProperty:-1"),
                (new TestStorage.AllProperties { ShortStringProperty = new string('a', 256), LongStringProperty = new string('a', 4001), BinaryProperty = new byte[8001] },
                    "ShortStringProperty:256, LongStringProperty:-1, BinaryProperty:-1"),
                (new TestStorage.AllProperties { ShortStringProperty = new string('a', 257), LongStringProperty = "a", BinaryProperty = new byte[1] },
                    "ShortStringProperty:-1, LongStringProperty:-1, BinaryProperty:-1"),
            };

            using var scope = TestScope.Create();
            var mapper = scope.Resolve<IPersistenceStorageObjectMappings>().GetMapping(typeof(TestStorage.AllProperties));

            Assert.AreEqual(
                string.Join(Environment.NewLine, tests.Select(test => test.ExpectedSizes)),
                string.Join(Environment.NewLine, tests.Select(test => DescribeParameterSizes(mapper.GetParameters(test.Entity)))));
        }

        private static string DescribeParameterSizes(PersistenceStorageObjectParameter[] parameters)
        {
            string[] describedProperties =
            {
                nameof(TestStorage.AllProperties.ShortStringProperty),
                nameof(TestStorage.AllProperties.LongStringProperty),
                nameof(TestStorage.AllProperties.BinaryProperty),
            };
            return string.Join(", ", describedProperties
                .Select(propertyName => $"{propertyName}:{parameters.Single(parameter => parameter.PropertyName == propertyName).DbParameter.Size}"));
        }
    }
}
#endif
