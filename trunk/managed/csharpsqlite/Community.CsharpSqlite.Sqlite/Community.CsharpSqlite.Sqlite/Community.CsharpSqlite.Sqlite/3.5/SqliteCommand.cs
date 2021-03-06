//
// Community.CsharpSqlite.Sqlite.SqliteCommand.cs
//
// Represents a Transact-SQL statement or stored procedure to execute against 
// a Sqlite database file.
//
// Author(s): 	Vladimir Vukicevic  <vladimir@pobox.com>
//		Everaldo Canuto  <everaldo_canuto@yahoo.com.br>
//		Chris Turchin <chris@turchin.net>
//		Jeroen Zwartepoorte <jeroen@xs4all.nl>
//		Thomas Zoechling <thomas.zoechling@gmx.at>
//		Joshua Tauberer <tauberer@for.net>
//
// Copyright (C) 2002  Vladimir Vukicevic
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
#if !NET_2_0
using System;
using System.Collections;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Data;
using System.Data.Common;
using System.Diagnostics; 
using Group = System.Text.RegularExpressions.Group;
using Community.CsharpSqlite.Sqlite;

namespace Community.CsharpSqlite.Sqlite 
{
	public class SqliteCommand : DbCommand, IDbCommand, ICloneable
	{
		#region Fields
		
		private SqliteConnection parent_conn;
		//private SqliteTransaction transaction;

		private IDbTransaction transaction;

		private string sql;
		private int timeout;
		private CommandType type;
		private UpdateRowSource upd_row_source;
		private SqliteParameterCollection sql_params;
		private bool prepared = false;
		private bool _designTimeVisible = true;
		
		#endregion

		#region Constructors and destructors
		
		public SqliteCommand ()
		{
			sql = "";
		}
				
		public SqliteCommand (string sqlText)
		{
			sql = sqlText;
		}
		
		public SqliteCommand (string sqlText, SqliteConnection dbConn)
		{
			sql = sqlText;
			parent_conn = dbConn;
		}
		
		public SqliteCommand (string sqlText, SqliteConnection dbConn, IDbTransaction trans)
		{
			sql = sqlText;
			parent_conn = dbConn;
			transaction = trans;
		}
		
		public void Dispose ()
		{
		}
		
		#endregion

		#region Properties

		public override string CommandText 
		{
			get { return sql; }
			set { sql = value; prepared = false; }
		}

        public override int CommandTimeout
		{
			get { return timeout; }
			set { timeout = value; }
		}

        public override CommandType CommandType 
		{
			get { return type; }
			set { type = value; }
		}		

		public SqliteConnection Connection
		{
			get { return parent_conn; }
			set { parent_conn = (SqliteConnection)value; }
		}

	    protected override DbConnection DbConnection
	    {
            get { return parent_conn; }
            set { parent_conn = (SqliteConnection)value; }
	    }

	    protected override DbParameterCollection DbParameterCollection
	    {
            get { return (DbParameterCollection)Parameters; }
	    }

	    protected override DbTransaction DbTransaction
	    {
            get
            {

				return (DbTransaction)transaction;

            }
            set { transaction = value; }
	    }

	    public override bool DesignTimeVisible
	    {
            get { return _designTimeVisible; }
            set { _designTimeVisible = value; }
	    }

	    IDbConnection IDbCommand.Connection 
		{
			get 
			{ 
				return parent_conn; 
			}
			set 
			{
				if (!(value is SqliteConnection)) 
				{
					throw new InvalidOperationException ("Can't set Connection to something other than a SqliteConnection");
				}
				parent_conn = (SqliteConnection) value;
			}
		}

		public SqliteParameterCollection Parameters {
			get {
				if (sql_params == null)
					sql_params = new SqliteParameterCollection();
				return sql_params;
			}
		}
		
		IDataParameterCollection IDbCommand.Parameters  {
			get { return Parameters; }
		}		

		public IDbTransaction Transaction {
			get { return transaction; }
			set { transaction = value; }
		}		

		public override UpdateRowSource UpdatedRowSource 
		{
			get { return upd_row_source; }
			set { upd_row_source = value; }
		}               
		#endregion

