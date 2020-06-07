/*

 Copyright (c) 2005-2006 Tomas Matousek.  

 This software is distributed under GNU General Public License version 2.
 The use and distribution terms for this software are contained in the file named LICENSE, 
 which can be found in the same directory as this file. By using this software 
 in any fashion, you are agreeing to be bound by the terms of this license.
 
 You must not remove this notice from this software.

*/

using System;
using System.Data;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using MySql.Data.MySqlClient;
using MySql.Data.Types;

using PHP.Core;
using System.Data.Common;

namespace PHP.Library.Data
{
	/// <summary>
	/// Represents a result of a MySQL command.
	/// </summary>
    public sealed class PhpMyDbResult : PhpDbResult
    {
        public new MySqlCommand Command => (MySqlCommand)base.Command;

        public new MySqlDataReader Reader => (MySqlDataReader)base.Reader;

        //public new PhpMyDbConnection Connection => (PhpMyDbConnection)base.Connection;

        /// <summary>
        /// Creates an instance of a result resource.
        /// </summary>
        /// <param name="connection">Database connection.</param>
        /// <param name="reader">Data reader from which to load results.</param>
        /// <param name="convertTypes">Whether to convert resulting values to PHP types.</param>
        /// <exception cref="ArgumentNullException">Argument is a <B>null</B> reference.</exception>
        public PhpMyDbResult(PhpDbConnection/*!*/ connection, IDataReader/*!*/ reader, bool convertTypes)
            : base(connection, reader, "MySQL result", convertTypes)
        {
            // no code in here
        }

        internal static PhpMyDbResult ValidResult(PhpResource handle)
        {
            PhpMyDbResult result = handle as PhpMyDbResult;
            if (result != null && result.IsValid) return result;

            PhpException.Throw(PhpError.Warning, LibResources.GetString("invalid_result_resource"));
            return null;
        }

        /// <summary>
        /// Gets row values.
        /// </summary>
        /// <param name="dataTypes">Data type names.</param>
        /// <param name="convertTypes">Whether to convert value to PHP types.</param>
        /// <returns>Row data.</returns>
        protected override object[] GetValues(string[] dataTypes, bool convertTypes)
        {
            var my_reader = Reader;
            var oa = new object[my_reader.FieldCount];
            
            if (convertTypes)
            {
                Debug.Assert(dataTypes.Length >= oa.Length);
                for (int i = 0; i < oa.Length; i++)
                {
                    oa[i] = ConvertDbValue(dataTypes[i], my_reader.GetValue(i));
                }
            }
            else
            {
                for (int i = 0; i < oa.Length; i++)
                {
                    oa[i] = my_reader.GetValue(i);
                }
            }

            return oa;
        }

        /// <summary>
        /// The elements are of type <see cref="MySqlDbColumn"/>.
        /// </summary>
        public IReadOnlyList<DbColumn> ColumnSchema => (IReadOnlyList<DbColumn>)GetRowCustomData();

        public MySqlDbColumn GetColumnSchema(int fieldIndex) => CheckFieldIndex(fieldIndex) ? (MySqlDbColumn)ColumnSchema[fieldIndex] : null;

        /// <summary>
        /// Collect additional information about current row of Reader.
        /// </summary>
        protected override object GetCustomData()
        {
            return Reader.FieldCount != 0 ? (IReadOnlyList<DbColumn>)Reader.GetColumnSchema() : Array.Empty<DbColumn>();
        }

        public override int GetFieldLength(int fieldIndex)
        {
            if (CheckFieldIndex(fieldIndex))
            {
                var size = ColumnSchema[fieldIndex].ColumnSize;
                if (size.HasValue)
                {
                    return size.Value;
                }
            }

            return -1;
        }

