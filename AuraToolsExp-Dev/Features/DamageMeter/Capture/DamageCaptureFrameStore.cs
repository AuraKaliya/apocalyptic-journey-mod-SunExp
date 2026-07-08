using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.DamageMeter.Capture;

internal interface IDamageCaptureFrame
{
    int Frame { get; set; }

    void Reset();
}

internal sealed class DamageFrameWindow<T> where T : class, IDamageCaptureFrame, new()
{
    private readonly int capacity;
    private readonly int poolCapacity;
    private readonly Action<T>? beforeRelease;
    private readonly List<T> items;
    private readonly Stack<T> pool;

    public DamageFrameWindow(int capacity, Action<T>? beforeRelease = null, int poolCapacity = 0)
    {
        this.capacity = Math.Max(1, capacity);
        this.poolCapacity = Math.Max(this.capacity, poolCapacity <= 0 ? this.capacity : poolCapacity);
        this.beforeRelease = beforeRelease;
        items = new List<T>(this.capacity);
        pool = new Stack<T>(this.poolCapacity);
    }

    public int Count => items.Count;

    public T this[int index] => items[index];

    public T Rent(int frame)
    {
        var item = pool.Count > 0 ? pool.Pop() : new T();
        item.Frame = frame;
        return item;
    }

    public void Add(T item)
    {
        if (item == null)
        {
            return;
        }

        items.Add(item);
        while (items.Count > capacity)
        {
            RemoveAt(0);
        }
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= items.Count)
        {
            return;
        }

        var item = items[index];
        items.RemoveAt(index);
        Release(item);
    }

    public void PruneOlderThan(int frame, int maxAge)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (frame - items[i].Frame > maxAge)
            {
                RemoveAt(i);
            }
        }
    }

    public void Clear()
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            Release(items[i]);
        }

        items.Clear();
    }

    private void Release(T item)
    {
        beforeRelease?.Invoke(item);
        item.Reset();
        if (pool.Count < poolCapacity)
        {
            pool.Push(item);
        }
    }
}
