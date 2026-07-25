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
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Rhetos.Dom.DefaultConcepts
{
    /// <summary>
    /// Non-generic access to the shared source of an <see cref="InterpretedQueryable{T}"/>,
    /// needed when rewriting the query expression, where the element type is not known at compile time.
    /// </summary>
    internal interface IInterpretedQueryable
    {
        InterpretedQuerySource QuerySource { get; }
    }

    /// <summary>
    /// A materialized in-memory collection that is the source of an <see cref="InterpretedQueryable{T}"/>.
    /// The same instance is shared between all queries that are composed over the same source.
    /// </summary>
    internal sealed class InterpretedQuerySource
    {
        /// <summary>
        /// The source collection. It implements <c>IEnumerable&lt;<see cref="ElementType"/>&gt;</c>.
        /// </summary>
        public IEnumerable Items { get; }

        public Type ElementType { get; }

        /// <summary>
        /// Number of records in <see cref="Items"/> when the queryable was created.
        /// It is used only for deciding whether to interpret the query or to use the standard queryable behavior.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// The query is interpreted if <see cref="Count"/> is smaller than the threshold,
        /// otherwise the standard <see cref="EnumerableQuery{T}"/> behavior is used.
        /// </summary>
        public int Threshold { get; }

        private readonly Type _itemsInterfaceType;

        private IQueryable _standardQuery;

        public InterpretedQuerySource(IEnumerable items, Type elementType, int count, int threshold)
        {
            Items = items;
            ElementType = elementType;
            Count = count;
            Threshold = threshold;
            _itemsInterfaceType = typeof(IEnumerable<>).MakeGenericType(elementType);
        }

        /// <summary>
        /// <c>IEnumerable&lt;<see cref="ElementType"/>&gt;</c>, the declared type of the source constant
        /// in the rewritten query expression.
        /// </summary>
        public Type ItemsInterfaceType => _itemsInterfaceType;

        /// <summary>
        /// The standard .NET queryable over the same source.
        /// Executing a query over this instance results with exactly the same behavior as if the optimization was not applied.
        /// </summary>
        public IQueryable StandardQuery
        {
            get
            {
                // No need for locking: creating a duplicate instance would be harmless, since it is stateless.
                _standardQuery ??= (IQueryable)Activator.CreateInstance(typeof(EnumerableQuery<>).MakeGenericType(ElementType), Items);
                return _standardQuery;
            }
        }
    }

#pragma warning disable CA1710 // Identifiers should have correct suffix. This is a query, not a collection; same as the standard EnumerableQuery<T> class.

    /// <summary>
    /// An in-memory queryable that executes the query with the expression interpreter, instead of compiling
    /// the expression tree to IL code on each execution (see <see cref="LambdaExpression.Compile(bool)"/>).
    /// It is an alternative to the standard <see cref="EnumerableQuery{T}"/> class, intended for small data sets,
    /// where the query compilation takes more time than the query execution.
    /// </summary>
    /// <remarks>
    /// If the source has too many records (see <see cref="InterpretedQuerySource.Threshold"/>), or if the query
    /// expression contains anything that this class cannot interpret, the query is executed by the standard
    /// <see cref="EnumerableQuery{T}"/> class, resulting with exactly the same behavior as if this class was not used.
    /// <para>
    /// Same as <see cref="EnumerableQuery{T}"/>, this class implements <see cref="IOrderedQueryable{T}"/>
    /// (some parts of the framework use reflection to call <c>Queryable.ThenBy</c> on the result
    /// of a previous sorting operation), and it does not implement any collection interface
    /// (the framework checks for collection interfaces to detect if the data is already materialized).
    /// </para>
    /// </remarks>
    public sealed class InterpretedQueryable<T> : IOrderedQueryable<T>, IQueryProvider, IInterpretedQueryable
    {
        private readonly InterpretedQuerySource _source;

        private readonly Expression _expression;

        /// <param name="source">A materialized collection. It is not copied, the query reads the current content of the collection on each execution.</param>
        /// <param name="threshold">If the source has this many records or more, the query is executed by the standard <see cref="EnumerableQuery{T}"/> class.</param>
        public InterpretedQueryable(IReadOnlyCollection<T> source, int threshold)
        {
            ArgumentNullException.ThrowIfNull(source);
            _source = new InterpretedQuerySource(source, typeof(T), source.Count, threshold);
            _expression = Expression.Constant(this);
        }

        internal InterpretedQueryable(InterpretedQuerySource source)
        {
            _source = source;
            _expression = Expression.Constant(this);
        }

        internal InterpretedQueryable(InterpretedQuerySource source, Expression expression)
        {
            _source = source;
            _expression = expression;
        }

        InterpretedQuerySource IInterpretedQueryable.QuerySource => _source;

        /// <inheritdoc/>
        public Type ElementType => typeof(T);

        /// <inheritdoc/>
        public Expression Expression => _expression;

        /// <inheritdoc/>
        public IQueryProvider Provider => this;

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            object result = ExecuteExpression(_expression);
            if (result is IEnumerable<T> typedResult)
                return typedResult.GetEnumerator();
            return ((IEnumerable)result).Cast<T>().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            return new InterpretedQueryable<TElement>(_source, expression);
        }

        /// <inheritdoc/>
        public IQueryable CreateQuery(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            Type elementType = InterpretedQueryUtility.GetQueryElementType(expression.Type);
            if (elementType == null)
                throw new ArgumentException($"The expression type '{expression.Type}' is not a queryable type.", nameof(expression));

            return (IQueryable)Activator.CreateInstance(
                typeof(InterpretedQueryable<>).MakeGenericType(elementType),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [_source, expression],
                null);
        }

        /// <inheritdoc/>
        public TResult Execute<TResult>(Expression expression) => (TResult)ExecuteExpression(expression);

        /// <inheritdoc/>
        public object Execute(Expression expression) => ExecuteExpression(expression);

        private object ExecuteExpression(Expression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);

            var telemetry = QueryableHelper.Telemetry;
            var stopwatch = telemetry != null ? Stopwatch.StartNew() : null;

            bool interpreted;
            object result;

            if (expression is ConstantExpression sourceConstant && sourceConstant.Value is IInterpretedQueryable sourceQuery)
            {
                // Reading the source without any query operator applied.
                interpreted = true;
                result = sourceQuery.QuerySource.Items;
            }
            else
            {
                Expression interpretedExpression = _source.Count < _source.Threshold
                    ? InterpretedQueryRewriter.TryRewrite(expression)
                    : null;

                interpreted = interpretedExpression != null;

                result = interpreted
                    ? InterpretedQueryUtility.CompileAndExecute(interpretedExpression)
                    : ExecuteWithStandardQueryable(expression);
            }

            telemetry?.Invoke(new InterpretedQueryTelemetry(expression.ToString(), _source.Count, stopwatch.Elapsed, interpreted));
            return result;
        }

        /// <summary>
        /// Executes the query by the standard <see cref="EnumerableQuery{T}"/> class,
        /// resulting with exactly the same behavior as if this class was not used.
        /// </summary>
        private object ExecuteWithStandardQueryable(Expression expression)
        {
            Expression standardExpression = StandardQueryableRewriter.Rewrite(expression);
            IQueryProvider standardProvider = _source.StandardQuery.Provider;

            // A query that returns records is created (and executed later, on enumeration) instead of being executed here,
            // because EnumerableQuery executes the query operators only when enumerating the result.
            return InterpretedQueryUtility.GetQueryElementType(expression.Type) != null
                ? standardProvider.CreateQuery(standardExpression)
                : standardProvider.Execute(standardExpression);
        }

        /// <summary>
        /// Same format as <see cref="EnumerableQuery{T}"/>, for consistent logging.
        /// </summary>
        public override string ToString()
        {
            if (_expression is ConstantExpression sourceConstant && ReferenceEquals(sourceConstant.Value, this))
                return _source.Items?.ToString() ?? "null";
            return _expression.ToString();
        }
    }

