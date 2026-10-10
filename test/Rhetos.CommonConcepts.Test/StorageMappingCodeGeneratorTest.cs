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
using Rhetos.Compiler;
using Rhetos.Dom.DefaultConcepts;
using Rhetos.Dsl.DefaultConcepts;
using Rhetos.MsSql.SqlResources;
using Rhetos.MsSqlEf6.SqlResources;
using Rhetos.PostgreSql.SqlResources;
using Rhetos.SqlResources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Rhetos.CommonConcepts.Test
{
    [TestClass]
    public class StorageMappingCodeGeneratorTest
    {
        [TestMethod]
        public void MsSqlSizesStringAndBinaryParameters()
        {
            Assert.AreEqual(
                ExpectedMsSqlMapping("Microsoft.Data.SqlClient.SqlParameter"),
                GenerateStorageMapping(new MsSqlResourcesPlugin()));
        }

        [TestMethod]
        public void MsSqlEf6SizesStringAndBinaryParameters()
        {
            Assert.AreEqual(
                ExpectedMsSqlMapping("System.Data.SqlClient.SqlParameter"),
                GenerateStorageMapping(new MsSqlEf6SqlResourcesPlugin()));
        }

        [TestMethod]
        public void PostgreSqlDoesNotSizeParameters()
        {
            Assert.AreEqual(
@"new PersistenceStorageObjectParameter(""Name"", new Npgsql.NpgsqlParameter("""", System.Data.SqlDbType.NVarChar) { Value = ((object)entity.Name) ?? DBNull.Value }),
new PersistenceStorageObjectParameter(""Text"", new Npgsql.NpgsqlParameter("""", System.Data.SqlDbType.NVarChar) { Value = ((object)entity.Text) ?? DBNull.Value }),
new PersistenceStorageObjectParameter(""Blob"", new Npgsql.NpgsqlParameter("""", System.Data.SqlDbType.VarBinary) { Value = ((object)entity.Blob) ?? DBNull.Value }),
new PersistenceStorageObjectParameter(""Number"", new Npgsql.NpgsqlParameter("""", System.Data.SqlDbType.Int) { Value = ((object)entity.Number) ?? DBNull.Value }),",
                GenerateStorageMapping(new PostgreSqlResourcesPlugin()));
        }

        private static string ExpectedMsSqlMapping(string dbParameterClass) =>
$@"new PersistenceStorageObjectParameter(""Name"", new {dbParameterClass}("""", System.Data.SqlDbType.NVarChar) {{ Value = ((object)entity.Name) ?? DBNull.Value, Size = Rhetos.Dom.DefaultConcepts.DbParameterSize.ComputeSize(entity.Name?.Length, 256) }}),
new PersistenceStorageObjectParameter(""Text"", new {dbParameterClass}("""", System.Data.SqlDbType.NVarChar) {{ Value = ((object)entity.Text) ?? DBNull.Value, Size = Rhetos.Dom.DefaultConcepts.DbParameterSize.ComputeSize(entity.Text?.Length, -1) }}),
new PersistenceStorageObjectParameter(""Blob"", new {dbParameterClass}("""", System.Data.SqlDbType.VarBinary) {{ Value = ((object)entity.Blob) ?? DBNull.Value, Size = Rhetos.Dom.DefaultConcepts.DbParameterSize.ComputeSize(entity.Blob?.Length, -1) }}),
new PersistenceStorageObjectParameter(""Number"", new {dbParameterClass}("""", System.Data.SqlDbType.Int) {{ Value = ((object)entity.Number) ?? DBNull.Value }}),";

        /// <summary>
        /// Runs the property code generators for an entity with ShortString, LongString, Binary and Integer properties,
        /// and returns the generated storage mapping lines.
        /// </summary>
        private static string GenerateStorageMapping(ISqlResourcesPlugin sqlResourcesPlugin)
        {
            var sqlResources = new SqlResourcesMock { Resources = new Dictionary<string, string>(sqlResourcesPlugin.GetResources()) };
            var entity = new EntityInfo { Module = new ModuleInfo { Name = "TestModule" }, Name = "TestEntity" };
            var propertyGenerators = new (PropertyInfo Property, IConceptCodeGenerator Generator)[]
            {
                (new ShortStringPropertyInfo { DataStructure = entity, Name = "Name" }, new ShortStringPropertyCodeGenerator(sqlResources)),
                (new LongStringPropertyInfo { DataStructure = entity, Name = "Text" }, new LongStringPropertyCodeGenerator(sqlResources)),
                (new BinaryPropertyInfo { DataStructure = entity, Name = "Blob" }, new BinaryCodeGenerator(sqlResources)),
                (new IntegerPropertyInfo { DataStructure = entity, Name = "Number" }, new IntegerPropertyCodeGenerator(sqlResources)),
            };

            var entityTags = new[]
            {
                DataStructureCodeGenerator.BodyTag,
                RepositoryHelper.AssignSimplePropertyTag,
                WritableOrmDataStructureCodeGenerator.OldDataLoadedTag,
                WritableOrmDataStructureCodeGenerator.PersistenceStorageMapperPropertyMappingTag,
            };

            var codeBuilder = new CodeBuilder("/*", "*/");
            codeBuilder.InsertCode(string.Join(Environment.NewLine, entityTags.Select(tag => tag.Evaluate(entity))));
            foreach (var (property, generator) in propertyGenerators)
                generator.GenerateCode(property, codeBuilder);

            return string.Join(Environment.NewLine, codeBuilder.GenerateCode()
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith($"new {nameof(PersistenceStorageObjectParameter)}(", StringComparison.Ordinal)));
        }
    }
}
