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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhetos;
using Rhetos.Dom.DefaultConcepts;
using Rhetos.TestCommon;
using Rhetos.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CommonConcepts.Test
{
    /// <summary>
    /// Tests the in-memory query optimization (see <see cref="QueryableHelper"/>) on the code paths
    /// that are implemented in the generated repository classes.
    /// </summary>
    [TestClass]
    public class OptimizeInMemoryQueryableTest
    {
        /// <summary>
        /// The <see cref="CommonConceptsRuntimeOptions.OptimizeInMemoryQueryable"/> option is always set explicitly,
        /// instead of using the application's configuration, so that the tests are independent of the test app settings.
        /// </summary>
        private static IUnitOfWorkScope CreateScope(bool optimizeInMemoryQueryable) =>
            TestScope.Create(builder =>
                builder.RegisterInstance(new CommonConceptsRuntimeOptions { OptimizeInMemoryQueryable = optimizeInMemoryQueryable }));

        //=========================================================================
        // Filter methods generated from a DSL script snippet (option-gated).

        /// <summary>
        /// TestFilter.FixedData has a ComposableFilterBy implementation that returns a materialized array
        /// with <c>AsQueryable()</c>, resulting with a standard in-memory query.
        /// </summary>
        [TestMethod]
        public void SnippetFilterOptimizationDisabled()
        {
            using (var scope = CreateScope(optimizeInMemoryQueryable: false))
            {
                var repository = scope.Resolve<Common.DomRepository>();
                var expectedName = scope.Resolve<IUserInfo>().UserName;

                var result = repository.TestFilter.FixedData.Filter(
                    repository.TestFilter.FixedData.Query(), new TestFilter.ComposableFilterWithContext());

                Assert.IsTrue(result is EnumerableQuery<Common.Queryable.TestFilter_FixedData>,
                    $"Unexpected result type {result.GetType()} when the optimization is disabled.");
                Assert.AreEqual(expectedName, TestUtility.Dump(result.ToList(), item => item.Name));
            }
        }

        [TestMethod]
        public void SnippetFilterOptimizationEnabled()
        {
            using (var scope = CreateScope(optimizeInMemoryQueryable: true))
            {
                var repository = scope.Resolve<Common.DomRepository>();
                var expectedName = scope.Resolve<IUserInfo>().UserName;

                var result = repository.TestFilter.FixedData.Filter(
                    repository.TestFilter.FixedData.Query(), new TestFilter.ComposableFilterWithContext());

                Assert.IsTrue(result is InterpretedQueryable<Common.Queryable.TestFilter_FixedData>,
                    $"Unexpected result type {result.GetType()} when the optimization is enabled.");
                Assert.AreEqual(expectedName, TestUtility.Dump(result.ToList(), item => item.Name));
                Assert.AreEqual(expectedName, result.Single().Name);
            }
        }

        /// <summary>
        /// A filter implementation that returns a subset of the given ORM query must not be modified.
        /// </summary>
        [TestMethod]
        public void SnippetFilterOnOrmQueryIsNotModified()
        {
            using (var scope = CreateScope(optimizeInMemoryQueryable: true))
            {
                var repository = scope.Resolve<Common.DomRepository>();

                var query = repository.TestFilter.Source.Query();
                var result = repository.TestFilter.Source.Filter(query, new TestFilter.ComposableFilterByPrefix { Prefix = "a" });

                Assert.IsFalse(result is InterpretedQueryable<Common.Queryable.TestFilter_Source>,
                    $"An ORM query should not be optimized, but the result type is {result.GetType()}.");
                Assert.IsFalse(result is EnumerableQuery,
                    $"An ORM query should not be materialized, but the result type is {result.GetType()}.");
            }
        }

        //=========================================================================
        // Empty query created by the generated OrmRepositoryBase.Filter(query, ids) method (unconditional).

        [TestMethod]
        public void EmptyIdsFilterIsAlwaysInterpreted()
        {
            foreach (bool optimizeInMemoryQueryable in new[] { false, true })
                using (var scope = CreateScope(optimizeInMemoryQueryable))
                {
                    var repository = scope.Resolve<Common.DomRepository>();

                    var result = repository.TestFilter.Simple.Filter(repository.TestFilter.Simple.Query(), new List<Guid>());

                    Assert.IsTrue(result is InterpretedQueryable<Common.Queryable.TestFilter_Simple>,
                        $"Unexpected result type {result.GetType()} (OptimizeInMemoryQueryable={optimizeInMemoryQueryable}).");
                    Assert.AreEqual(0, result.Count());
                    Assert.AreEqual("", TestUtility.Dump(result.ToList()));

                    // Same query shape as the LoadOldItems code in the generated Save method.
                    var oldItems = result.Select(item => new { item.ID, item.Name }).ToList();
                    Assert.AreEqual("", TestUtility.Dump(oldItems, item => item.Name));
                }
        }

        /// <summary>
        /// The empty-ids query is also used by the generated Save method (LoadOldItems) and by the Lock concepts.
        /// </summary>
        [TestMethod]
        public void SaveWithLockConceptsUsesEmptyIdsFilter()
        {
            foreach (bool optimizeInMemoryQueryable in new[] { false, true })
                using (var scope = CreateScope(optimizeInMemoryQueryable))
                {
                    var repository = scope.Resolve<Common.DomRepository>();
                    var item = new TestLockItems.Simple { ID = Guid.NewGuid(), Name = "a", Count = 1 };

                    repository.TestLockItems.Simple.Insert(item); // Insert-only save: the updated and deleted ID lists are empty.

                    item.Name = "b";
                    repository.TestLockItems.Simple.Update(item); // Update-only save: the inserted and deleted ID lists are empty.

                    Assert.AreEqual("b", repository.TestLockItems.Simple.Load(new[] { item.ID }).Single().Name);

                    repository.TestLockItems.Simple.Delete(item);

                    Assert.AreEqual(0, repository.TestLockItems.Simple.Load(new[] { item.ID }).Length);
                }
        }

        //=========================================================================
        // Row permissions deny-all filter (unconditional).

        [TestMethod]
        public void RowPermissionsDenyAllIsAlwaysInterpreted()
        {
            foreach (bool optimizeInMemoryQueryable in new[] { false, true })
                using (var scope = CreateScope(optimizeInMemoryQueryable))
                {
                    var repository = scope.Resolve<Common.DomRepository>();

                    var result = FilterExpression<Common.Queryable.TestFilter_Simple>.OptimizedWhere(
                        repository.TestFilter.Simple.Query(), item => false);

                    Assert.IsTrue(result is InterpretedQueryable<Common.Queryable.TestFilter_Simple>,
                        $"Unexpected result type {result.GetType()} (OptimizeInMemoryQueryable={optimizeInMemoryQueryable}).");
                    Assert.AreEqual("", TestUtility.Dump(result.ToList()));
                }
        }

        //=========================================================================
        // Empty query created by the framework, used as a subquery in an ORM query (unconditional).

        [TestMethod]
        public void RowPermissionsDenyAllResultInOrmSubqueryBehavesAsStandardEmptyQuery()
        {
            foreach (bool optimizeInMemoryQueryable in new[] { false, true })
                using (var scope = CreateScope(optimizeInMemoryQueryable))
                {
                    var repository = scope.Resolve<Common.DomRepository>();
                    var userName = scope.Resolve<IUserInfo>().UserName;

                    // ComplexRP row permissions deny all records to a user without a ComplexRPPermissions record.
                    repository.TestRowPermissions.ComplexRPPermissions.Delete(
                        repository.TestRowPermissions.ComplexRPPermissions.Load(permission => permission.userName == userName));
                    repository.TestRowPermissions.ComplexRP.Insert(new TestRowPermissions.ComplexRP { ID = Guid.NewGuid(), value = 1 });

                    var allowedItems = repository.TestRowPermissions.ComplexRP.Query(new Common.RowPermissionsReadItems());
                    Assert.AreSame(QueryableHelper.EmptyInterpreted<Common.Queryable.TestRowPermissions_ComplexRP>(), allowedItems);

                    AssertSubqueryBehavesAsStandardEmptyQuery(allowedItems, subquery =>
                    {
                        var allowedIds = subquery.Select(item => item.ID);
                        return repository.TestRowPermissions.ComplexRP.Query()
                            .Where(item => allowedIds.Contains(item.ID))
                            .Select(item => item.ID);
                    });
                }
        }

        [TestMethod]
        public void EmptyIdsFilterResultInOrmSubqueryBehavesAsStandardEmptyQuery()
        {
            foreach (bool optimizeInMemoryQueryable in new[] { false, true })
                using (var scope = CreateScope(optimizeInMemoryQueryable))
                {
                    var repository = scope.Resolve<Common.DomRepository>();

                    var parent = new TestGenericFilter.Simple { ID = Guid.NewGuid(), Name = "p" };
                    repository.TestGenericFilter.Simple.Insert(parent);
                    repository.TestGenericFilter.Child.Insert(new TestGenericFilter.Child { ID = Guid.NewGuid(), Name = "c", ParentID = parent.ID });

                    var parents = repository.TestGenericFilter.Simple.Filter(repository.TestGenericFilter.Simple.Query(), new List<Guid>());
                    Assert.AreSame(QueryableHelper.EmptyInterpreted<Common.Queryable.TestGenericFilter_Simple>(), parents);

                    AssertSubqueryBehavesAsStandardEmptyQuery(parents, subquery =>
                    {
                        var parentIds = subquery.Select(item => item.ID);
                        return repository.TestGenericFilter.Child.Query()
                            .Where(item => parentIds.Contains(item.ParentID.Value))
                            .Select(item => item.ID);
                    });
                }
        }

        /// <summary>
        /// Embedding the framework-created empty query in an ORM query behaves the same as embedding
        /// the standard empty in-memory query (<c>Array.Empty&lt;T&gt;().AsQueryable()</c>).
        /// </summary>
        private static void AssertSubqueryBehavesAsStandardEmptyQuery<TEntity>(
            IQueryable<TEntity> emptyQuery, Func<IQueryable<TEntity>, IQueryable<Guid>> buildOrmQuery)
        {
            string outcome = DumpResultOrExceptionType(buildOrmQuery(emptyQuery));
            string standardOutcome = DumpResultOrExceptionType(buildOrmQuery(Array.Empty<TEntity>().AsQueryable()));
            Assert.AreEqual(standardOutcome, outcome,
                $"With the standard empty query: {standardOutcome}. With the framework-created empty query: {outcome}.");
        }

        /// <summary>
        /// Reports only the exception type, because the exception message may contain the printed in-memory query type.
        /// </summary>
        private static string DumpResultOrExceptionType(IQueryable<Guid> query)
        {
            try
            {
                return $"Result: [{TestUtility.Dump(query.ToList())}]";
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.GetType().FullName}";
            }
        }
    }
}