		#region Internal Methods
		
		internal int NumChanges () 
		{
			if (parent_conn.Version == 3)
				return Sqlite3.sqlite3_changes(parent_conn.Handle2);
			else
                return Sqlite3.sqlite3_changes(parent_conn.Handle2);
		}

        private void BindParameters3(Sqlite3.Vdbe pStmt)
		{
            if (sql_params == null) return;
            if (sql_params.Count == 0) return;

            int pcount = Sqlite3.sqlite3_bind_parameter_count(pStmt);

            for (int i = 1; i <= pcount; i++)
            {
                String name = Sqlite3.sqlite3_bind_parameter_name(pStmt, i);

                SqliteParameter param = null;
                if (name != null)
                    param = sql_params[name] as SqliteParameter;
                else
                    param = sql_params[i - 1] as SqliteParameter;

                if (param.Value == null)
                {
                    Sqlite3.sqlite3_bind_null(pStmt, i);
                    continue;
                }

                Type ptype = param.Value.GetType();
                if (ptype.IsEnum)
                    ptype = Enum.GetUnderlyingType(ptype);

                SqliteError err;

                if (ptype.Equals(typeof(String)))
                {
                    String s = (String)param.Value;
                    err = (SqliteError)Sqlite3.sqlite3_bind_text(pStmt, i, s, -1, null);
                }
                else if (ptype.Equals(typeof(DBNull)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_null(pStmt, i);
                }
                else if (ptype.Equals(typeof(Boolean)))
                {
                    bool b = (bool)param.Value;
                    err = (SqliteError)Sqlite3.sqlite3_bind_int(pStmt, i, b ? 1 : 0);
                }
                else if (ptype.Equals(typeof(Byte)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_int(pStmt, i, (Byte)param.Value);
                }
                else if (ptype.Equals(typeof(Char)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_int(pStmt, i, (Char)param.Value);
                }
                else if (ptype.IsEnum)
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_int(pStmt, i, (Int32)param.Value);
                }
                else if (ptype.Equals(typeof(Int16)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_int(pStmt, i, (Int16)param.Value);
                }
                else if (ptype.Equals(typeof(Int32)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_int(pStmt, i, (Int32)param.Value);
                }
                else if (ptype.Equals(typeof(SByte)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_int(pStmt, i, (SByte)param.Value);
                }
                else if (ptype.Equals(typeof(UInt16)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_int(pStmt, i, (UInt16)param.Value);
                }
                else if (ptype.Equals(typeof(DateTime)))
                {
                    DateTime dt = (DateTime)param.Value;
                    err = (SqliteError)Sqlite3.sqlite3_bind_text(pStmt, i, dt.ToString(), -1, null);
                }
                else if (ptype.Equals(typeof(Double)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_double(pStmt, i, (Double)param.Value);
                }
                else if (ptype.Equals(typeof(Single)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_double(pStmt, i, (Single)param.Value);
                }
                else if (ptype.Equals(typeof(UInt32)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_int64(pStmt, i, (UInt32)param.Value);
                }
                else if (ptype.Equals(typeof(Int64)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_int64(pStmt, i, (Int64)param.Value);
                }
                else if (ptype.Equals(typeof(Byte[])))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_blob(pStmt, i, (byte[])param.Value, ((byte[])param.Value).Length, null);
                }
                else if (ptype.Equals(typeof(Guid)))
                {
                    err = (SqliteError)Sqlite3.sqlite3_bind_text(pStmt, i, param.Value.ToString(), param.Value.ToString().Length, null);
                }
                else
                {
                    throw new ApplicationException("Unkown Parameter Type");
                }
                if (err != SqliteError.OK)
                {
                    throw new ApplicationException("Sqlite error in bind " + err);
                }
            }
		}

        private void GetNextStatement(string pzStart, ref string pzTail, ref Sqlite3.Vdbe pStmt)
        {
            UTF8Encoding encoding = new UTF8Encoding();
            SqliteError err = (SqliteError)Sqlite3.sqlite3_prepare(parent_conn.Handle2, pzStart, pzStart.Length, ref pStmt, ref pzTail);
            if (err != SqliteError.OK)
                throw new SqliteSyntaxException(GetError3());
        }

        // Executes a statement and ignores its result.
        private void ExecuteStatement(Sqlite3.Vdbe pStmt)
        {
            int cols;
            IntPtr pazValue, pazColName;
            ExecuteStatement(pStmt, out cols, out pazValue, out pazColName);
        }

		// Executes a statement and returns whether there is more data available.
        // Executes a statement and returns whether there is more data available.
        internal bool ExecuteStatement(Sqlite3.Vdbe pStmt, out int cols, out IntPtr pazValue, out IntPtr pazColName)
        {
            SqliteError err;

            //if (parent_conn.Version == 3) 
            //{
            err = (SqliteError)Sqlite3.sqlite3_step(pStmt);

            if (err == SqliteError.ERROR)
                throw new SqliteExecutionException(GetError3());

            pazValue = IntPtr.Zero; pazColName = IntPtr.Zero; // not used for v=3
            cols = Sqlite3.sqlite3_column_count(pStmt);

            /*
            }
            else 
            {
                err = (SqliteError)Sqlite3.sqlite3_step(pStmt, out cols, out pazValue, out pazColName);
                if (err == SqliteError.ERROR)
                    throw new SqliteExecutionException ();
            }
            */
            if (err == SqliteError.BUSY)
                throw new SqliteBusyException();

            if (err == SqliteError.MISUSE)
                throw new SqliteExecutionException();

            // err is either ROW or DONE.
            return err == SqliteError.ROW;
        }
		
		#endregion

		#region Public Methods
		
		object ICloneable.Clone ()
		{
			return new SqliteCommand (sql, parent_conn, transaction);
		}

		public override void Cancel ()
		{
		}
		
		public string BindParameters2()
		{
			string text = sql;
			
			// There used to be a crazy regular expression here, but it caused Mono
			// to go into an infinite loop of some sort when there were no parameters
			// in the SQL string.  That was too complicated anyway.
			
			// Here we search for substrings of the form [:?]wwwww where w is a letter or digit
			// (not sure what a legitimate Sqlite3 identifier is), except those within quotes.
			
			char inquote = (char)0;
			int counter = 0;
			for (int i = 0; i < text.Length; i++) {
				char c = text[i];
				if (c == inquote) {
					inquote = (char)0;
				} else if (inquote == (char)0 && (c == '\'' || c == '"')) {
					inquote = c;
				} else if (inquote == (char)0 && (c == ':' || c == '?')) {
					int start = i;
					while (++i < text.Length && char.IsLetterOrDigit(text[i])) { } // scan to end
					string name = text.Substring(start, i-start);
					SqliteParameter p;
					if (name.Length > 1)
						p = Parameters[name] as SqliteParameter;
					else
						p = Parameters[counter] as SqliteParameter;
					string value = "'" + Convert.ToString(p.Value).Replace("'", "''") + "'";
					text = text.Remove(start, name.Length).Insert(start, value);
					i += value.Length - name.Length - 1;
					counter++;
				}
			}
			
			return text;
		}		

		public override void Prepare ()
		{
			// There isn't much we can do here.  If a table schema
			// changes after preparing a statement, Sqlite bails,
			// so we can only compile statements right before we
			// want to run them.		
	
			if (prepared) return;
		
			if (Parameters.Count > 0 && parent_conn.Version == 2)
			{
				sql = BindParameters2();
			}
			prepared = true;
		}
		
		IDbDataParameter IDbCommand.CreateParameter()
		{
			return CreateParameter ();
		}

	    protected override DbParameter CreateDbParameter()
	    {
            return new SqliteParameter();
	    }

	    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
	    {
            return (DbDataReader)ExecuteReader(behavior);
	    }

	    public SqliteParameter CreateParameter ()
		{
			return new SqliteParameter ();
		}		

		public override int ExecuteNonQuery ()
		{
			int rows_affected;
			ExecuteReader (CommandBehavior.Default, false, out rows_affected);
			return rows_affected;
		}		

		public override object ExecuteScalar ()
		{
			SqliteDataReader r = (SqliteDataReader)ExecuteReader ();
			if (r == null || !r.Read ()) {
				return null;
			}
			object o = r[0];
			r.Close ();
			return o;
		}
		
		IDataReader IDbCommand.ExecuteReader ()
		{
			return ExecuteReader ();
		}
		
		IDataReader IDbCommand.ExecuteReader (CommandBehavior behavior)
		{
			return ExecuteReader (behavior);
		}
		
		public SqliteDataReader ExecuteReader ()
		{
			return ExecuteReader (CommandBehavior.Default);
		}
		
		public new SqliteDataReader ExecuteReader (CommandBehavior behavior)
		{
			int r;
			return ExecuteReader (behavior, true, out r);
		}		

		public SqliteDataReader ExecuteReader (CommandBehavior behavior, bool want_results, out int rows_affected)
		{
			Prepare ();
			
			// The SQL string may contain multiple sql commands, so the main
			// thing to do is have Sqlite iterate through the commands.
			// If want_results, only the last command is returned as a
			// DataReader.  Otherwise, no command is returned as a
			// DataReader.
		
			IntPtr psql; // pointer to SQL command
			
			// Sqlite 2 docs say this: By default, SQLite assumes that all data uses a fixed-size 8-bit 
			// character (iso8859).  But if you give the --enable-utf8 option to the configure script, then the 
			// library assumes UTF-8 variable sized characters. This makes a difference for the LIKE and GLOB 
			// operators and the LENGTH() and SUBSTR() functions. The static string sqlite_encoding will be set 
			// to either "UTF-8" or "iso8859" to indicate how the library was compiled. In addition, the sqlite.h 
			// header file will define one of the macros SQLITE_UTF8 or SQLITE_ISO8859, as appropriate.
			// 
			// We have no way of knowing whether Sqlite 2 expects ISO8859 or UTF-8, but ISO8859 seems to be the
			// default.  Therefore, we need to use an ISO8859(-1) compatible encoding, like ANSI.
			// OTOH, the user may want to specify the encoding of the bytes stored in the database, regardless
			// of what Sqlite is treating them as, 
			
			// For Sqlite 3, we use the UTF-16 prepare function, so we need a UTF-16 string.
			/*
			if (parent_conn.Version == 2)
				psql = Sqlite.StringToHeap (sql.Trim(), parent_conn.Encoding);
			else
				psql = Marshal.StringToHGlobalUni (sql.Trim());
            */
            string queryval = sql.Trim();
            string pzTail = sql.Trim();
            IntPtr errMsgPtr;
			
			parent_conn.StartExec ();

			rows_affected = 0;
			
			try {
				while (true) {
                    Sqlite3.Vdbe pStmt = null;

                    queryval = pzTail;
                    GetNextStatement(queryval, ref pzTail, ref pStmt);

                    if (pStmt == null)
                        throw new Exception();
					
					// pzTail is positioned after the last byte in the
					// statement, which will be the NULL character if
					// this was the last statement.
					
                    bool last = pzTail.Length == 0;

                    try
                    {
                        if (parent_conn.Version == 3)
                            BindParameters3(pStmt);

                        if (last && want_results)
                            return new SqliteDataReader(this, pStmt, parent_conn.Version);

                        ExecuteStatement(pStmt);

                        if (last) // rows_affected is only used if !want_results
                            rows_affected = NumChanges();

                    }
                    finally
                    {
                        //if (parent_conn.Version == 3) 
                        Sqlite3.sqlite3_finalize(ref pStmt);
                        //else
                        //	Sqlite.sqlite_finalize (pStmt, out errMsgPtr);
                    }
					
					if (last) break;
				}

				return null;
			} finally {
				parent_conn.EndExec ();
				//Marshal.FreeHGlobal (psql);
			}
		}

		public int LastInsertRowID () 
		{
			return parent_conn.LastInsertRowId;
		}
		
		private string GetError3() {
            return Sqlite3.sqlite3_errmsg(parent_conn.Handle2);
		}
	#endregion
	}
}
#endif
