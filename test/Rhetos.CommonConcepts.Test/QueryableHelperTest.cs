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
using Rhetos.CommonConcepts.Test.Mocks;
using Rhetos.Dom.DefaultConcepts;
using Rhetos.TestCommon;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Rhetos.CommonConcepts.Test
{
    /// <summary>
    /// Tests for <see cref="QueryableHelper.OptimizeInMemoryQueryable{T}(IQueryable{T}, int)"/>
    /// and its usage in <see cref="GenericRepository{TEntityInterface}"/>.
    /// </summary>
    [TestClass]
    public class QueryableHelperTest
    {
        //=========================================================================
        #region Test data

        public interface ISimpleEntity : IEntity
        {
            string Name { get; set; }
        }

        public class SimpleEntity : ISimpleEntity
        {
            public Guid ID { get; set; }
            public string Name { get; set; }

            public override string ToString() => Name ?? "<null>";
        }

        private const int NoFallbackThreshold = int.MaxValue;

        private static Guid Id(int index) => new Guid(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        private static List<SimpleEntity> TestItems() =>
        [
            new SimpleEntity { ID = Id(1), Name = "a1" },
            new SimpleEntity { ID = Id(2), Name = "b1" },
            new SimpleEntity { ID = Id(3), Name = "a2" },
        ];

        /// <summary>
        /// A queryable that is not an <see cref="EnumerableQuery{T}"/>, simulating an ORM query
        /// that must not be modified by the optimization.
        /// </summary>
        private sealed class FakeOrmQueryable<T> : IQueryable<T>
        {
            public Type ElementType => typeof(T);

            public Expression Expression => Expression.Constant(this);

            public IQueryProvider Provider => throw new NotSupportedException("The test queryable should not be executed.");

            public IEnumerator<T> GetEnumerator() => Enumerable.Empty<T>().GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        #endregion
        //=========================================================================
        #region Optimizing the queryable

        [TestMethod]
        public void OptimizeMaterializedSource()
        {
            var items = TestItems();
            var query = items.AsQueryable();

            var optimized = QueryableHelper.OptimizeInMemoryQueryable(query, NoFallbackThreshold);

            Assert.IsTrue(optimized is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {optimized.GetType()}.");
            Assert.AreEqual("a1, b1, a2", TestUtility.Dump(optimized.ToList()));
        }

        [TestMethod]
        public void OptimizeArraySource()
        {
            SimpleEntity[] items = [.. TestItems()];

            var optimized = QueryableHelper.OptimizeInMemoryQueryable(items.AsQueryable(), NoFallbackThreshold);

            Assert.IsTrue(optimized is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {optimized.GetType()}.");
            Assert.AreEqual("a1, b1, a2", TestUtility.Dump(optimized.ToList()));
        }

        [TestMethod]
        public void OptimizeIsIdempotent()
        {
            var optimized = QueryableHelper.OptimizeInMemoryQueryable(TestItems().AsQueryable(), NoFallbackThreshold);
            var optimizedAgain = QueryableHelper.OptimizeInMemoryQueryable(optimized, NoFallbackThreshold);

            Assert.AreSame(optimized, optimizedAgain);
        }

        [TestMethod]
        public void OtherQueryableIsNotModified()
        {
            var ormQuery = new FakeOrmQueryable<SimpleEntity>();

            Assert.AreSame(ormQuery, QueryableHelper.OptimizeInMemoryQueryable(ormQuery, NoFallbackThreshold));
        }

        /// <summary>
        /// The source of a queryable must not be enumerated by the optimization,
        /// because ReflectionHelper.AsQueryable often creates a queryable over a lazy iterator.
        /// </summary>
        [TestMethod]
        public void LazySourceIsNotModifiedOrEnumerated()
        {
            int sourceCounter = 0;
            IEnumerable<SimpleEntity> lazyItems = Enumerable.Range(1, 5)
                .Select(index => { sourceCounter++; return new SimpleEntity { ID = Id(index), Name = "a" + index }; });

            var query = lazyItems.AsQueryable();
            var optimized = QueryableHelper.OptimizeInMemoryQueryable(query, NoFallbackThreshold);

            Assert.AreSame(query, optimized);
            Assert.AreEqual(0, sourceCounter, "The optimization must not enumerate the source.");
        }

        [TestMethod]
        public void LazyCastIteratorIsNotModifiedOrEnumerated()
        {
            int sourceCounter = 0;
            IEnumerable lazyItems = TestItems().Select(item => { sourceCounter++; return item; });

            // Same as ReflectionHelper.AsQueryable: Enumerable.Cast returns a lazy iterator.
            var query = lazyItems.Cast<SimpleEntity>().AsQueryable();
            var optimized = QueryableHelper.OptimizeInMemoryQueryable(query, NoFallbackThreshold);

            Assert.AreSame(query, optimized);
            Assert.AreEqual(0, sourceCounter, "The optimization must not enumerate the source.");
        }

        [TestMethod]
        public void OptimizeQueryWithComposedOperators()
        {
            var items = TestItems();
            var composed = items.AsQueryable()
                .Where(item => item.Name.StartsWith("a", StringComparison.Ordinal))
                .OrderByDescending(item => item.Name);

            var optimized = QueryableHelper.OptimizeInMemoryQueryable((IQueryable<SimpleEntity>)composed, NoFallbackThreshold);

            Assert.IsTrue(optimized is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {optimized.GetType()}.");
            Assert.AreEqual(TestUtility.Dump(composed.ToList()), TestUtility.Dump(optimized.ToList()));
            Assert.AreEqual("a2, a1", TestUtility.Dump(optimized.ToList()));
        }

        [TestMethod]
        public void OptimizeQueryWithChangedElementType()
        {
            var items = TestItems();
            var composed = items.AsQueryable().Select(item => item.Name);

            var optimized = QueryableHelper.OptimizeInMemoryQueryable(composed, NoFallbackThreshold);

            Assert.IsTrue(optimized is InterpretedQueryable<string>, $"Unexpected result type {optimized.GetType()}.");
            Assert.AreEqual("a1, b1, a2", TestUtility.Dump(optimized.ToList()));
        }

        [TestMethod]
        public void OptimizeUntypedEntryPoint()
        {
            IEnumerable items = TestItems().AsQueryable();

            var optimized = QueryableHelper.OptimizeInMemoryQueryable(items, NoFallbackThreshold);

            Assert.IsTrue(optimized is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {optimized.GetType()}.");
            Assert.AreEqual("a1, b1, a2", TestUtility.Dump(optimized.Cast<SimpleEntity>().ToList()));

            IEnumerable list = TestItems();
            Assert.AreSame(list, QueryableHelper.OptimizeInMemoryQueryable(list, NoFallbackThreshold));
        }

        [TestMethod]
        public void TelemetryIsOptional()
        {
            Assert.IsNull(QueryableHelper.Telemetry, "Telemetry must be disabled by default.");

            var optimized = QueryableHelper.OptimizeInMemoryQueryable(TestItems().AsQueryable(), NoFallbackThreshold);
            Assert.AreEqual("a1, b1, a2", TestUtility.Dump(optimized.ToList()));

            var records = new List<InterpretedQueryTelemetry>();
            QueryableHelper.Telemetry = records.Add;
            try
            {
                Assert.AreEqual("a1, a2", TestUtility.Dump(optimized.Where(item => item.Name.StartsWith("a", StringComparison.Ordinal)).ToList()));
            }
            finally
            {
                QueryableHelper.Telemetry = null;
            }

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(3, records[0].SourceCount);
            Assert.IsTrue(records[0].Interpreted, "The query should be interpreted below the threshold.");
            TestUtility.AssertContains(records[0].ExpressionShape, "Where");

            Assert.AreEqual("a1, a2", TestUtility.Dump(optimized.Where(item => item.Name.StartsWith("a", StringComparison.Ordinal)).ToList()));
        }

        #endregion
        //=========================================================================
        #region Empty interpreted query

        [TestMethod]
        public void EmptyInterpretedIsInterpretedQueryable()
        {
            var empty = QueryableHelper.EmptyInterpreted<SimpleEntity>();

            Assert.IsTrue(empty is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {empty.GetType()}.");
            Assert.AreEqual("", TestUtility.Dump(empty.ToList()));
            Assert.AreEqual(0, empty.Count());
        }

        [TestMethod]
        public void EmptyInterpretedComposedQueryIsInterpreted()
        {
            var records = new List<InterpretedQueryTelemetry>();
            List<string> result;

            QueryableHelper.Telemetry = records.Add;
            try
            {
                // Same query shape as the generated Save method's LoadOldItems code.
                result = QueryableHelper.EmptyInterpreted<SimpleEntity>()
                    .Select(item => new { item.ID, item.Name })
                    .ToList()
                    .Select(item => item.Name)
                    .ToList();
            }
            finally
            {
                QueryableHelper.Telemetry = null;
            }

            Assert.AreEqual("", TestUtility.Dump(result));
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(0, records[0].SourceCount);
            Assert.IsTrue(records[0].Interpreted, "The empty query must not fall back to the standard queryable.");
        }

        [TestMethod]
        public void EmptyInterpretedInstanceIsCachedPerElementType()
        {
            Assert.AreSame(QueryableHelper.EmptyInterpreted<SimpleEntity>(), QueryableHelper.EmptyInterpreted<SimpleEntity>());
            Assert.AreNotSame(
                (object)QueryableHelper.EmptyInterpreted<SimpleEntity>(),
                (object)QueryableHelper.EmptyInterpreted<string>());
        }

        [TestMethod]
        public void EmptyInterpretedCompositionDoesNotModifyCachedInstance()
        {
            var empty = QueryableHelper.EmptyInterpreted<SimpleEntity>();

            var composed = empty.Where(item => item.Name == "a1");

            Assert.AreNotSame(empty, composed);
            Assert.AreSame(empty, QueryableHelper.EmptyInterpreted<SimpleEntity>());
            Assert.AreEqual("", TestUtility.Dump(composed.ToList()));
            Assert.AreEqual("", TestUtility.Dump(QueryableHelper.EmptyInterpreted<SimpleEntity>().ToList()));
        }

        /// <summary>
        /// The empty query is already optimized, the optimization must not wrap it again.
        /// </summary>
        [TestMethod]
        public void EmptyInterpretedIsNotOptimizedAgain()
        {
            var empty = QueryableHelper.EmptyInterpreted<SimpleEntity>();

            Assert.AreSame(empty, QueryableHelper.OptimizeInMemoryQueryable(empty, NoFallbackThreshold));
        }

        #endregion
        //=========================================================================
        #region Optimizing the filter result

        private static CommonConceptsRuntimeOptions Options(bool optimize, int threshold = 1000) =>
            new CommonConceptsRuntimeOptions { OptimizeInMemoryQueryable = optimize, OptimizeInMemoryQueryableThreshold = threshold };

        [TestMethod]
        public void OptimizeFilterResultWithoutOptions()
        {
            var query = TestItems().AsQueryable();

            Assert.AreSame(query, QueryableHelper.OptimizeFilterResult(query, null));
        }

        [TestMethod]
        public void OptimizeFilterResultDisabled()
        {
            var query = TestItems().AsQueryable();

            Assert.AreSame(query, QueryableHelper.OptimizeFilterResult(query, Options(optimize: false)));
        }

        [TestMethod]
        public void OptimizeFilterResultEnabled()
        {
            var query = TestItems().AsQueryable();

            var optimized = QueryableHelper.OptimizeFilterResult(query, Options(optimize: true));

            Assert.IsTrue(optimized is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {optimized.GetType()}.");
            Assert.AreEqual("a1, b1, a2", TestUtility.Dump(optimized.ToList()));
        }

        [TestMethod]
        public void OptimizeFilterResultOnOrmQuery()
        {
            var ormQuery = new FakeOrmQueryable<SimpleEntity>();

            Assert.AreSame(ormQuery, QueryableHelper.OptimizeFilterResult(ormQuery, Options(optimize: true)));
        }

        [TestMethod]
        public void OptimizeFilterResultUsesThresholdFromOptions()
        {
            var records = new List<InterpretedQueryTelemetry>();

            // The test data has 3 records, the threshold is 3: the standard queryable behavior is expected.
            var optimized = QueryableHelper.OptimizeFilterResult(TestItems().AsQueryable(), Options(optimize: true, threshold: 3));
            Assert.IsTrue(optimized is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {optimized.GetType()}.");

            QueryableHelper.Telemetry = records.Add;
            try
            {
                Assert.AreEqual("a1, a2", TestUtility.Dump(optimized.Where(item => item.Name.StartsWith("a", StringComparison.Ordinal)).ToList()));
            }
            finally
            {
                QueryableHelper.Telemetry = null;
            }

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(3, records[0].SourceCount);
            Assert.IsFalse(records[0].Interpreted, "The query should not be interpreted at the threshold.");

            // The same query below the threshold is interpreted.
            records.Clear();
            var optimizedSmall = QueryableHelper.OptimizeFilterResult(TestItems().AsQueryable(), Options(optimize: true, threshold: 4));
            QueryableHelper.Telemetry = records.Add;
            try
            {
                Assert.AreEqual("a1, a2", TestUtility.Dump(optimizedSmall.Where(item => item.Name.StartsWith("a", StringComparison.Ordinal)).ToList()));
            }
            finally
            {
                QueryableHelper.Telemetry = null;
            }

            Assert.AreEqual(1, records.Count);
            Assert.IsTrue(records[0].Interpreted, "The query should be interpreted below the threshold.");
        }

        #endregion
        //=========================================================================
        #region GenericRepository integration

        public class SimpleFilter { }

        public class SimpleQueryParameter { }

        public class SimpleRepository : IRepository
        {
            public List<SimpleEntity> Items { get; } = TestItems();

            public IQueryable<SimpleEntity> Query() => Items.AsQueryable();

            public IQueryable<SimpleEntity> Query(SimpleQueryParameter parameter) => Items.Where(item => item.Name != "b1").ToList().AsQueryable();

            public IQueryable<SimpleEntity> Filter(IQueryable<SimpleEntity> source, SimpleFilter parameter)
                => source.Where(item => item.Name.StartsWith("a", StringComparison.Ordinal));
        }

        private static GenericRepository<ISimpleEntity> NewRepository(IRepository repository, bool? optimizeInMemoryQueryable)
        {
            var parameters = TestGenericRepository<ISimpleEntity, SimpleEntity>.NewParameters(repository);
            if (optimizeInMemoryQueryable != null)
                parameters.CommonConceptsRuntimeOptions = new CommonConceptsRuntimeOptions
                {
                    OptimizeInMemoryQueryable = optimizeInMemoryQueryable.Value,
                    OptimizeInMemoryQueryableThreshold = 1000,
                };

            return new GenericRepository<ISimpleEntity>(parameters,
                new RegisteredInterfaceImplementations { { typeof(ISimpleEntity), typeof(SimpleEntity).FullName } });
        }

        /// <summary>
        /// GenericRepository.Read, reading option "queryable Filter(Query(), parameter)".
        /// </summary>
        [TestMethod]
        public void GenericRepositoryQueryableFilterOnQuery()
        {
            var disabled = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: false).Query(new SimpleFilter(), typeof(SimpleFilter));
            Assert.IsTrue(disabled is EnumerableQuery<SimpleEntity>, $"Unexpected result type {disabled.GetType()} when the optimization is disabled.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(disabled.ToList()));

            var enabled = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: true).Query(new SimpleFilter(), typeof(SimpleFilter));
            Assert.IsTrue(enabled is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {enabled.GetType()} when the optimization is enabled.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(enabled.ToList()));
        }

        /// <summary>
        /// GenericRepository.Read, reading option "Query(parameter)".
        /// </summary>
        [TestMethod]
        public void GenericRepositoryQueryWithParameter()
        {
            var disabled = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: false).Query(new SimpleQueryParameter(), typeof(SimpleQueryParameter));
            Assert.IsTrue(disabled is EnumerableQuery<SimpleEntity>, $"Unexpected result type {disabled.GetType()} when the optimization is disabled.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(disabled.ToList()));

            var enabled = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: true).Query(new SimpleQueryParameter(), typeof(SimpleQueryParameter));
            Assert.IsTrue(enabled is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {enabled.GetType()} when the optimization is enabled.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(enabled.ToList()));
        }

        /// <summary>
        /// GenericRepository.FilterOrQuery, filtering option "queryable Filter(items, parameter)".
        /// </summary>
        [TestMethod]
        public void GenericRepositoryQueryableFilterOnItems()
        {
            var items = TestItems().AsQueryable();

            var disabled = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: false).FilterOrQuery(items, new SimpleFilter());
            Assert.IsTrue(disabled is EnumerableQuery<SimpleEntity>, $"Unexpected result type {disabled.GetType()} when the optimization is disabled.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(disabled.ToList()));

            var enabled = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: true).FilterOrQuery(items, new SimpleFilter());
            Assert.IsTrue(enabled is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {enabled.GetType()} when the optimization is enabled.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(enabled.ToList()));
        }

        /// <summary>
        /// The runtime options are not available in the build-time dependency injection container.
        /// </summary>
        [TestMethod]
        public void GenericRepositoryWithoutRuntimeOptions()
        {
            var repository = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: null);

            var result = repository.Query(new SimpleFilter(), typeof(SimpleFilter));

            Assert.IsTrue(result is EnumerableQuery<SimpleEntity>, $"Unexpected result type {result.GetType()}.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(result.ToList()));
        }

        /// <summary>
        /// The optimized query must be materialized by ReflectionHelper.MaterializeEntityList, same as any other query.
        /// </summary>
        [TestMethod]
        public void GenericRepositoryMaterializesOptimizedQuery()
        {
            var repository = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: true);

            var result = repository.Filter(TestItems().AsQueryable(), new SimpleFilter());

            Assert.IsTrue(result is List<SimpleEntity>, $"Unexpected result type {result.GetType()}. GenericRepository.Filter should always return a materialized list.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(result));
        }

        /// <summary>
        /// Filtering an already optimized query must not wrap it again, and must return the same results.
        /// </summary>
        [TestMethod]
        public void GenericRepositoryChainedQueryableFilters()
        {
            var repository = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: true);

            var firstResult = repository.FilterOrQuery(TestItems().AsQueryable(), new SimpleFilter());
            Assert.IsTrue(firstResult is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {firstResult.GetType()}.");

            var secondResult = repository.FilterOrQuery(firstResult, new SimpleFilter());
            Assert.IsTrue(secondResult is InterpretedQueryable<SimpleEntity>, $"Unexpected result type {secondResult.GetType()}.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(secondResult.ToList()));
        }

        [TestMethod]
        public void GenericRepositoryFilterReturnsMaterializedList()
        {
            var repository = NewRepository(new SimpleRepository(), optimizeInMemoryQueryable: true);

            var result = repository.Filter(TestItems(), new SimpleFilter());

            Assert.IsTrue(result is List<SimpleEntity>, $"Unexpected result type {result.GetType()}. GenericRepository.Filter should always return a materialized list.");
            Assert.AreEqual("a1, a2", TestUtility.Dump(result));
        }

        #endregion
    }
}
