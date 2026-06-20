using System;
using System.Globalization;
using System.Text;

namespace AuraToolsExp.Dll.Features.Logging;

internal readonly struct AuraToolsLogRecord
{
    public AuraToolsLogRecord(DateTime timestamp, string source, string level, string? tag, string message, string? stackTrace)
    {
        Timestamp = timestamp;
        Source = source;
        Level = level;
        Tag = tag;
        Message = message;
        StackTrace = stackTrace;
    }

    private DateTime Timestamp { get; }

    private string Source { get; }

    private string Level { get; }

    private string? Tag { get; }

    private string Message { get; }

    private string? StackTrace { get; }

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