        /// <summary>
        /// Converts a value of a specified MySQL DB type to PHP value.
        /// </summary>
        /// <param name="dataType">MySQL DB data type.</param>
        /// <param name="sqlValue">The value.</param>
        /// <returns>PHP value.</returns>
        private static object ConvertDbValue(string dataType, object sqlValue)
        {
            if (sqlValue == null || sqlValue.GetType() == typeof(string))
                return sqlValue;

            if (sqlValue.GetType() == typeof(double))
                return Core.Convert.DoubleToString((double)sqlValue);

            if (sqlValue == System.DBNull.Value)
                return null;

            if (sqlValue.GetType() == typeof(int))
                return ((int)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(uint))
                return ((uint)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(bool))
                return (bool)sqlValue ? "1" : "0";

            if (sqlValue.GetType() == typeof(byte))
                return ((byte)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(sbyte))
                return ((sbyte)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(short))
                return ((short)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(ushort))
                return ((ushort)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(float))
                return Core.Convert.DoubleToString((float)sqlValue);

            if (sqlValue.GetType() == typeof(DateTime))
                return ConvertDateTime(dataType, (DateTime)sqlValue);

            if (sqlValue.GetType() == typeof(long))
                return ((long)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(ulong))
                return ((ulong)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(TimeSpan))
                return ((TimeSpan)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(decimal))
                return ((decimal)sqlValue).ToString();

            if (sqlValue.GetType() == typeof(byte[]))
                return new PhpBytes((byte[])sqlValue);

            //MySqlDateTime sql_date_time = sqlValue as MySqlDateTime;
            if (sqlValue.GetType() == typeof(MySqlDateTime))
            {
                MySqlDateTime sql_date_time = (MySqlDateTime)sqlValue;
                if (sql_date_time.IsValidDateTime)
                    return ConvertDateTime(dataType, sql_date_time.GetDateTime());

                if (dataType == "DATE" || dataType == "NEWDATE")
                    return "0000-00-00";
                else
                    return "0000-00-00 00:00:00";
            }

            Debug.Fail("Unexpected DB field type " + sqlValue.GetType() + ".");
            return sqlValue.ToString();
        }

        private static string ConvertDateTime(string dataType, DateTime value)
        {
            if (dataType == "DATE" || dataType == "NEWDATE")
                return value.ToString("yyyy-MM-dd");
            else
                return value.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// Maps MySQL .NET Connector's type name to the one displayed by PHP.
        /// </summary>
        /// <param name="typeName">MySQL .NET Connector's name.</param>
        /// <returns>PHP name.</returns>
        protected override string MapFieldTypeName(string typeName)
        {
            switch (typeName)
            {
                case "VARCHAR":
                    return "string";

                case "INT":
                case "BIGINT":
                case "MEDIUMINT":
                case "SMALLINT":
                case "TINYINT":
                    return "int";

                case "FLOAT":
                case "DOUBLE":
                case "DECIMAL":
                    return "real";

                case "YEAR":
                    return "year";

                case "DATE":
                case "NEWDATE":
                    return "date";

                case "TIMESTAMP":
                    return "timestamp";

                case "DATETIME":
                    return "datetime";

                case "TIME":
                    return "time";

                case "SET":
                    return "set";

                case "ENUM":
                    return "enum";

                case "TINY_BLOB":
                case "MEDIUM_BLOB":
                case "LONG_BLOB":
                case "BLOB":
                    return "blob";

                // not in PHP:
                case "BIT":
                    return "bit";

                case null:
                case "NULL":
                    return "NULL";

                default:
                    return "unknown";
            }
        }

        /// <summary>
        /// Determines whether a type of a specified PHP name is a numeric type.
        /// </summary>
        /// <param name="phpName">PHP type name.</param>
        /// <returns>Whether the type is numeric ("int", "real", or "year").</returns>
        public bool IsNumericType(string phpName)
        {
            switch (phpName)
            {
                case "int":
                case "real":
                case "year":
                case "timestamp":
                    return true;

                default:
                    return false;
            }
        }
    }
}
