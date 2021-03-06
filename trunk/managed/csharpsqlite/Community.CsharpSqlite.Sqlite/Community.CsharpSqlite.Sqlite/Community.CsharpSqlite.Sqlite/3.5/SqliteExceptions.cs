using System;
using System.Data;

namespace Community.CsharpSqlite.Sqlite 
{
    public class SqliteException : ApplicationException
    {
        public SqliteException() : this("An unknown error occurred while doing your task")
        {
            
        }

        public SqliteException(string message) : base(message)
        {
        }

        public SqliteException(string message, Exception cause) : base(message, cause)
        {
            
        }
    }
	// This exception is raised whenever a statement cannot be compiled.
    public class SqliteSyntaxException : SqliteException
	{
		public SqliteSyntaxException() : this("An error occurred compiling the Sqlite command.")
		{
		}
		
		public SqliteSyntaxException(string message) : base(message)
		{
		}

		public SqliteSyntaxException(string message, Exception cause) : base(message, cause)
		{
		}
	}

	// This exception is raised whenever the execution
	// of a statement fails.
    public class SqliteExecutionException : SqliteException
	{
		public SqliteExecutionException() : this("An error occurred executing the Sqlite command.")
		{
		}
		
		public SqliteExecutionException(string message) : base(message)
		{
		}

		public SqliteExecutionException(string message, Exception cause) : base(message, cause)
		{
		}
	}

	// This exception is raised whenever Sqlite says it
	// cannot run a command because something is busy.
	public class SqliteBusyException : SqliteExecutionException
	{
		public SqliteBusyException() : this("The database is locked.")
		{
		}
		
		public SqliteBusyException(string message) : base(message)
		{
		}

		public SqliteBusyException(string message, Exception cause) : base(message, cause)
		{
		}
	}

}