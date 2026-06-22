using System;
using System.Collections.Generic;

namespace AuraOnline.Shared;

public sealed class AuraChatRateLimiter
{
    private readonly int shortWindowLimit;
    private readonly TimeSpan shortWindow;
    private readonly int longWindowLimit;
    private readonly TimeSpan longWindow;
    private readonly Dictionary<string, Queue<DateTime>> recent = new(StringComparer.Ordinal);

    public AuraChatRateLimiter(
        int shortWindowLimit = 1,
        int shortWindowSeconds = 1,
        int longWindowLimit = 3,
        int longWindowSeconds = 5)
    {
        this.shortWindowLimit = Math.Max(1, shortWindowLimit);
        shortWindow = TimeSpan.FromSeconds(Math.Max(1, shortWindowSeconds));
        this.longWindowLimit = Math.Max(this.shortWindowLimit, longWindowLimit);
        longWindow = TimeSpan.FromSeconds(Math.Max(1, longWindowSeconds));
    }

    public bool Allow(string senderId, DateTime nowUtc)
    {
        senderId = string.IsNullOrWhiteSpace(senderId) ? "unknown" : senderId.Trim();
        if (!recent.TryGetValue(senderId, out var queue))
        {
            queue = new Queue<DateTime>();
            recent[senderId] = queue;
        }

        while (queue.Count > 0 && nowUtc - queue.Peek() > longWindow)
        {
            queue.Dequeue();
        }

        var shortCount = 0;
        foreach (var item in queue)
        {
            if (nowUtc - item <= shortWindow)
            {
                shortCount++;
            }
        }

        if (shortCount >= shortWindowLimit || queue.Count >= longWindowLimit)
        {
            return false;
        }

        queue.Enqueue(nowUtc);
        return true;
    }
}
