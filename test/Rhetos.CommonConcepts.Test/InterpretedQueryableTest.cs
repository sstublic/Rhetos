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
using Rhetos.Processing.DefaultCommands;
using Rhetos.TestCommon;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Rhetos.CommonConcepts.Test
{
    /// <summary>
    /// Tests that <see cref="InterpretedQueryable{T}"/> returns the same results as the standard
    /// <see cref="EnumerableQuery{T}"/> (BCL implementation of <see cref="Queryable"/> over an in-memory list),
    /// including the framework's reflection-based query composition patterns.
    /// </summary>
    [TestClass]
    public class InterpretedQueryableTest
    {
        //=========================================================================
        #region Test data

        public interface INamed
        {
            string Name { get; }
        }

        public class Book : INamed
        {
            public Guid ID { get; set; }
            public string Name { get; set; }
            public int? Pages { get; set; }

            public override string ToString() => Name ?? "<null>";
        }

        public class BookInfo
        {
            public string Title { get; set; }
            public int Size { get; set; }

            public override string ToString() => $"{Title}/{Size}";
        }

        /// <summary>
        /// The wrapper falls back to the standard <see cref="EnumerableQuery{T}"/> behavior
        /// when the source row count reaches the threshold. Use this value to disable the fallback in tests.
        /// </summary>
        private const int NoFallbackThreshold = int.MaxValue;

        private static Guid Id(int index) => new Guid(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        private static List<Book> TestBooks() =>
        [
            new Book { ID = Id(1), Name = "a1", Pages = 100 },
            new Book { ID = Id(2), Name = "b1", Pages = 200 },
            new Book { ID = Id(3), Name = "a2", Pages = null },
            new Book { ID = Id(4), Name = null, Pages = 300 },
            new Book { ID = Id(5), Name = "b2", Pages = 200 },
        ];

        private static InterpretedQueryable<Book> NewWrapper(int threshold = NoFallbackThreshold)
            => new InterpretedQueryable<Book>(TestBooks(), threshold);

        /// <summary>
        /// Executes the same composed query on the standard <see cref="EnumerableQuery{T}"/>
        /// and on <see cref="InterpretedQueryable{T}"/>, and asserts that the results are the same.
        /// </summary>
        private static void AssertSameAsEnumerableQuery<TResult>(Func<IQueryable<Book>, TResult> composeQuery, int threshold = NoFallbackThreshold)
        {
            string expected = Format(composeQuery(TestBooks().AsQueryable()));
            string actual = Format(composeQuery(new InterpretedQueryable<Book>(TestBooks(), threshold)));
            Console.WriteLine($"[AssertSameAsEnumerableQuery] '{expected}'");
            Assert.AreEqual(expected, actual);
        }

        private static string Format(object value)
        {
            if (value is string text)
                return text;
            if (value is IEnumerable sequence)
                return string.Join(", ", sequence.Cast<object>().Select(item => item?.ToString() ?? "<null>"));
            return value?.ToString() ?? "<null>";
        }

        private static List<InterpretedQueryTelemetry> RecordTelemetry(Action action)
        {
            var records = new List<InterpretedQueryTelemetry>();
            QueryableHelper.Telemetry = records.Add;
            try
            {
                action();
            }
            finally
            {
                QueryableHelper.Telemetry = null;
            }
            foreach (var record in records)
                Console.WriteLine($"[Telemetry] Interpreted={record.Interpreted}, SourceCount={record.SourceCount}, Elapsed={record.Elapsed}, Query={record.ExpressionShape}");
            return records;
        }

        #endregion
        //=========================================================================
        #region Standard query operators

        [TestMethod]
        public void StackedWhere()
        {
            AssertSameAsEnumerableQuery(query => query
                .Where(book => book.Name != null)
                .Where(book => book.Pages != null && book.Pages > 100)
                .Where(book => book.Name.StartsWith("b", StringComparison.Ordinal))
                .ToList());
        }

        [TestMethod]
        public void WhereWithNullHandling()
        {
            AssertSameAsEnumerableQuery(query => query
                .Where(book => book.Name == null || book.Pages == null)
                .ToList());

            AssertSameAsEnumerableQuery(query => query
                .Where(book => (book.Pages ?? 0) >= 200)
                .Select(book => book.Name ?? "<null>")
                .ToList());
        }

        [TestMethod]
        public void OrderByThenBy()
        {
            AssertSameAsEnumerableQuery(query => query.OrderBy(book => book.Pages).ThenBy(book => book.Name).ToList());
            AssertSameAsEnumerableQuery(query => query.OrderBy(book => book.Pages).ThenByDescending(book => book.Name).ToList());
            AssertSameAsEnumerableQuery(query => query.OrderByDescending(book => book.Pages).ThenBy(book => book.Name).ToList());
        }

        [TestMethod]
        public void SkipAndTake()
        {
            AssertSameAsEnumerableQuery(query => query.OrderBy(book => book.ID).Skip(1).Take(2).ToList());
            AssertSameAsEnumerableQuery(query => query.OrderBy(book => book.ID).Skip(10).Take(2).ToList());
            AssertSameAsEnumerableQuery(query => query.OrderBy(book => book.ID).Take(0).ToList());
        }

        [TestMethod]
        public void SelectToOtherType()
        {
            AssertSameAsEnumerableQuery(query => query
                .Where(book => book.Name != null)
                .Select(book => new BookInfo { Title = book.Name, Size = book.Pages ?? 0 })
                .ToList());
        }

        [TestMethod]
        public void SelectToAnonymousType()
        {
            AssertSameAsEnumerableQuery(query => query
                .Where(book => book.Name != null)
                .Select(book => new { book.Name, book.Pages })
                .ToList());
        }

        [TestMethod]
        public void ScalarResults()
        {
            AssertSameAsEnumerableQuery(query => query.Count());
            AssertSameAsEnumerableQuery(query => query.Count(book => book.Name != null));
            AssertSameAsEnumerableQuery(query => query.Any());
            AssertSameAsEnumerableQuery(query => query.Any(book => book.Pages == 300));
            AssertSameAsEnumerableQuery(query => query.OrderBy(book => book.ID).First());
            AssertSameAsEnumerableQuery(query => query.Where(book => book.Name == "nonexistent").FirstOrDefault());
            AssertSameAsEnumerableQuery(query => query.Single(book => book.Pages == 300));
            AssertSameAsEnumerableQuery(query => query.Select(book => book.Pages ?? 0).Sum());
        }

        [TestMethod]
        public void CastToBaseType()
        {
            AssertSameAsEnumerableQuery(query => query.Cast<INamed>().Select(item => item.Name ?? "<null>").ToList());
            AssertSameAsEnumerableQuery(query => query.Where(book => book.Name != null).Cast<INamed>().ToList());
        }

        [TestMethod]
        public void ValueTypeElements()
        {
            List<int> source = [5, 1, 4, 1, 3];

            var expected = source.AsQueryable().Where(item => item > 1).OrderBy(item => item).Skip(1).ToList();
            var actual = new InterpretedQueryable<int>(source, NoFallbackThreshold).Where(item => item > 1).OrderBy(item => item).Skip(1).ToList();

            Assert.AreEqual(TestUtility.Dump(expected), TestUtility.Dump(actual));
            Assert.AreEqual(source.AsQueryable().Sum(), new InterpretedQueryable<int>(source, NoFallbackThreshold).Sum());
        }

        [TestMethod]
        public void EmptySource()
        {
            List<Book> empty = [];

            var expected = empty.AsQueryable().Where(book => book.Name != null).OrderBy(book => book.Name).ToList();
            var actual = new InterpretedQueryable<Book>(empty, NoFallbackThreshold).Where(book => book.Name != null).OrderBy(book => book.Name).ToList();

            Assert.AreEqual(TestUtility.Dump(expected), TestUtility.Dump(actual));
            Assert.AreEqual(0, new InterpretedQueryable<Book>(empty, NoFallbackThreshold).Count());
            Assert.IsFalse(new InterpretedQueryable<Book>(empty, NoFallbackThreshold).Any());
        }

        [TestMethod]
        public void ContainsWithClosureCapturedList()
        {
            List<Guid> ids = [Id(1), Id(3), Id(9)];

            AssertSameAsEnumerableQuery(query => query
                .Where(book => ids.Contains(book.ID))
                .OrderBy(book => book.Name)
                .ToList());
        }

        #endregion
        //=========================================================================
        #region Framework reflection patterns

        /// <summary>
        /// Mirrors ReflectionHelper.Where: Queryable.Where is invoked by reflection with a supertype generic argument,
        /// on a query with a subtype element, and the result is cast back to the original element type.
        /// </summary>
        [TestMethod]
        public void SupertypeGenericWhereByReflection()
        {
            var whereMethod = typeof(Queryable).GetMethods()
                .Where(method => method.Name == "Where" && method.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2)
                .Single()
                .MakeGenericMethod(typeof(INamed));

            Expression<Func<INamed, bool>> predicate = item => item.Name != null && item.Name.StartsWith("a", StringComparison.Ordinal);

            AssertSameAsEnumerableQuery(query =>
            {
                var filtered = (IQueryable<INamed>)whereMethod.Invoke(null, [query, predicate]);
                return filtered.Cast<Book>().OrderBy(book => book.Name).ToList();
            });
        }

        /// <summary>
        /// Mirrors GenericFilterHelper.Sort: Queryable.OrderBy and Queryable.ThenBy are invoked by reflection,
        /// where ThenBy requires the previous result to implement IOrderedQueryable.
        /// </summary>
        [TestMethod]
        public void OrderByThenByByReflection()
        {
            AssertSameAsEnumerableQuery(query =>
            {
                var sorted = GenericFilterHelper.Sort(query, "Pages", ascending: true, firstProperty: true);
                Assert.IsTrue(sorted is IOrderedQueryable<Book>, $"Sorted query {sorted.GetType()} must implement IOrderedQueryable<T>, otherwise the reflection call of Queryable.ThenBy fails.");
                sorted = GenericFilterHelper.Sort(sorted, "Name", ascending: false, firstProperty: false);
                return sorted.ToList();
            });
        }

        /// <summary>
        /// GenericFilterHelper.SortAndPaginate is used on the standard read pipeline.
        /// It uses reflection for sorting and for Skip/Take, based on the element type of the query.
        /// </summary>
        [TestMethod]
        public void SortAndPaginateOnReadCommand()
        {
            var commandInfo = new ReadCommandInfo
            {
                DataSource = "Test.Book",
                ReadRecords = true,
                OrderByProperties =
                [
                    new OrderByProperty { Property = "Pages", Descending = true },
                    new OrderByProperty { Property = "ID", Descending = false },
                ],
                Skip = 1,
                Top = 2,
            };

            AssertSameAsEnumerableQuery(query => GenericFilterHelper.SortAndPaginate(query, commandInfo).ToList());
        }

        [TestMethod]
        public void QueryableInterfaceIsUnambiguous()
        {
            var wrapper = NewWrapper();

            var queryableInterface = wrapper.GetType().GetInterface("IQueryable`1");
            Assert.AreEqual(typeof(IQueryable<Book>), queryableInterface);

            var enumerableInterface = wrapper.GetType().GetInterface("IEnumerable`1");
            Assert.AreEqual(typeof(IEnumerable<Book>), enumerableInterface);

            Assert.IsTrue(wrapper is IOrderedQueryable<Book>, "The wrapper must implement IOrderedQueryable<T>, same as EnumerableQuery<T>.");
            Assert.IsTrue(wrapper is IQueryable, "The wrapper must implement IQueryable.");
            Assert.IsTrue(wrapper.GetType().IsPublic, "The wrapper class must be public, for reflection-based overload resolution in the generated object model.");
        }

        /// <summary>
        /// The generated object model resolves the ToSimple method overload by the runtime type of the query,
        /// using the default reflection binder (see DomInitializationCodeGenerator, GenericToSimple).
        /// </summary>
        [TestMethod]
        public void ToSimpleOverloadResolution()
        {
            foreach (IQueryable<Book> query in new IQueryable<Book>[] { TestBooks().AsQueryable(), NewWrapper() })
            {
                var method = typeof(QueryExtensionsStub).GetMethod("ToSimple", BindingFlags.Static | BindingFlags.Public, null, [query.GetType()], null);

                Assert.IsNotNull(method, $"Cannot find 'ToSimple' method for argument type '{query.GetType()}'.");
                Assert.AreEqual(typeof(IQueryable<Book>), method.GetParameters()[0].ParameterType);
            }
        }

        public static class QueryExtensionsStub
        {
            public static IQueryable<Book> ToSimple(IQueryable<Book> query) => query;

            public static IQueryable<BookInfo> ToSimple(IQueryable<BookInfo> query) => query;
        }

        [TestMethod]
        public void WrapperIsNotMaterializedCollection()
        {
            var wrapper = NewWrapper();
            var interfaces = wrapper.GetType().GetInterfaces();
            Console.WriteLine(TestUtility.DumpSorted(interfaces, item => item.Name));

            Type[] collectionInterfaces = [typeof(IList), typeof(ICollection), typeof(IList<>), typeof(ICollection<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>)];

            var implementedCollectionInterfaces = interfaces
                .Where(item => collectionInterfaces.Contains(item.IsGenericType ? item.GetGenericTypeDefinition() : item));

            Assert.AreEqual("", TestUtility.DumpSorted(implementedCollectionInterfaces, item => item.Name),
                "The wrapper must not be recognized as a materialized collection (IList checks in GenericRepository tests, CsUtility.Materialized).");
        }

        [TestMethod]
        public void ToStringSameAsEnumerableQuery()
        {
            var source = TestBooks();

            Assert.AreEqual(source.AsQueryable().ToString(), new InterpretedQueryable<Book>(source, NoFallbackThreshold).ToString());

            // The composed query prints the expression tree, where the source constant prints the source collection (see above).
            string expected = source.AsQueryable().Where(book => book.Name != null).ToString();
            string actual = new InterpretedQueryable<Book>(source, NoFallbackThreshold).Where(book => book.Name != null).ToString();
            Console.WriteLine(expected);
            Console.WriteLine(actual);
            Assert.AreEqual(expected, actual);
        }

        #endregion
        //=========================================================================
        #region Deferred execution

        [TestMethod]
        public void DeferredExecutionReadsSourceOnEachEnumeration()
        {
            var source = TestBooks();
            var referenceSource = TestBooks();

            var query = new InterpretedQueryable<Book>(source, NoFallbackThreshold).Where(book => book.Name != null).OrderBy(book => book.Name);
            var referenceQuery = referenceSource.AsQueryable().Where(book => book.Name != null).OrderBy(book => book.Name);

            Assert.AreEqual(TestUtility.Dump(referenceQuery.ToList()), TestUtility.Dump(query.ToList()));

            source.Add(new Book { ID = Id(6), Name = "a3", Pages = 400 });
            referenceSource.Add(new Book { ID = Id(6), Name = "a3", Pages = 400 });

            Assert.AreEqual("a1, a2, a3, b1, b2", TestUtility.Dump(referenceQuery.ToList()));
            Assert.AreEqual(TestUtility.Dump(referenceQuery.ToList()), TestUtility.Dump(query.ToList()));
        }

        [TestMethod]
        public void RecompositionAfterPartialEnumeration()
        {
            var source = TestBooks();
            var query = new InterpretedQueryable<Book>(source, NoFallbackThreshold).Where(book => book.Name != null);

            using (var enumerator = query.GetEnumerator())
            {
                Assert.IsTrue(enumerator.MoveNext());
                Assert.AreEqual("a1", enumerator.Current.ToString());
            }

            Assert.AreEqual("b2, b1", TestUtility.Dump(query.OrderByDescending(book => book.Name).Take(2).ToList()));
            Assert.AreEqual("a1, b1, a2, b2", TestUtility.Dump(query.ToList()));
        }

        #endregion
        //=========================================================================
        #region Fallback to the standard EnumerableQuery behavior

        [TestMethod]
        public void FallbackOnThreshold()
        {
            var source = TestBooks();
            Assert.AreEqual(5, source.Count);

            List<Book> withFallback = null;
            List<Book> interpreted = null;

            var telemetry = RecordTelemetry(() =>
            {
                withFallback = new InterpretedQueryable<Book>(source, 5).Where(book => book.Pages == 200).ToList();
                interpreted = new InterpretedQueryable<Book>(source, 6).Where(book => book.Pages == 200).ToList();
            });

            Assert.AreEqual("b1, b2", TestUtility.Dump(withFallback));
            Assert.AreEqual("b1, b2", TestUtility.Dump(interpreted));

            Assert.AreEqual("False, True", TestUtility.Dump(telemetry, record => record.Interpreted));
            Assert.AreEqual("5, 5", TestUtility.Dump(telemetry, record => record.SourceCount));
            TestUtility.AssertContains(telemetry[0].ExpressionShape, "Where");
        }

        /// <summary>
        /// Any expression that the interpreting rewriter does not understand must be executed
        /// by the standard <see cref="EnumerableQuery{T}"/> provider, with the same result.
        /// </summary>
        [TestMethod]
        public void FallbackOnUnsupportedExpression()
        {
            List<Book> withFallback = null;

            var telemetry = RecordTelemetry(() =>
            {
                var query = new InterpretedQueryable<Book>(TestBooks(), NoFallbackThreshold).Where(book => book.Name != null);
                withFallback = CustomQueryOperator.FirstTwo(query).ToList();
            });

            var referenceQuery = TestBooks().AsQueryable().Where(book => book.Name != null);
            var expected = CustomQueryOperator.FirstTwo(referenceQuery).ToList();

            Assert.AreEqual(TestUtility.Dump(expected), TestUtility.Dump(withFallback));
            Assert.AreEqual("False", TestUtility.Dump(telemetry, record => record.Interpreted));
        }

        /// <summary>
        /// A custom query operator that is not a <see cref="Queryable"/> method.
        /// The interpreting rewriter cannot map it, but the standard <see cref="EnumerableQuery{T}"/>
        /// rewriter finds the IEnumerable overload by name.
        /// </summary>
        public static class CustomQueryOperator
        {
            private static readonly MethodInfo QueryableOverload = typeof(CustomQueryOperator).GetMethods()
                .Single(method => method.Name == nameof(FirstTwo) && method.GetParameters()[0].ParameterType == typeof(IQueryable<Book>));

            public static IQueryable<Book> FirstTwo(IQueryable<Book> source)
                => source.Provider.CreateQuery<Book>(Expression.Call(QueryableOverload, source.Expression));

            public static IEnumerable<Book> FirstTwo(IEnumerable<Book> source)
                => source.Take(2);
        }

        #endregion
        //=========================================================================
        #region Entity Framework 6 compatibility

        /// <summary>
        /// EFExpression.OptimizeContains rewrites `ids.Contains(item.ID)` into a call of the private static
        /// method EFExpression.ContainsIds. This test verifies that the expression interpreter can execute
        /// such expression in memory, since the interpreter is used instead of the IL compiler
        /// by <see cref="InterpretedQueryable{T}"/>.
        /// </summary>
        [TestMethod]
        public void Ef6OptimizeContainsCanBeInterpreted()
        {
            List<Guid> ids = [Id(1), Id(3)];
            Expression<Func<Book, bool>> predicate = book => ids.Contains(book.ID);

            var optimized = new Ef6OrmUtility().OptimizeContains(predicate);
            Console.WriteLine(optimized.ToString());
            TestUtility.AssertContains(optimized.ToString(), "ContainsIds",
                "EFExpression.OptimizeContains is expected to replace List.Contains with the private static EFExpression.ContainsIds method.");

            var interpretedPredicate = optimized.Compile(preferInterpretation: true);
            Assert.AreEqual("a1, a2", TestUtility.Dump(TestBooks().Where(interpretedPredicate).ToList()));

            var query = new InterpretedQueryable<Book>(TestBooks(), NoFallbackThreshold).Where(optimized);
            Assert.AreEqual("a1, a2", TestUtility.Dump(query.OrderBy(book => book.Name).ToList()));
        }

        #endregion
    }
}
