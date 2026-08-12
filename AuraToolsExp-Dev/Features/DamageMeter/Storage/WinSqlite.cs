using System;
using System.Runtime.InteropServices;

namespace AuraToolsExp.Dll.Features.DamageMeter.Storage;

internal sealed class WinSqliteConnection : IDisposable
{
    private const int Ok = 0;
    private const int Row = 100;
    private const int Done = 101;
    private IntPtr database;

    internal WinSqliteConnection(string path)
    {
        var result = Native.sqlite3_open16(path, out database);
        if (result != Ok || database == IntPtr.Zero)
        {
            var message = ErrorMessage(database);
            if (database != IntPtr.Zero)
            {
                Native.sqlite3_close_v2(database);
                database = IntPtr.Zero;
            }

            throw new InvalidOperationException("SQLite open failed (" + result + "): " + message);
        }

        Native.sqlite3_busy_timeout(database, 5000);
    }

    internal int Changes => Native.sqlite3_changes(database);

    internal void Execute(string sql)
    {
        using var statement = Prepare(sql);
        statement.Execute();
    }

    internal WinSqliteStatement Prepare(string sql)
    {
        var result = Native.sqlite3_prepare16_v2(database, sql, -1, out var statement, IntPtr.Zero);
        if (result != Ok || statement == IntPtr.Zero)
        {
            throw new InvalidOperationException("SQLite prepare failed (" + result + "): " + ErrorMessage(database));
        }

        return new WinSqliteStatement(database, statement);
    }

    public void Dispose()
    {
        if (database == IntPtr.Zero)
        {
            return;
        }

        Native.sqlite3_close_v2(database);
        database = IntPtr.Zero;
    }

    internal static string ErrorMessage(IntPtr database)
    {
        if (database == IntPtr.Zero)
        {
            return "database handle unavailable";
        }

        var pointer = Native.sqlite3_errmsg16(database);
        return pointer == IntPtr.Zero ? "unknown SQLite error" : Marshal.PtrToStringUni(pointer) ?? "unknown SQLite error";
    }

    internal sealed class WinSqliteStatement : IDisposable
    {
        private static readonly IntPtr Transient = new(-1);
        private readonly IntPtr database;
        private IntPtr statement;

        internal WinSqliteStatement(IntPtr database, IntPtr statement)
        {
            this.database = database;
            this.statement = statement;
        }

        internal void Bind(int index, string? value)
        {
            var result = value == null
                ? Native.sqlite3_bind_null(statement, index)
                : Native.sqlite3_bind_text16(statement, index, value, -1, Transient);
            Check(result, "bind text");
        }

        internal void Bind(int index, long value)
        {
            Check(Native.sqlite3_bind_int64(statement, index, value), "bind integer");
        }

        internal void Bind(int index, double value)
        {
            Check(Native.sqlite3_bind_double(statement, index, value), "bind number");
        }

        internal void Bind(int index, byte[]? value)
        {
            var result = value == null
                ? Native.sqlite3_bind_null(statement, index)
                : Native.sqlite3_bind_blob(statement, index, value, value.Length, Transient);
            Check(result, "bind blob");
        }

        internal bool Read()
        {
            var result = Native.sqlite3_step(statement);
            if (result == Row)
            {
                return true;
            }

            if (result == Done)
            {
                return false;
            }

            throw new InvalidOperationException("SQLite query failed (" + result + "): " + ErrorMessage(database));
        }

        internal void Execute()
        {
            while (Read())
            {
            }
        }

        internal long Int64(int column)
        {
            return Native.sqlite3_column_int64(statement, column);
        }

        internal double Double(int column)
        {
            return Native.sqlite3_column_double(statement, column);
        }

        internal string Text(int column)
        {
            var pointer = Native.sqlite3_column_text16(statement, column);
            if (pointer == IntPtr.Zero)
            {
                return "";
            }

            var byteCount = Native.sqlite3_column_bytes16(statement, column);
            return byteCount <= 0 ? "" : Marshal.PtrToStringUni(pointer, byteCount / 2) ?? "";
        }

        internal byte[] Blob(int column)
        {
            var pointer = Native.sqlite3_column_blob(statement, column);
            var byteCount = Native.sqlite3_column_bytes(statement, column);
            if (pointer == IntPtr.Zero || byteCount <= 0)
            {
                return Array.Empty<byte>();
            }

            var result = new byte[byteCount];
            Marshal.Copy(pointer, result, 0, byteCount);
            return result;
        }

        public void Dispose()
        {
            if (statement == IntPtr.Zero)
            {
                return;
            }

            Native.sqlite3_finalize(statement);
            statement = IntPtr.Zero;
        }

        private void Check(int result, string operation)
        {
            if (result != Ok)
            {
                throw new InvalidOperationException("SQLite " + operation + " failed (" + result + "): " + ErrorMessage(database));
            }
        }
    }

    private static class Native
    {
        private const string Library = "winsqlite3.dll";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open16([MarshalAs(UnmanagedType.LPWStr)] string filename, out IntPtr database);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close_v2(IntPtr database);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_errmsg16(IntPtr database);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare16_v2(
            IntPtr database,
            [MarshalAs(UnmanagedType.LPWStr)] string sql,
            int byteCount,
            out IntPtr statement,
            IntPtr tail);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr statement);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr statement);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_null(IntPtr statement, int index);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_double(IntPtr statement, int index, double value);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_text16(
            IntPtr statement,
            int index,
            [MarshalAs(UnmanagedType.LPWStr)] string value,
            int byteCount,
            IntPtr destructor);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_blob(IntPtr statement, int index, byte[] value, int byteCount, IntPtr destructor);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_column_int64(IntPtr statement, int column);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern double sqlite3_column_double(IntPtr statement, int column);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text16(IntPtr statement, int column);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_bytes16(IntPtr statement, int column);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_bytes(IntPtr statement, int column);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_changes(IntPtr database);
    }
}
