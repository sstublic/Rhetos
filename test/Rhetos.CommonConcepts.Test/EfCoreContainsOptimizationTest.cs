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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhetos.CommonConcepts.Test.Mocks;
using Rhetos.CommonConcepts.Test.Tools;
using Rhetos.Dom.DefaultConcepts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace Rhetos.CommonConcepts.Test
{
    /// <summary>
    /// EF Core implementation of <see cref="IOrmUtility.WhereContains"/> and <see cref="IOrmUtility.OptimizeContains(IQueryable, Expression)"/>.
    /// </summary>
    [TestClass]
    public class EfCoreContainsOptimizationTest
    {
        private static readonly IOrmUtility OrmUtility = new EfCoreOrmUtility();

        [TestMethod]
        [DataRow(199)]
        [DataRow(200)]
        public void WhereContainsInMemoryQuery(int idCount)
        {
            var items = CreateItems(idCount + 1);
            var requestedItems = items.Take(idCount).ToList();

            var filtered = OrmUtility.WhereContains(items.AsQueryable(), requestedItems.Select(item => item.ID).ToList(), item => item.ID);

            CollectionAssert.AreEqual(requestedItems, filtered.ToList());
        }

        [TestMethod]
        [DataRow(199, "IN, parameters: 200")] // EF Core default translation: one parameter per ID, padded to a multiple of 50 in this range.
        [DataRow(200, "OPENJSON, parameters: 1")]
        public void WhereContainsSqlServerTranslation(int idCount, string expectedTranslation)
        {
            using var dbContext = new SqlServerDbContext();
            var ids = CreateItems(idCount).Select(item => item.ID).ToList();

            string sql = OrmUtility.WhereContains(dbContext.Items, ids, item => item.ID).ToQueryString();

            Assert.AreEqual(expectedTranslation, SummarizeListTranslation(sql), sql);
        }

        [TestMethod]
        public void GenericInFilterInMemoryQuery()
        {
            var items = CreateItems(201);
            var requestedItems = items.Take(200).ToList();
            var query = items.AsQueryable();
            var predicate = CreateGenericInFilterPredicate(nameof(Item.ID), requestedItems.Select(item => item.ID).ToList());

            var optimizedPredicate = OrmUtility.OptimizeContains(query, predicate);

            Assert.AreSame(predicate, optimizedPredicate);
            CollectionAssert.AreEqual(requestedItems, query.Where((Expression<Func<Item, bool>>)optimizedPredicate).ToList());
        }

        [TestMethod]
        [DataRow(nameof(Item.ID), "In", 199, "IN, parameters: 200")]
        [DataRow(nameof(Item.ID), "In", 200, "OPENJSON, parameters: 1")]
        [DataRow(nameof(Item.ID), "NotIn", 200, "OPENJSON, parameters: 1")]
        [DataRow(nameof(Item.ParentID), "In", 200, "OPENJSON, parameters: 1")]
        [DataRow(nameof(Item.Name), "In", 300, "IN, parameters: 300")] // Only lists of IDs are sent as one collection parameter.
        public void GenericInFilterSqlServerTranslation(string property, string operation, int valueCount, string expectedTranslation)
        {
            using var dbContext = new SqlServerDbContext();
            var predicate = CreateGenericInFilterPredicate(property, CreateInFilterValues(property, valueCount), operation);

            var optimizedPredicate = (Expression<Func<Item, bool>>)OrmUtility.OptimizeContains(dbContext.Items, predicate);

            string sql = dbContext.Items.Where(optimizedPredicate).ToQueryString();
            Assert.AreEqual(expectedTranslation, SummarizeListTranslation(sql), sql);
        }

        [TestMethod]
        public void GenericRepositorySqlServerTranslation()
        {
            using var dbContext = new SqlServerDbContext();
            var repository = new TestGenericRepository<Item, Item>(new ItemRepository(dbContext), OrmUtility);
            var ids = CreateItems(200).Select(item => item.ID).ToList();
            var genericInFilter = new FilterCriteria(nameof(Item.ID), "In", ids);

            var queries = new (string Name, IEnumerable<Item> Query)[]
            {
                ("Query(generic In)", repository.Query(genericInFilter)),
                ("Query(ids)", repository.Query(ids)),
                ("FilterOrQuery(query, generic In)", repository.FilterOrQuery(dbContext.Items, genericInFilter)),
                ("FilterOrQuery(query, ids)", repository.FilterOrQuery(dbContext.Items, ids)),
            };

            Assert.AreEqual(
                string.Join(Environment.NewLine, queries.Select(query => $"{query.Name}: OPENJSON, parameters: 1")),
                string.Join(Environment.NewLine, queries.Select(query => $"{query.Name}: {SummarizeListTranslation(((IQueryable)query.Query).ToQueryString())}")));
        }

        public class Item : IEntity
        {
            public Guid ID { get; set; }
            public Guid? ParentID { get; set; }
            public string Name { get; set; }
        }

        private static List<Item> CreateItems(int count) =>
            Enumerable.Range(0, count).Select(_ => new Item { ID = Guid.NewGuid() }).ToList();

        private static object CreateInFilterValues(string property, int count) =>
            property == nameof(Item.Name)
                ? Enumerable.Range(0, count).Select(index => "name" + index).ToList()
                : CreateItems(count).Select(item => item.ID).ToList();

        private static Expression<Func<Item, bool>> CreateGenericInFilterPredicate(string property, object values, string operation = "In") =>
            Factory.CreateGenericFilterHelper(new DataStructureReadParametersStub(), OrmUtility)
                .ToExpression<Item>([new PropertyFilter { Property = property, Operation = operation, Value = values }]);

        /// <summary>
        /// A repository without filter methods, so the generic repository applies the generic filters to the query.
        /// </summary>
        private sealed class ItemRepository(SqlServerDbContext dbContext) : IRepository
        {
            public IQueryable<Item> Query() => dbContext.Items;
        }

        /// <summary>
        /// Translates queries to SQL without connecting to the database.
        /// </summary>
        private sealed class SqlServerDbContext : DbContext
        {
            public DbSet<Item> Items => Set<Item>();

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
                optionsBuilder.UseSqlServer("Server=NotConnected");
        }

        /// <summary>
        /// Returns how the SQL query sends the list, for example "IN, parameters: 200" or "OPENJSON, parameters: 1".
        /// </summary>
        private static string SummarizeListTranslation(string sql)
        {
            string operation = sql.Contains("OPENJSON", StringComparison.Ordinal) ? "OPENJSON" : "IN";
            int parameterCount = Regex.Matches(sql, "^DECLARE @", RegexOptions.Multiline).Count;
            return $"{operation}, parameters: {parameterCount}";
        }
    }
}
