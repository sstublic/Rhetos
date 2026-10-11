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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Rhetos.Dom.DefaultConcepts
{
    /// <summary>
    /// Helper methods for the 'contains' operation in LINQ queries, with the same API as in the EF6 provider.
    /// </summary>
    public static class EFExpression
    {
        /// <summary>
        /// Optimized alternative to LINQ operation: <c>query.Where(item => ids.Contains(memberSelector))</c>.
        /// On an EF Core query, a list of 200 or more IDs is sent to the database as one collection parameter
        /// (SQL Server: a JSON parameter with OPENJSON, PostgreSQL: an array parameter).
        /// A smaller list keeps EF Core's default translation of the 'contains' operation.
        /// </summary>
        /// <remarks>
        /// The OPENJSON translation on SQL Server requires the database compatibility level 130 or higher.
        /// </remarks>
        public static IQueryable<T> WhereContains<T>(this IQueryable<T> query, List<Guid> ids, Expression<Func<T, Guid>> memberSelector)
        {
            Expression<Func<List<Guid>>> idsLambda = () => ids;
            var idsContainsExpression = (Expression<Func<T, bool>>)Expression.Lambda(
                Expression.Call(
                    idsLambda.Body,
                    ListOfGuidContainsMethod,
                    memberSelector.Body),
                memberSelector.Parameters.Single());
            return query.Where((Expression<Func<T, bool>>)OptimizeContains(query, idsContainsExpression));
        }

        static readonly MethodInfo ListOfGuidContainsMethod = typeof(List<Guid>).GetMethod("Contains");

        /// <summary>
        /// This method is available for backward compatibility with EF6, which required certain optimizations for the 'contains' operation.
        /// It simply returns the given expression,
        /// since EF Core has internal optimizations for the 'contains' operation.
        /// The expression is expected to be in format <c>item => ids.Contains(memberSelector)</c>.
        /// </summary>
        public static Expression<Func<T, bool>> OptimizeContains<T>(Expression<Func<T, bool>> expressionToOptimize)
        {
            return expressionToOptimize;
        }

        /// <summary>
        /// This method is available for backward compatibility with EF6, which required certain optimizations for the 'contains' operation.
        /// It simply returns the given expression,
        /// since EF Core has internal optimizations for the 'contains' operation.
        /// The expression is expected to be in format <c>item => ids.Contains(memberSelector)</c>.
        /// </summary>
        /// <remarks>
        /// This is an untyped version of <see cref="OptimizeContains{T}(Expression{Func{T, bool}})"/> method,
        /// for when it is not convenient the use the generic method.
        /// </remarks>
        public static Expression OptimizeContains(Expression expressionToOptimize)
        {
            return expressionToOptimize;
        }

        /// <summary>
        /// On an EF Core query, returns the predicate where each list of 200 or more IDs (Guid or nullable Guid elements)
        /// in a 'contains' operation is sent to the database as one collection parameter. Smaller lists, other element types,
        /// and queries that are not translated by EF Core keep the predicate unchanged.
        /// </summary>
        internal static Expression OptimizeContains(IQueryable query, Expression predicate) =>
            // EF.Parameter throws if evaluated outside EF Core query translation, for example on a query of in-memory items.
            query.Provider is IAsyncQueryProvider
                ? new LargeIdListToCollectionParameterVisitor().Visit(predicate)
                : predicate;

        // Measured on SQL Server: below this count, EF Core's default translation (one parameter per ID) is faster on warm queries.
        // From this count, one collection parameter is faster, and avoids a separate SQL query plan for each padded list size.
        private const int MinIdCountForCollectionParameter = 200;

        private sealed class LargeIdListToCollectionParameterVisitor : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var visited = (MethodCallExpression)base.VisitMethodCall(node);
                return visited switch
                {
                    { Method.Name: nameof(Enumerable.Contains), Object: null, Arguments: [var ids, var id] } when IsLargeIdList(ids, id.Type)
                        => visited.Update(null, [CreateCollectionParameter(ids), id]),
                    { Method.Name: nameof(Enumerable.Contains), Object: { } ids, Arguments: [var id] } when IsLargeIdList(ids, id.Type)
                        => visited.Update(CreateCollectionParameter(ids), visited.Arguments),
                    _ => visited,
                };
            }
        }

        private static bool IsLargeIdList(Expression list, Type idType) =>
            (idType == typeof(Guid) || idType == typeof(Guid?))
            && EvaluateClosureValue(list) is ICollection { Count: >= MinIdCountForCollectionParameter };

        /// <summary>
        /// Returns the value of a closure member, which is the expression of a list captured in a lambda expression
        /// or created by <see cref="EfCoreOrmUtility.CreateContainsItemsExpression"/>. Returns null for any other expression,
        /// for example a list that is already wrapped in <see cref="EF.Parameter"/>.
        /// </summary>
        private static object EvaluateClosureValue(Expression expression) => expression switch
        {
            MemberExpression { Expression: ConstantExpression closure, Member: FieldInfo field } => field.GetValue(closure.Value),
            MemberExpression { Expression: ConstantExpression closure, Member: PropertyInfo property } => property.GetValue(closure.Value),
            UnaryExpression { NodeType: ExpressionType.Convert, Method: null } conversion => EvaluateClosureValue(conversion.Operand),
            _ => null,
        };

        private static MethodCallExpression CreateCollectionParameter(Expression list) =>
            Expression.Call(EFParameterMethod.MakeGenericMethod(list.Type), list);

        static readonly MethodInfo EFParameterMethod = typeof(EF).GetMethod(nameof(EF.Parameter));
    }
}
