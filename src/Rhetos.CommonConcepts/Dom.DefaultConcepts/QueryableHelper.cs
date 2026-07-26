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

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Rhetos.Dom.DefaultConcepts
{
    /// <summary>
    /// Information on a single execution of an <see cref="InterpretedQueryable{T}"/> query,
    /// reported to <see cref="QueryableHelper.Telemetry"/> if it is set.
    /// </summary>
    public sealed class InterpretedQueryTelemetry
    {
        public InterpretedQueryTelemetry(string expressionShape, int sourceCount, TimeSpan elapsed, bool interpreted)
        {
            ExpressionShape = expressionShape;
            SourceCount = sourceCount;
            Elapsed = elapsed;
            Interpreted = interpreted;
        }

        /// <summary>
        /// The executed query expression.
        /// </summary>
        public string ExpressionShape { get; }

        /// <summary>
        /// Number of records in the source collection.
        /// </summary>
        public int SourceCount { get; }

        /// <summary>
        /// Total time of the query execution, including the expression analysis and compilation.
        /// </summary>
        public TimeSpan Elapsed { get; }

        /// <summary>
        /// True if the query was executed by the expression interpreter,
        /// false if it was executed by the standard <see cref="EnumerableQuery{T}"/> class.
        /// </summary>
        public bool Interpreted { get; }
    }

    /// <summary>
    /// Helper methods for optimizing in-memory queries.
    /// </summary>
    /// <remarks>
    /// Coverage map of the in-memory query optimization (see <see cref="InterpretedQueryable{T}"/>),
    /// applied at the places where an in-memory queryable is <b>created</b>:
    /// <list type="bullet">
    /// <item><description>
    /// <b>Empty queries created by the framework</b> (unconditional, see <see cref="EmptyInterpreted{T}"/>):
    /// the generated <c>Common.OrmRepositoryBase.Filter(query, ids)</c> method for an empty list of IDs,
    /// and the deny-all row permissions filter in <see cref="FilterExpression{T}.OptimizedWhere"/>.
    /// </description></item>
    /// <item><description>
    /// <b>Dynamic reading and filtering</b> (enabled by <see cref="CommonConceptsRuntimeOptions.OptimizeInMemoryQueryable"/>):
    /// <see cref="GenericRepository{TEntityInterface}"/> applies <see cref="OptimizeInMemoryQueryable{T}(IQueryable{T}, int)"/>
    /// on the results of the repository's <c>Query(parameter)</c> and <c>Filter</c> methods that it resolves by reflection.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static class QueryableHelper
    {
        /// <summary>
        /// Optional diagnostics callback, called on each execution of an <see cref="InterpretedQueryable{T}"/> query.
        /// It is null by default, and it should be left null in production, since it is called on each query execution.
        /// </summary>
        public static Action<InterpretedQueryTelemetry> Telemetry { get; set; }

        /// <summary>
        /// If the given query is a standard in-memory query (<see cref="EnumerableQuery{T}"/>) over an already
        /// materialized collection, returns an equivalent <see cref="InterpretedQueryable{T}"/> that executes
        /// the query with the expression interpreter, instead of compiling the expression tree to IL code
        /// on each execution. In any other case the given query is returned unchanged.
        /// </summary>
        /// <param name="query">Any queryable. ORM queries and queries over a lazy source are returned unchanged.</param>
        /// <param name="threshold">If the source has this many records or more, the standard <see cref="EnumerableQuery{T}"/> behavior is used.</param>
        /// <remarks>
        /// The source of the given query is never enumerated by this method,
        /// since the queries are often composed over a lazy iterator (see ReflectionHelper.AsQueryable).
        /// </remarks>
        public static IQueryable<T> OptimizeInMemoryQueryable<T>(IQueryable<T> query, int threshold)
        {
            if (query is IInterpretedQueryable)
                return query; // Already optimized.

            if (query is not EnumerableQuery)
                return query; // An ORM query or a custom queryable implementation.

            InterpretedQuerySource source = TryCreateSource(query, threshold);
            if (source == null)
                return query; // The source is not a materialized collection, or it could not be read.

            IQueryable interpretedSourceQuery = CreateSourceQueryable(source);

            if (query.Expression is ConstantExpression sourceConstant && ReferenceEquals(sourceConstant.Value, query))
                return (IQueryable<T>)interpretedSourceQuery; // The query has no operators applied, the element type is T.

            // Keeping the query operators that are already applied, and replacing the source of the query.
            Expression optimizedExpression = new InterpretedSourceRewriter(interpretedSourceQuery).Visit(query.Expression);
            return (IQueryable<T>)interpretedSourceQuery.Provider.CreateQuery(optimizedExpression);
        }

        /// <summary>
        /// Same as <see cref="OptimizeInMemoryQueryable{T}(IQueryable{T}, int)"/>, for use on code paths
        /// where the element type is not known at compile time.
        /// </summary>
        public static IEnumerable OptimizeInMemoryQueryable(IEnumerable items, int threshold)
        {
            if (items is IInterpretedQueryable)
                return items; // Already optimized.

            if (items is not EnumerableQuery)
                return items; // A materialized list, an ORM query or a custom queryable implementation.

            Type elementType = InterpretedQueryUtility.GetQueryElementType(items.GetType());
            if (elementType == null)
                return items;

            var optimize = _untypedOptimizers.GetOrAdd(elementType, CreateUntypedOptimizer);
            return optimize(items, threshold);
        }

        /// <summary>
        /// Returns an empty query that is executed by the expression interpreter, instead of compiling the query's
        /// expression tree to IL code on each execution. It is intended to be used by the framework and the generated
        /// code, instead of <c>Array.Empty&lt;T&gt;().AsQueryable()</c>.
        /// </summary>
        /// <remarks>
        /// In contrast to <see cref="OptimizeInMemoryQueryable{T}(IQueryable{T}, int)"/>, this optimization is
        /// unconditional (it is not controlled by <see cref="CommonConceptsRuntimeOptions.OptimizeInMemoryQueryable"/>):
        /// with zero records in the source, the interpreted execution returns exactly the same result as the standard
        /// <see cref="EnumerableQuery{T}"/> and is strictly cheaper, because the expression compilation cost is paid
        /// per query execution, not per record. Query expressions that cannot be interpreted still fall back
        /// to the standard behavior, see <see cref="InterpretedQueryable{T}"/>.
        /// <para>
        /// A single instance is cached for each element type: the returned query is immutable, it reads the source
        /// collection on each execution, and <c>Array.Empty&lt;T&gt;()</c> is a shared singleton instance.
        /// Composing additional query operators over the returned instance creates new instances,
        /// it does not modify the cached one.
        /// </para>
        /// </remarks>
        public static IQueryable<T> EmptyInterpreted<T>() => EmptyInterpretedQuery<T>.Instance;

        #region Implementation

        private static class EmptyInterpretedQuery<T>
        {
            /// <summary>
            /// The threshold is 1, so that the empty source (0 records) is always below the threshold,
            /// and the query is always interpreted.
            /// </summary>
            public static readonly InterpretedQueryable<T> Instance = new InterpretedQueryable<T>(Array.Empty<T>(), 1);
        }

        private static readonly ConcurrentDictionary<Type, Func<IEnumerable, int, IEnumerable>> _untypedOptimizers =
            new ConcurrentDictionary<Type, Func<IEnumerable, int, IEnumerable>>();

        private static readonly MethodInfo _optimizeUntypedMethod =
            typeof(QueryableHelper).GetMethod(nameof(OptimizeUntyped), BindingFlags.Static | BindingFlags.NonPublic);

        private static Func<IEnumerable, int, IEnumerable> CreateUntypedOptimizer(Type elementType)
            => _optimizeUntypedMethod.MakeGenericMethod(elementType).CreateDelegate<Func<IEnumerable, int, IEnumerable>>();

        private static IEnumerable OptimizeUntyped<T>(IEnumerable query, int threshold)
            => OptimizeInMemoryQueryable((IQueryable<T>)query, threshold);

        private static readonly ConcurrentDictionary<Type, Func<InterpretedQuerySource, IQueryable>> _sourceQueryableFactories =
            new ConcurrentDictionary<Type, Func<InterpretedQuerySource, IQueryable>>();

        private static readonly MethodInfo _createSourceQueryableMethod =
            typeof(QueryableHelper).GetMethod(nameof(CreateSourceQueryableGeneric), BindingFlags.Static | BindingFlags.NonPublic);

        private static IQueryable CreateSourceQueryable(InterpretedQuerySource source)
            => _sourceQueryableFactories.GetOrAdd(source.ElementType, CreateSourceQueryableFactory).Invoke(source);

        private static Func<InterpretedQuerySource, IQueryable> CreateSourceQueryableFactory(Type elementType)
            => _createSourceQueryableMethod.MakeGenericMethod(elementType).CreateDelegate<Func<InterpretedQuerySource, IQueryable>>();

#pragma warning disable CA1859 // The return type must match the delegate signature used by CreateSourceQueryableFactory.
        private static IQueryable CreateSourceQueryableGeneric<T>(InterpretedQuerySource source) => new InterpretedQueryable<T>(source);
#pragma warning restore CA1859

        private static InterpretedQuerySource TryCreateSource(IQueryable query, int threshold)
        {
            EnumerableQuery sourceQuery = FindSourceQuery(query.Expression);
            if (sourceQuery == null)
                return null;

            IEnumerable items = GetSourceItems(sourceQuery);
            if (items == null)
                return null;

            Type elementType = InterpretedQueryUtility.GetQueryElementType(sourceQuery.GetType());
            if (elementType == null)
                return null;

            int count = GetKnownCount(items, elementType);
            if (count < 0)
                return null; // The source is not materialized. It must not be enumerated here.

            return new InterpretedQuerySource(items, elementType, count, threshold);
        }

        /// <summary>
        /// Returns the single <see cref="EnumerableQuery{T}"/> that is the source of the given query expression,
        /// or null if the expression contains a different number of in-memory queries.
        /// </summary>
        private static EnumerableQuery FindSourceQuery(Expression expression)
        {
            var finder = new SourceQueryFinder();
            finder.Visit(expression);
            return finder.SourceQueries.Count == 1 ? finder.SourceQueries[0] : null;
        }

        private sealed class SourceQueryFinder : ExpressionVisitor
        {
            public List<EnumerableQuery> SourceQueries { get; } = new List<EnumerableQuery>();

            protected override Expression VisitConstant(ConstantExpression node)
            {
                if (node.Value is EnumerableQuery sourceQuery)
                    SourceQueries.Add(sourceQuery);
                return node;
            }
        }

        private sealed class InterpretedSourceRewriter : ExpressionVisitor
        {
            private readonly ConstantExpression _interpretedSource;

            public InterpretedSourceRewriter(IQueryable interpretedSourceQuery)
            {
                _interpretedSource = Expression.Constant(interpretedSourceQuery);
            }

            protected override Expression VisitConstant(ConstantExpression node)
                => node.Value is EnumerableQuery ? _interpretedSource : node;
        }

        private static readonly ConcurrentDictionary<Type, FieldInfo> _enumerableQuerySourceFields =
            new ConcurrentDictionary<Type, FieldInfo>();

        /// <summary>
        /// Returns the source collection of the given <see cref="EnumerableQuery{T}"/>, or null if it is not available.
        /// </summary>
        /// <remarks>
        /// The standard <see cref="EnumerableQuery{T}"/> class does not provide public access to its source,
        /// so a private field is read here. If the implementation of that class changes, this method returns null
        /// and the optimization is simply not applied.
        /// </remarks>
        private static IEnumerable GetSourceItems(EnumerableQuery query)
        {
            FieldInfo sourceField = _enumerableQuerySourceFields.GetOrAdd(query.GetType(),
                queryType => queryType.GetField("_enumerable", BindingFlags.Instance | BindingFlags.NonPublic));

            return sourceField?.GetValue(query) as IEnumerable;
        }

        /// <summary>
        /// Returns the number of records in the given collection, or -1 if the collection is not materialized.
        /// The collection is never enumerated here.
        /// </summary>
        private static int GetKnownCount(IEnumerable items, Type elementType)
        {
            if (items is ICollection collection)
                return collection.Count;

            Type readOnlyCollectionType = typeof(IReadOnlyCollection<>).MakeGenericType(elementType);
            if (readOnlyCollectionType.IsInstanceOfType(items))
                return (int)readOnlyCollectionType.GetProperty(nameof(IReadOnlyCollection<object>.Count)).GetValue(items);

            return -1;
        }

        #endregion
    }
}
