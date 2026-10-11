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
using Rhetos;
using Rhetos.Dom.DefaultConcepts;
using Rhetos.Extensibility;
using Rhetos.Processing;
using Rhetos.Processing.DefaultCommands;
using Rhetos.TestCommon;
using Rhetos.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CommonConcepts.Test
{
    /// <summary>
    /// Filtering by a list of IDs, see OrmRepositoryBase.Filter(query, ids).
    /// EF6 filters large ID lists by inserting the IDs into the Common.FilterId table.
    /// EF Core does not use the table; it sends the ID list as query parameters.
    /// </summary>
    [TestClass]
    public class FilterByIdsTest
    {
        /// <summary>
        /// The smallest ID list that EF6 filters by the Common.FilterId table.
        /// </summary>
        private const int LargeIdListSize = 200;

        //=======================================================================
        // Common.FilterId table usage on each code path that filters by IDs:

        [TestMethod]
        public void LargeIdList_RepositoryFilter()
        {
            var records = CreateTest10SimpleRecords(LargeIdListSize);
            var ids = records.Select(record => record.ID).ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope => ReplaceTest10SimpleRecords(scope, records),
                act: scope =>
                {
                    var repository = scope.Resolve<Common.DomRepository>().Test10.Simple;
                    Assert.AreEqual(ids.Count, repository.Filter(repository.Query(), ids).Count());
                });
        }

        [TestMethod]
        public void LargeIdList_RepositoryQuery()
        {
            var records = CreateTest10SimpleRecords(LargeIdListSize);
            var ids = records.Select(record => record.ID).ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope => ReplaceTest10SimpleRecords(scope, records),
                act: scope => Assert.AreEqual(ids.Count, scope.Resolve<Common.DomRepository>().Test10.Simple.Query(ids).Count()));
        }

        [TestMethod]
        public void LargeIdList_GenericRepositoryQuery()
        {
            var records = CreateTest10SimpleRecords(LargeIdListSize);
            var ids = records.Select(record => record.ID).ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope => ReplaceTest10SimpleRecords(scope, records),
                act: scope => Assert.AreEqual(ids.Count, scope.Resolve<GenericRepository<Test10.Simple>>().Query(ids).Count()));
        }

        [TestMethod]
        public void LargeIdList_GenericRepositoryFilterInMemoryItems()
        {
            // GenericRepository filters in-memory items with the repository's queryable Filter method, on an in-memory query.
            var items = Enumerable.Range(0, LargeIdListSize + 1)
                .Select(index => new Common.Queryable.Test10_Simple { ID = Guid.NewGuid(), i = index })
                .ToList();
            var ids = items.Take(LargeIdListSize).Select(item => item.ID).ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                act: scope =>
                {
                    var filtered = scope.Resolve<GenericRepository<Test10.Simple>>().Filter(items, ids);
                    CollectionAssert.AreEqual(ids, filtered.Select(item => item.ID).ToList());
                });
        }

        [TestMethod]
        public void LargeIdList_SaveLoadOldItems()
        {
            // ModificationTimeOf loads the old items' properties on save.
            var items = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestAuditable.Simple2 { ID = Guid.NewGuid(), Name = "n" + index })
                .ToList();
            var ids = items.Select(item => item.ID).ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope => scope.Resolve<Common.DomRepository>().TestAuditable.Simple2.Insert(items),
                act: scope =>
                {
                    foreach (var item in items)
                        item.Name += "x";
                    var repository = scope.Resolve<Common.DomRepository>().TestAuditable.Simple2;
                    repository.Update(items);
                    Assert.AreEqual(ids.Count, repository.Load(ids).Count(item => item.Name.EndsWith('x')));
                });
        }

        [TestMethod]
        public void LargeIdList_InvalidData()
        {
            var items = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestInvalidData.Simple2 { ID = Guid.NewGuid(), Name = "x" + index })
                .ToList();
            var ids = items.Select(item => item.ID).ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                act: scope =>
                {
                    var repository = scope.Resolve<Common.DomRepository>().TestInvalidData.Simple2;
                    repository.Insert(items);
                    Assert.AreEqual(ids.Count, repository.Load(ids).Length);
                });
        }

        [TestMethod]
        public void LargeIdList_InvalidDataMessageParameters()
        {
            // Each name contains "c", which is invalid data with message parameters (name, name length).
            var items = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestInvalidData.Simple2 { ID = Guid.NewGuid(), Name = "xc" + index.ToString("000") })
                .ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                act: scope => TestUtility.ShouldFail<UserException>(
                    () => scope.Resolve<Common.DomRepository>().TestInvalidData.Simple2.Insert(items),
                    "Contains C (xc", ", 5)"));
        }

        [TestMethod]
        public void LargeIdList_LockItems()
        {
            var items = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestLockItems.Simple { ID = Guid.NewGuid(), Name = "n" + index, Count = 0 })
                .ToList();
            var lockedItem = items.Last();
            lockedItem.Name = "lock";
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope => scope.Resolve<Common.DomRepository>().TestLockItems.Simple.Insert(items),
                act: scope =>
                {
                    foreach (var item in items)
                        item.Count++;
                    TestUtility.ShouldFail<UserException>(
                        () => scope.Resolve<Common.DomRepository>().TestLockItems.Simple.Update(items),
                        "[Test] Name contains lock mark.", lockedItem.ID.ToString());
                });
        }

        [TestMethod]
        public void LargeIdList_LockItemsExceptProperties()
        {
            var items = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestLockItems.Simple2 { ID = Guid.NewGuid(), Name = "n" + index, Count = 0 })
                .ToList();
            var lockedItem = items.Last();
            lockedItem.Name = "LockExceptName";
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope => scope.Resolve<Common.DomRepository>().TestLockItems.Simple2.Insert(items),
                act: scope =>
                {
                    foreach (var item in items)
                        item.Count++;
                    TestUtility.ShouldFail<UserException>(
                        () => scope.Resolve<Common.DomRepository>().TestLockItems.Simple2.Update(items),
                        "The record is locked except the Name property.", lockedItem.ID.ToString());
                });
        }

        [TestMethod]
        public void LargeIdList_LockProperty()
        {
            var items = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestLockItems.Simple2 { ID = Guid.NewGuid(), Name = "n" + index, Count = 0 })
                .ToList();
            var lockedItem = items.Last();
            lockedItem.Count = -1;
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope => scope.Resolve<Common.DomRepository>().TestLockItems.Simple2.Insert(items),
                act: scope =>
                {
                    foreach (var item in items)
                        item.Name += "x";
                    TestUtility.ShouldFail<UserException>(
                        () => scope.Resolve<Common.DomRepository>().TestLockItems.Simple2.Update(items),
                        "Name is locked if count negative.", lockedItem.ID.ToString());
                });
        }

        [TestMethod]
        public void LargeIdList_PolymorphicFilterSubtype()
        {
            var items = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestPolymorphic.Simple1 { ID = Guid.NewGuid(), Name = "n" + index, Days = index })
                .ToList();
            var ids = items.Select(item => item.ID).ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope => scope.Resolve<Common.DomRepository>().TestPolymorphic.Simple1.Insert(items),
                act: scope =>
                {
                    var filter = new FilterSubtype { Ids = ids, Subtype = typeof(TestPolymorphic.Simple1).FullName, ImplementationName = "" };
                    var loaded = scope.Resolve<Common.DomRepository>().TestPolymorphic.SimpleBase.Load(filter);
                    CollectionAssert.AreEquivalent(ids, loaded.Select(item => item.ID).ToList());
                });
        }

        [TestMethod]
        public void LargeIdList_SaveCommandRowPermissions()
        {
            // The write row permissions of SimpleRP allow values from 500 to 3500.
            var items = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestRowPermissions.SimpleRP { ID = Guid.NewGuid(), value = 1000 })
                .ToList();
            var ids = items.Select(item => item.ID).ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope => scope.Resolve<Common.DomRepository>().TestRowPermissions.SimpleRP.Insert(items),
                act: scope =>
                {
                    foreach (var item in items)
                        item.value++;
                    ExecuteSaveCommand(scope, new SaveEntityCommandInfo { Entity = typeof(TestRowPermissions.SimpleRP).FullName, DataToUpdate = items.ToArray() });
                    Assert.AreEqual(ids.Count, scope.Resolve<Common.DomRepository>().TestRowPermissions.SimpleRP.Load(ids).Count(item => item.value == 1001));
                });
        }

        [TestMethod]
        public void LargeIdList_SaveCommandMissingItems()
        {
            var nonexistentItems = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestRowPermissions.SimpleRP { ID = Guid.NewGuid(), value = 1000 })
                .ToArray();
            AssertFilterIdTableUsedOnlyOnEf6(
                act: scope => TestUtility.ShouldFail<ClientException>(
                    () => ExecuteSaveCommand(scope, new SaveEntityCommandInfo { Entity = typeof(TestRowPermissions.SimpleRP).FullName, DataToUpdate = nonexistentItems }),
                    "Updating a record that does not exist in database."));
        }

        [TestMethod]
        public void LargeIdList_ChangesOnReferenced()
        {
            var parent = new TestChangesOnReferenced.Parent { ID = Guid.NewGuid(), Name = "p" };
            var poly = new TestChangesOnReferenced.ImplementationSimple { ID = Guid.NewGuid(), Name = "s" };
            var items = Enumerable.Range(0, LargeIdListSize)
                .Select(index => new TestChangesOnReferenced.Tested { ID = Guid.NewGuid(), Name = "t", ParentID = parent.ID, PolyID = poly.ID })
                .ToList();
            var ids = items.Select(item => item.ID).ToList();
            AssertFilterIdTableUsedOnlyOnEf6(
                arrange: scope =>
                {
                    var test = scope.Resolve<Common.DomRepository>().TestChangesOnReferenced;
                    test.Parent.Insert(parent);
                    test.ImplementationSimple.Insert(poly);
                    test.Tested.Insert(items);
                },
                act: scope =>
                {
                    foreach (var item in items)
                        item.Name = "tx";
                    var test = scope.Resolve<Common.DomRepository>().TestChangesOnReferenced;
                    test.Tested.Update(items);
                    var computed = test.TestedInfo.Load(ids);
                    Assert.AreEqual(ids.Count, computed.Length);
                    Assert.AreEqual("tx-p-s", TestUtility.DumpSorted(computed.Select(item => item.Info).Distinct()));
                });
        }

        /// <summary>
        /// Executes <paramref name="arrange"/> and then <paramref name="act"/> in one transaction, and asserts that
        /// <paramref name="act"/> used the Common.FilterId table on EF6, but not on EF Core.
        /// The usage is detected by the SQL commands executed by Rhetos and by the table's row count,
        /// since the inserted IDs are deleted only when the transaction is committed.
        /// </summary>
        private static void AssertFilterIdTableUsedOnlyOnEf6(Action<IUnitOfWorkScope> act, Action<IUnitOfWorkScope> arrange = null)
        {
            string filterIdTable = typeof(Common.FilterId).FullName;
            var sqlLog = new SqlExecuterLog();
            using var scope = TestScope.Create(builder => builder.ConfigureSqlExecuterMonitor(sqlLog));
            var filterIdRepository = scope.Resolve<Common.DomRepository>().Common.FilterId;

            arrange?.Invoke(scope);
            int rowCountBefore = filterIdRepository.Query().Count();
            sqlLog.Clear();

            act(scope);

            var filterIdUsage = (
                SqlCommands: sqlLog.Count(sql => sql.Contains(filterIdTable, StringComparison.OrdinalIgnoreCase)),
                RowsAdded: filterIdRepository.Query().Count() - rowCountBefore);
            Console.WriteLine($"{filterIdTable} usage: {filterIdUsage}");
#if RHETOS_EF6
            Assert.IsTrue(filterIdUsage.SqlCommands > 0 && filterIdUsage.RowsAdded > 0,
                $"EF6 is expected to filter large ID lists by the {filterIdTable} table. Actual usage: {filterIdUsage}.");
#else
            Assert.AreEqual((0, 0), filterIdUsage,
                $"EF Core is expected to filter large ID lists without the {filterIdTable} table. Usage: (SQL commands, rows added).");
#endif
        }

        private static void ExecuteSaveCommand(IUnitOfWorkScope scope, SaveEntityCommandInfo saveInfo)
        {
            var commands = scope.Resolve<IPluginsContainer<ICommandImplementation>>();
            commands.GetImplementations(saveInfo.GetType()).Single().Execute(saveInfo);
        }

        //=======================================================================
        // Results for different list sizes, including the limits of the ID list translation on EF Core with SQL Server:
        // The ID filters (Filter, Query(ids), and Load(ids) in batches of 500 IDs) and the generic In filter on ID and reference properties
        // send a list of 200 or more IDs in a single JSON parameter.
        // A smaller list keeps EF Core's default translation: SQL Server allows 2100 parameters per command, and EF Core sends one parameter per ID
        // while the command has at most 2098 parameters in total (including other query parameters), otherwise it sends the IDs in a single JSON parameter.

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(199)]
        [DataRow(200)]
        [DataRow(2097)]
        [DataRow(2099)]
        [DataRow(10000)]
        public void FilterByIdsResults(int idCount)
        {
            using var scope = TestScope.Create();
            var repository = scope.Resolve<Common.DomRepository>().Test10.Simple;

            var requestedRecords = CreateTest10SimpleRecords(idCount);
            var storedRecords = requestedRecords
                .Where(record => record.i % 3 != 2) // Every third requested ID does not exist in the database.
                .Append(new Test10.Simple { ID = Guid.NewGuid(), i = idCount }) // The stored record that is not requested.
                .ToList();
            ReplaceTest10SimpleRecords(scope, storedRecords);

            var ids = requestedRecords.Select(record => record.ID).ToList();
            var expected = storedRecords.Select(record => record.i).Where(i => i < idCount).ToList();

            CollectionAssert.AreEqual(
                expected,
                repository.Filter(repository.Query(), ids).Select(item => item.i).OrderBy(i => i).ToList(),
                "Filter by IDs.");

            int minI = idCount / 4;
            CollectionAssert.AreEqual(
                expected.Where(i => i >= minI).ToList(),
                repository.Filter(repository.Query().Where(item => item.i >= minI), ids).Select(item => item.i).OrderBy(i => i).ToList(),
                "Filter by IDs, with an additional query parameter.");

            CollectionAssert.AreEqual(
                expected,
                repository.Load(ids).Select(item => item.i).OrderBy(i => i).ToList(),
                "Load by IDs.");
        }

        /// <summary>
        /// Returns new Test10.Simple records with unique IDs, where property 'i' is the record's index.
        /// </summary>
        private static List<Test10.Simple> CreateTest10SimpleRecords(int count)
        {
            return Enumerable.Range(0, count).Select(index => new Test10.Simple { ID = Guid.NewGuid(), i = index }).ToList();
        }

        /// <summary>
        /// Deletes all Test10.Simple records, and inserts the given records with plain SQL, for performance.
        /// </summary>
        private static void ReplaceTest10SimpleRecords(IUnitOfWorkScope scope, IEnumerable<Test10.Simple> records)
        {
            const int maxRowsPerInsert = 1000; // SQL Server limit for INSERT ... VALUES.
            var sqlUtility = scope.Resolve<ISqlUtility>();
            var insertCommands = records.Chunk(maxRowsPerInsert)
                .Select(chunk => "INSERT INTO Test10.Simple (ID, i) VALUES "
                    + string.Join(", ", chunk.Select(record => $"({sqlUtility.QuoteGuid(record.ID)}, {record.i})"))
                    + ";");
            scope.Resolve<ISqlExecuter>().ExecuteSql(insertCommands.Prepend("DELETE FROM Test10.Simple;"));
        }

        //=======================================================================

