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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhetos.Dsl.DefaultConcepts;
using System.Globalization;
using System.ComponentModel.Composition;
using Rhetos.Compiler;
using Rhetos.Extensibility;
using Rhetos.Dsl;

namespace Rhetos.Dom.DefaultConcepts
{
    [Export(typeof(IConceptCodeGenerator))]
    [ExportMetadata(MefProvider.Implements, typeof(DataStructureInfo))]
    [ExportMetadata(MefProvider.DependsOn, typeof(OrmDataStructureCodeGenerator))]
    public class FilterIdRepositoryCodeGenerator : IConceptCodeGenerator
    {
        /// <summary>
        /// Body of the Common.FilterId repository method CreateQueryableFilterIds, after the IDs are materialized.
        /// The ORM provider must insert the implementation at this tag, including the return statement.
        /// </summary>
        public static readonly string CreateQueryableFilterIdsTag = "/*CreateQueryableFilterIds*/";

        /// <summary>
        /// Returns true for the Common.FilterId data structure, the only one whose repository contains <see cref="CreateQueryableFilterIdsTag"/>.
        /// </summary>
        public static bool IsFilterIdDataStructure(DataStructureInfo info) => info.Module.Name == "Common" && info.Name == "FilterId";

        public void GenerateCode(IConceptInfo conceptInfo, ICodeBuilder codeBuilder)
        {
            var info = (DataStructureInfo)conceptInfo;

            if (IsFilterIdDataStructure(info))
                codeBuilder.InsertCode(CreateQueryableFilterIdsSnippet, RepositoryHelper.RepositoryMembers, info);
        }

        private static string CreateQueryableFilterIdsSnippet =>
        $@"/// <summary>
        /// Returns the given IDs in a form that the ORM provider can efficiently use in a LINQ query filter, such as <c>ids.Contains(item.ID)</c>.
        /// Depending on the ORM provider and the number of IDs, the result is a materialized list of the IDs, or a query that selects them.
        /// </summary>
        public IEnumerable<Guid> CreateQueryableFilterIds(IEnumerable<Guid> ids)
        {{
            Rhetos.Utilities.CsUtility.Materialize(ref ids);

            {CreateQueryableFilterIdsTag}
        }}

        ";
    }
}
