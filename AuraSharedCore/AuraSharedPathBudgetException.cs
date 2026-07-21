using System;

namespace AuraShared.Core;

public sealed class AuraSharedPathBudgetException : Exception
{
    public AuraSharedPathBudgetException(string operation, string path, int maximumLength)
        : base("Shared storage path exceeds the portable Windows path budget: operation="
               + (operation ?? "unknown")
               + ", length=" + (path ?? "").Length
               + ", maximum=" + maximumLength
               + ", path=" + (path ?? ""))
    {
        Operation = operation ?? "unknown";
        Path = path ?? "";
        PathLength = Path.Length;
        MaximumLength = maximumLength;
    }

    public string Operation { get; }

    public string Path { get; }

    public int PathLength { get; }

    public int MaximumLength { get; }
}
