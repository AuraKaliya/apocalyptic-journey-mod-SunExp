using System;
using System.Globalization;
using System.Text;

namespace LogExp.Dll.Infrastructure;

internal readonly struct LogRecord
{
    public LogRecord(DateTime timestamp, string source, string level, string? tag, string message, string? stackTrace)
    {
        Timestamp = timestamp;
        Source = source;
        Level = level;
        Tag = tag;
        Message = message;
        StackTrace = stackTrace;
    }

    public DateTime Timestamp { get; }

    public string Source { get; }

    public string Level { get; }

    public string? Tag { get; }

    public string Message { get; }

    public string? StackTrace { get; }

    public string Format()
    {
        var builder = new StringBuilder();
        builder.Append('[')
            .Append(Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append("] [")
            .Append(Source)
            .Append('/')
            .Append(Level)
            .Append("] ");

        if (!string.IsNullOrWhiteSpace(Tag))
        {
            builder.Append('[').Append(Tag).Append("] ");
        }

        builder.Append(Message);

        if (!string.IsNullOrWhiteSpace(StackTrace))
        {
            builder.AppendLine();
            builder.Append(StackTrace);
        }

        return builder.ToString();
    }
}
