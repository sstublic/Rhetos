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

namespace Rhetos.Dom.DefaultConcepts
{
    /// <summary>
    /// Run-time configuration.
    /// </summary>
    [Options("CommonConcepts")]
    public class CommonConceptsRuntimeOptions
    {
        /// <summary>
        /// Number of records inserted updated or deleted in a single SQL command.
        /// When saving a list of records with count larger than this limit, the list will be automatically split to batches.
        /// </summary>
        public int SaveSqlCommandBatchSize { get; set; } = 20;

        /// <summary>
        /// Enable for compatibility with Rhetos.CommonConcepts v5.4.0 and earlier versions, but the insert and delete operations
        /// with a larger number of records might be more then 10x slower.
        /// It separates each insert or delete operation into a separate SQL statement withing a single batch script, instead of creating a single
        /// insert or delete command that impacts multiple records.
        /// In both cases, the number of records in a batch is limited by <see cref="SaveSqlCommandBatchSize"/>.
        /// This option may influence the insert and delete triggers on the table: when true, the triggers will always receive a single record.
        /// </summary>
        public bool SqlCommandBatchSeparateQueries { get; set; } = false;

        /// <summary>
        /// If set to false, the application will throw exception if the decimal scale of the value being written is more than 2.
        /// For backward compatibility, setting this to true will automatically round the value before writing to the database.
        /// </summary>
        public bool AutoRoundMoney { get; set; } = false;

        /// <summary>
        /// If true, read command checks that valid property filter should contain a simple property correctly formated.
        /// </summary>
        public bool ReadCommandSimpleProperty { get; set; } = true;

        /// <summary>
        /// Optimizes in-memory queries (LINQ queries over a materialized list, usually created by a filter
        /// implementation that returns <c>items.AsQueryable()</c>), by executing them with the expression
        /// interpreter instead of compiling the expression tree to IL code on each query execution.
        /// <para>
        /// The optimization is applied only on small data sets (see <see cref="OptimizeInMemoryQueryableThreshold"/>),
        /// where the query compilation takes more time than the query execution.
        /// On larger data sets, and on any query expression that cannot be interpreted,
        /// the standard .NET behavior is used.
        /// </para>
        /// <para>
        /// This option is disabled by default. Enable it if the application often reads small data sets
        /// with filters that are implemented on an in-memory list.
        /// </para>
        /// </summary>
        public bool OptimizeInMemoryQueryable { get; set; } = false;

        /// <summary>
        /// Maximum number of records in the source collection for the <see cref="OptimizeInMemoryQueryable"/>
        /// optimization to be applied. If the source has this many records or more, the standard .NET behavior is used.
        /// </summary>
        /// <remarks>
        /// The default value is derived from micro-benchmark measurements (BenchmarkDotNet, .NET 10) comparing the interpreted
        /// and the compiled execution of representative composed queries (stacked Where, OrderBy/ThenBy, Skip/Take,
        /// Select projection) over an in-memory list: the measured break-even is between 6000 records (scalar Count
        /// queries) and 9500 records (materializing queries). The default value leaves a safety margin below the
        /// break-even, and it also limits the additional memory allocations of the expression interpreter
        /// (about 0.7 kB per interpreted record).
        /// </remarks>
        public int OptimizeInMemoryQueryableThreshold { get; set; } = 1000;
    }
}
