using System;
using System.Collections;
using System.IO;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed class ReplayLoadedRecord
{
    internal MatchRecord Record { get; set; } = new();
    internal ReplayDocumentEnvelopeV17 Envelope { get; set; } = new();
    internal static ReplayLoadedRecord Read(string id)
    {
        var database = MatchRecordStorage.Database;
        var record = database.Get(id) ?? throw new InvalidDataException("找不到这条对局记录。");
        var envelope = database.LoadV17(id, loadAssetPayloads: true) ?? throw new InvalidDataException("找不到经过验证的回放文档。");
        foreach (var asset in envelope.Document.Assets)
        {
            var error = ReplayAssetContractV17.Validate(asset, requirePayload: true);
            if (error.Length > 0) throw new InvalidDataException("回放内嵌资源缺失或损坏：" + error);
        }
        return new ReplayLoadedRecord { Record = record, Envelope = envelope };
    }
}

// Unity coroutines await I/O without a synchronous wait or a polling file read.
internal sealed class ReplayIoOperation<T> : IEnumerator
{
    private bool completed;
    private T result = default!;
    private Exception? error;
    internal ReplayIoOperation(string source, Func<T> work)
    {
        ReplayBackgroundWork.Storage.Enqueue(source, work,
            value => { result = value; completed = true; },
            failure => { error = failure; completed = true; });
    }
    public object? Current => null;
    public bool MoveNext() => !completed;
    public void Reset() => throw new NotSupportedException();
    internal T Result => !completed ? throw new InvalidOperationException("I/O is still pending.")
        : error != null ? throw error : result;
}