#if RHETOS_MSSQL
        [TestMethod]
        public void FilterByIdsSqlShape()
        {
            using var scope = TestScope.Create();
            var repository = scope.Resolve<Common.DomRepository>().Test10.Simple;

            var tests = new (int IdCount, string LinqContainsShape, string RhetosFilterShape)[]
            {
                (199, "IN (200 parameters)", "IN (200 parameters)"), // EF Core pads the parameter list to a multiple of 50 in this range.
                (200, "IN (200 parameters)", "OPENJSON"), // The smallest list that the Rhetos filters send in a JSON parameter.
                (2099, "OPENJSON", "OPENJSON"), // Above EF Core's limit of 2098 parameters.
            };

            // The LINQ Contains query is a control query, with EF Core's default translation of the ID list.
            var queries = new (string Name, Func<List<Guid>, IQueryable<Common.Queryable.Test10_Simple>> Create, bool IsControlQuery)[]
            {
                ("LINQ Contains", ids => repository.Query().Where(item => ids.Contains(item.ID)), true),
                ("generic In", ids => repository.Query(new FilterCriteria("ID", "In", ids)), false),
                ("Filter", ids => repository.Filter(repository.Query(), ids), false),
                ("Query(ids)", ids => repository.Query(ids), false),
            };

            var expectedReport = tests.SelectMany(test => queries.Select(query =>
                $"{test.IdCount} IDs, {query.Name}: {(query.IsControlQuery ? test.LinqContainsShape : test.RhetosFilterShape)}"));
            var actualReport = tests.SelectMany(test =>
            {
                var ids = CreateTest10SimpleRecords(test.IdCount).Select(record => record.ID).ToList();
                return queries.Select(query =>
                    $"{test.IdCount} IDs, {query.Name}: {SummarizeIdListFilterSql(query.Create(ids).Select(item => item.ID).GetSqlQuery())}");
            });

            string expected = string.Join(Environment.NewLine, expectedReport);
            string actual = string.Join(Environment.NewLine, actualReport);
            Assert.AreEqual(expected, actual, $"{Environment.NewLine}Expected:{Environment.NewLine}{expected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
        }

        /// <summary>
        /// Returns a short description of how the SQL query filters by an ID list, for example "IN (250 parameters)" or "OPENJSON".
        /// </summary>
        private static string SummarizeIdListFilterSql(string sql)
        {
            var inListParameterCounts = Regex.Matches(sql, @"\[ID\] IN \((?<parameters>@\w+(, @\w+)*)\)")
                .Select(match => $"IN ({match.Groups["parameters"].Value.Split(", ").Length} parameters)");
            var otherFeatures = new[] { "OPENJSON", nameof(Common.FilterId) }
                .Where(feature => sql.Contains(feature, StringComparison.OrdinalIgnoreCase));
            return string.Join(", ", inListParameterCounts.Concat(otherFeatures));
        }
#endif
    }
}
