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
using System.Reflection;
using MySqlConnector;
using PHP.Core;

namespace PHP.Library.Data
{
	internal sealed class MySqlConnectionManager : ConnectionManager
	{
        protected override PhpDbConnection CreateConnection(string/*!*/ connectionString)
        {
            return new PhpMyDbConnection(connectionString, ScriptContext.CurrentContext);
        }
    }

	/// <summary>
	/// Summary description for PhpMyDbConnection.
	/// </summary>
	public sealed class PhpMyDbConnection : PhpDbConnection
	{
        private readonly ScriptContext/*!*/ _context;
        private bool _sharedConnection;

		/// <summary>
		/// Server.
		/// </summary>
		public string/*!*/ Server { get { return server; } }
		private string/*!*/ server;
		internal void SetServer(string/*!*/ value) { server = value; }

		/// <summary>
		/// Creates a connection resource.
		/// </summary>
        /// <param name="connectionString">Connection string.</param>
        /// <param name="context">Script context associated with the connection.</param>
        public PhpMyDbConnection(string/*!*/ connectionString, ScriptContext/*!*/ context)
		: base(connectionString, new MySqlConnection(), "mysql connection")
		{
            if (context == null)
                throw new ArgumentNullException("context");
            _context = context;
            _sharedConnection = false;
		}

	    /// <summary>
	    /// Gets the underlying MySql connection from the connection. We specifically support the case where
	    /// the connection is a wrapped connection such as we get from Glimpse, and we look for InnerConnection to
	    /// find the native MySqlConnection when we need it.
	    /// </summary>
	    internal MySqlConnection MySqlConnection
	    {
	        get
	        {
		        if (_mySqlConnection != null) return _mySqlConnection;
		        _mySqlConnection = connection as MySqlConnection;
		        if (_mySqlConnection != null) return _mySqlConnection;
		        if (_innerConnectionMethod == null)
			        _innerConnectionMethod = connection.GetType().GetMethod("get_InnerConnection", BindingFlags.Instance | BindingFlags.Public);
		        _mySqlConnection = _innerConnectionMethod?.Invoke(connection, null) as MySqlConnection;
		        return _mySqlConnection;
	        }
	    }
	    private MySqlConnection _mySqlConnection;
	    private static MethodInfo _innerConnectionMethod;

	    /// <summary>
        /// Override the connection to use a shared connection
        /// </summary>
        /// <param name="sharedConnection">Shared MySQL connection</param>
        internal void SetSharedConnection(
            IDbConnection sharedConnection)
        {
            // Close the unused connection created in the constructor
            connection.Close();

            // Indicate this connection is now shared
            _sharedConnection = true;

            // Save the shared connection
            connection = sharedConnection;
        }

        /// <summary>
        /// Closes connection and releases the resource.
        /// </summary>
        protected override void FreeManaged()
        {
            // Get rid of the shared connection but don't close it! It will be closed later
            if (_sharedConnection) {
                connection = null;
                _sharedConnection = false;
            }
            base.FreeManaged();
        }

		internal static PhpMyDbConnection ValidConnection(PhpResource handle)
		{
			PhpMyDbConnection connection;

            if (handle != null && handle.GetType() == typeof(PhpMyDbConnection))
                connection = (PhpMyDbConnection)handle;
            else
                connection = null;

            if (connection != null && connection.IsValid)
                return connection;

			PhpException.Throw(PhpError.Warning, LibResources.GetString("invalid_connection_resource"));
			return null;
		}

        /// <summary>
		/// Gets a query result resource.
		/// </summary>
		/// <param name="connection">Database connection.</param>
		/// <param name="reader">Data reader to be used for result resource population.</param>
		/// <param name="convertTypes">Whether to convert data types to PHP ones.</param>
		/// <returns>Result resource holding all resulting data of the query.</returns>
		protected override PhpDbResult GetResult(PhpDbConnection/*!*/ connection, IDataReader/*!*/ reader, bool convertTypes)
		{
			return new PhpMyDbResult(connection, reader, convertTypes);
		}

	    /// <summary>
	    /// Command factory.
	    /// </summary>
	    /// <returns>An empty instance of <see cref="MySqlCommand"/>.</returns>
	    protected override IDbCommand /*!*/ CreateCommand()
	    {
	        IDbCommand command = connection.CreateCommand();
	        MySqlLocalConfig local = MySqlConfiguration.GetLocal(_context);
	        if (local.DefaultCommandTimeout >= 0) {
	            command.CommandTimeout = local.DefaultCommandTimeout;
	        }
	        return command;
	    }

	    /// <summary>
		/// Gets last error number.
		/// </summary>
		/// <returns>The error number it is known, -1 if unknown error occured, or zero on success.</returns>
		public override int GetLastErrorNumber()
		{
		  if (LastException==null) return 0;

		  MySqlException e = LastException as MySqlException;
		  return (e!=null) ? e.Number : -1;
		}

        /// <summary>
		/// Gets the last error message.
		/// </summary>
		/// <returns>The message or an empty string if no error occured.</returns>
		public override string GetLastErrorMessage()
        {
          if (LastException != null && !(LastException is MySqlException)) {
            return LastException.Message + "\n\n" + LastException.StackTrace;
          }
          return StripErrorNumber(base.GetLastErrorMessage());
        }

		/// <summary>
		/// Gets a message from an exception raised by the connector.
		/// Removes the initial #{number} and the ending dot.
		/// </summary>
		/// <param name="e">Exception.</param>
		/// <returns>The message.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="e"/> is a <B>null</B> reference.</exception>
		public override string GetExceptionMessage(Exception/*!*/ e)
		{
		  if (e == null) throw new ArgumentNullException("e");

		  MySqlException mye = e as MySqlException;
		  if (mye == null || mye.Message.Length == 0) return e.Message;

		  string msg = StripErrorNumber(mye.Message);

		  // skip last dot:
		  int j = msg.Length;
		  if (msg[j-1] == '.') j--;

		  return String.Format("{0} (error {1})", msg.Substring(0, j), mye.Number);
		}

		private string StripErrorNumber(string msg)
		{
		  // find first non-digit:
		  if (msg.Length > 0 && msg[0] == '#')
		  {
		    int i = 1;
		    while (i < msg.Length && msg[i] >= '0' && msg[i] <= '9') i++;
		    return msg.Substring(i);
		  }
		  else
		  {
		    return msg;
		  }
		}

		/// <summary>
		/// Queries server for a value of a global variable.
		/// </summary>
		/// <param name="name">Global variable name.</param>
		/// <returns>Global variable value (converted).</returns>
		internal object QueryGlobalVariable(string name)
		{
      // TODO: better query:

      PhpDbResult result = ExecuteQuery("SHOW GLOBAL VARIABLES LIKE '" + name + "'",true);

      // default value
      if (result.FieldCount != 2 || result.RowCount != 1)
        return null;

      return result.GetFieldValue(0,1);
    }
	}
}