#pragma warning restore CA1710 // Identifiers should have correct suffix

    internal static class InterpretedQueryUtility
    {
        /// <summary>
        /// Returns the element type of the given queryable type, or null if the type is not a queryable.
        /// </summary>
        public static Type GetQueryElementType(Type queryType)
        {
            if (queryType.IsGenericType)
            {
                Type definition = queryType.GetGenericTypeDefinition();
                if (definition == typeof(IQueryable<>) || definition == typeof(IOrderedQueryable<>))
                    return queryType.GetGenericArguments()[0];
            }

            foreach (Type interfaceType in queryType.GetInterfaces())
                if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IQueryable<>))
                    return interfaceType.GetGenericArguments()[0];

            return null;
        }

        /// <summary>
        /// Executes the expression with the expression interpreter, without compiling it to IL code.
        /// </summary>
        public static object CompileAndExecute(Expression body)
        {
            Expression boxedBody = body.Type == typeof(object) ? body : Expression.Convert(body, typeof(object));
            return Expression.Lambda<Func<object>>(boxedBody).Compile(preferInterpretation: true).Invoke();
        }
    }

    /// <summary>
    /// Converts a query expression that uses <see cref="Queryable"/> methods over an <see cref="IInterpretedQueryable"/>
    /// source, to an equivalent expression that uses <see cref="Enumerable"/> methods over the source collection,
    /// with the lambda expressions compiled by the expression interpreter.
    /// </summary>
    internal sealed class InterpretedQueryRewriter : ExpressionVisitor
    {
        private static readonly ConcurrentDictionary<MethodInfo, MethodInfo> _enumerableMethods = new ConcurrentDictionary<MethodInfo, MethodInfo>();

        private bool _failed;

        private InterpretedQueryRewriter()
        {
        }

        /// <summary>
        /// Returns null if the expression contains anything that cannot be interpreted by this class.
        /// The caller should then use the standard <see cref="EnumerableQuery{T}"/> behavior.
        /// </summary>
        public static Expression TryRewrite(Expression expression)
        {
            var rewriter = new InterpretedQueryRewriter();
            Expression result;
            try
            {
                result = rewriter.Visit(expression);
            }
            catch (ArgumentException)
            {
                // The rewritten expression could not be constructed, for example because of an unexpected method overload.
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }

            return rewriter._failed ? null : result;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is IInterpretedQueryable query)
            {
                var source = query.QuerySource;
                return Expression.Constant(source.Items, source.ItemsInterfaceType);
            }
            return node;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (_failed)
                return node;

            if (node.Method.DeclaringType != typeof(Queryable))
            {
                // Any other method (a custom query operator or an ORM extension) is not interpreted here.
                _failed = true;
                return node;
            }

            var arguments = new Expression[node.Arguments.Count];
            for (int i = 0; i < node.Arguments.Count; i++)
            {
                Expression argument = node.Arguments[i];

                LambdaExpression lambda = argument is UnaryExpression quote && quote.NodeType == ExpressionType.Quote
                    ? quote.Operand as LambdaExpression
                    : argument as LambdaExpression;

                arguments[i] = lambda != null ? CompileLambda(lambda) : Visit(argument);

                if (_failed)
                    return node;
            }

            MethodInfo enumerableMethod = _enumerableMethods.GetOrAdd(node.Method, FindEnumerableMethod);
            if (enumerableMethod == null)
            {
                _failed = true;
                return node;
            }

            return Expression.Call(null, enumerableMethod, arguments);
        }

        protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
        {
            // The lambda arguments of the Queryable methods are compiled by CompileLambda, without visiting them.
            // Any other lambda expression is not expected here, so the standard queryable behavior is used instead.
            _failed = true;
            return node;
        }

        /// <summary>
        /// Compiles the lambda expression with the expression interpreter, and embeds the resulting delegate
        /// in the query expression, so that the interpreter does not need to handle the lambda while executing the query.
        /// </summary>
        private Expression CompileLambda(LambdaExpression lambda)
        {
            try
            {
                Delegate compiledLambda = lambda.Compile(preferInterpretation: true);
                return Expression.Constant(compiledLambda, lambda.Type);
            }
            catch (InvalidOperationException)
            {
                // For example, if the lambda references a parameter from an enclosing lambda expression.
                _failed = true;
                return lambda;
            }
        }

        /// <summary>
        /// Returns the <see cref="Enumerable"/> method that matches the given <see cref="Queryable"/> method,
        /// or null if there is no matching method.
        /// </summary>
        private static MethodInfo FindEnumerableMethod(MethodInfo queryableMethod)
        {
            Type[] genericArguments = queryableMethod.IsGenericMethod ? queryableMethod.GetGenericArguments() : Type.EmptyTypes;
            Type[] parameterTypes = queryableMethod.GetParameters().Select(parameter => ToEnumerableType(parameter.ParameterType)).ToArray();

            foreach (MethodInfo candidate in typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (candidate.Name != queryableMethod.Name)
                    continue;

                MethodInfo method;
                if (candidate.IsGenericMethodDefinition)
                {
                    if (candidate.GetGenericArguments().Length != genericArguments.Length)
                        continue;
                    try
                    {
                        method = candidate.MakeGenericMethod(genericArguments);
                    }
                    catch (ArgumentException)
                    {
                        continue; // The generic arguments do not satisfy the constraints of the candidate method.
                    }
                }
                else
                {
                    if (genericArguments.Length != 0)
                        continue;
                    method = candidate;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;

                bool match = true;
                for (int i = 0; i < parameters.Length; i++)
                    if (parameters[i].ParameterType != parameterTypes[i])
                    {
                        match = false;
                        break;
                    }

                if (match)
                    return method;
            }

            return null;
        }

        /// <summary>
        /// Converts a parameter type of a <see cref="Queryable"/> method to the corresponding
        /// parameter type of the <see cref="Enumerable"/> method.
        /// </summary>
        private static Type ToEnumerableType(Type parameterType)
        {
            if (parameterType == typeof(IQueryable))
                return typeof(IEnumerable);

            if (parameterType.IsGenericType)
            {
                Type definition = parameterType.GetGenericTypeDefinition();
                if (definition == typeof(IQueryable<>))
                    return typeof(IEnumerable<>).MakeGenericType(parameterType.GetGenericArguments());
                if (definition == typeof(IOrderedQueryable<>))
                    return typeof(IOrderedEnumerable<>).MakeGenericType(parameterType.GetGenericArguments());
                if (definition == typeof(Expression<>))
                    return parameterType.GetGenericArguments()[0];
            }

            return parameterType;
        }
    }

    /// <summary>
    /// Replaces the <see cref="IInterpretedQueryable"/> source of the query with the standard
    /// <see cref="EnumerableQuery{T}"/> over the same collection, so that the query can be executed
    /// with the standard behavior.
    /// </summary>
    internal sealed class StandardQueryableRewriter : ExpressionVisitor
    {
        private StandardQueryableRewriter()
        {
        }

        public static Expression Rewrite(Expression expression) => new StandardQueryableRewriter().Visit(expression);

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is IInterpretedQueryable query)
                return Expression.Constant(query.QuerySource.StandardQuery);
            return node;
        }
    }
}
