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
    /// Size of a database command parameter for a variable-length string or binary value.
    /// </summary>
    /// <remarks>
    /// Without an explicit size, the database client declares each parameter with the value length,
    /// so commands that differ only in value lengths get different texts and separate cached execution plans.
    /// The size is never smaller than the value length, because the client silently truncates a longer value,
    /// while a value too long for the column must be rejected by the database.
    /// </remarks>
    public static class DbParameterSize
    {
        /// <summary>
        /// Maximum length of an unbounded column, such as NVARCHAR(MAX) or VARBINARY(MAX),
        /// and the parameter size of a value sent as MAX.
        /// </summary>
        public const int Unbounded = -1;

        /// <summary>
        /// Returns <paramref name="maxLength"/> if the column is bounded and the value fits (a null value fits),
        /// otherwise <see cref="Unbounded"/>.
        /// </summary>
        /// <param name="valueLength">String length in characters or binary length in bytes, null for a null value.</param>
        /// <param name="maxLength">Positive column length, or <see cref="Unbounded"/>.</param>
        public static int ComputeSize(int? valueLength, int maxLength)
            => maxLength > 0 && (valueLength is null || valueLength <= maxLength) ? maxLength : Unbounded;
    }
}
