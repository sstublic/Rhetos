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

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhetos.Dom.DefaultConcepts;
using System;
using System.Linq;

namespace Rhetos.CommonConcepts.Test
{
    [TestClass]
    public class DbParameterSizeTest
    {
        [TestMethod]
        public void ComputeSize()
        {
            const int unbounded = DbParameterSize.Unbounded;

            var tests = new (int? ValueLength, int MaxLength, int ExpectedSize)[]
            {
                (null, 256, 256),
                (0, 256, 256),
                (1, 256, 256),
                (255, 256, 256),
                (256, 256, 256),
                (257, 256, unbounded),
                (4000, 256, unbounded),
                (4001, 256, unbounded),
                (null, unbounded, unbounded),
                (0, unbounded, unbounded),
                (1, unbounded, unbounded),
                (4000, unbounded, unbounded),
                (4001, unbounded, unbounded),
                (8000, unbounded, unbounded),
                (8001, unbounded, unbounded),
            };

            string report = string.Join(Environment.NewLine, tests.Select(test =>
                DescribeSize(test.ValueLength, test.MaxLength, DbParameterSize.ComputeSize(test.ValueLength, test.MaxLength))));

            string expectedReport = string.Join(Environment.NewLine, tests.Select(test =>
                DescribeSize(test.ValueLength, test.MaxLength, test.ExpectedSize)));

            Assert.AreEqual(expectedReport, report);
        }

        /// <summary>
        /// Marks a size smaller than the value length, because the database client would silently truncate the value.
        /// </summary>
        private static string DescribeSize(int? valueLength, int maxLength, int size)
        {
            bool truncatesValue = size != DbParameterSize.Unbounded && size < (valueLength ?? 0);
            return $"ComputeSize({valueLength?.ToString() ?? "null"}, {maxLength}) = {size}{(truncatesValue ? " TRUNCATES VALUE" : "")}";
        }
    }
}
