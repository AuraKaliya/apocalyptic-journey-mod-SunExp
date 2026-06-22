using System;
using System.Collections.Generic;

namespace AuraOnline.Shared;

public sealed class AuraChatLocalStore
{
    private readonly List<AuraChatMessage> messages = new();
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);

    public AuraChatLocalStore(int maxMessages = 80)
    {
        MaxMessages = Math.Max(1, maxMessages);
    }

    public int MaxMessages { get; set; }

    public IReadOnlyList<AuraChatMessage> Messages => messages;

    public bool Add(AuraChatMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.MessageId) || !seen.Add(message.MessageId))
        {
            return false;
        }

        messages.Add(message);
        while (messages.Count > MaxMessages)
        {
            seen.Remove(messages[0].MessageId);
            messages.RemoveAt(0);
        }

        return true;
    }

    public void Clear()
    {
        messages.Clear();
        seen.Clear();
    }
}
