using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using AuraCombatAi.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal static class AuraToolsFoundationWorkerRuntime
{
    private const string WorkerDirectoryName = "TrainingWorker";
    private const string WorkerExecutableName = "AuraFoundationTrainer.Worker.exe";
    private const string ControlCenterExecutableName =
        "AuraFoundationTrainer.ControlCenter.exe";

    public static string ExecutablePath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(
                "AURA_FOUNDATION_WORKER_PATH");
            return !string.IsNullOrWhiteSpace(overridePath)
                ? Path.GetFullPath(overridePath)
                : Path.Combine(
                    AuraToolsConfigService.ModDirectory,
                    WorkerDirectoryName,
                    WorkerExecutableName);
        }
    }

    public static string ControlCenterExecutablePath =>
        Path.Combine(
            AuraToolsConfigService.ModDirectory,
            WorkerDirectoryName,
            ControlCenterExecutableName);

    public static bool LaunchControlCenter(out string message)
    {
        var path = ControlCenterExecutablePath;
        if (!File.Exists(path))
        {
            message = "独立训练控制台不存在：" + path;
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "--mod-root \""
                            + AuraToolsConfigService.ModDirectory
                                .Replace("\"", "\\\"")
                            + "\" --data-root \""
                            + AuraToolsConfigService.DataRootDirectory
                                .Replace("\"", "\\\"")
                            + "\"",
                WorkingDirectory = Path.GetDirectoryName(path)
                                   ?? AuraToolsConfigService.ModDirectory,
                UseShellExecute = true
            });
            message = "已打开独立底模训练控制台";
            return true;
        }
        catch (Exception ex)
        {
            message = "打开独立训练控制台失败：" + ex.Message;
            return false;
        }
    }

    public static bool ExternalTrainingActive()
    {
        var leasePath = Path.Combine(
            AuraToolsAutoBattleSimulationRuntime.ResultsRootDirectory,
            "foundation-success-cases",
            ".foundation-training.lock");
        if (!File.Exists(leasePath))
        {
            return false;
        }
        try
        {
            using var storage = new AuraSharedStorageCoordinator(
                AuraSharedPaths.RootDirectory);
            return storage.IsFileWriteLeaseHeld(leasePath);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAvailable(out string reason)
    {
        var path = ExecutablePath;
        if (!File.Exists(path))
        {
            reason = "独立训练器不存在：" + path;
            return false;
        }
        reason = "";
        return true;
    }

    public static CombatFoundationWorkerResult Run(
        CombatFoundationWorkerJob job,
        Action<CombatCampaignFoundationTelemetry> telemetry,
        Action<string> progressDiagnostic,
        CancellationToken cancellationToken)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));
        if (!IsAvailable(out var unavailable))
        {
            throw new FileNotFoundException(unavailable, ExecutablePath);
        }
        Directory.CreateDirectory(job.ResultDirectory);
        var jobPath = Path.Combine(job.ResultDirectory, "foundation-worker-job.json");
        WriteAtomic(jobPath, AuraSharedJson.Serialize(job));
        TryDelete(job.ProgressPath);
        TryDelete(job.ResultPath);
        TryDelete(job.CancellationPath);

        var outputGate = new object();
        var output = new Queue<string>();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = "--job \"" + jobPath.Replace("\"", "\\\"") + "\"",
                WorkingDirectory = Path.GetDirectoryName(ExecutablePath)
                                   ?? AuraToolsConfigService.ModDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };
        process.StartInfo.EnvironmentVariables["DOTNET_gcServer"] = "1";
        process.StartInfo.EnvironmentVariables["DOTNET_GCConserveMemory"] = "0";
        process.OutputDataReceived += (_, args) =>
            CaptureOutput(outputGate, output, args.Data);
        process.ErrorDataReceived += (_, args) =>
            CaptureOutput(outputGate, output, args.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("独立训练器进程未能启动");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        AuraToolsLog.Info(
            "[AutoBattle][Foundation][Worker] started pid="
            + process.Id
            + ", executable="
            + ExecutablePath);

        DateTime progressWriteUtc = DateTime.MinValue;
        var cancellationRequestedUtc = DateTime.MinValue;
        var lastProgressDiagnostic = "";
        while (!process.WaitForExit(250))
        {
            if (TryReadProgress(
                    job,
                    ref progressWriteUtc,
                    out var progress,
                    out var diagnostic))
            {
                lastProgressDiagnostic = "";
                telemetry(progress.Telemetry);
            }
            else if (!string.IsNullOrWhiteSpace(diagnostic)
                     && !string.Equals(
                         lastProgressDiagnostic,
                         diagnostic,
                         StringComparison.Ordinal))
            {
                lastProgressDiagnostic = diagnostic;
                AuraToolsLog.Warn(
                    "[AutoBattle][Foundation][Worker][Progress] "
                    + diagnostic);
                progressDiagnostic(diagnostic);
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
            if (cancellationRequestedUtc == DateTime.MinValue)
            {
                cancellationRequestedUtc = DateTime.UtcNow;
                WriteAtomic(job.CancellationPath, DateTime.UtcNow.ToString("O"));
            }
            else if ((DateTime.UtcNow - cancellationRequestedUtc).TotalSeconds >= 10d)
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // The worker may have exited between the wait and kill.
                }
            }
        }
        process.WaitForExit();
        if (TryReadProgress(
                job,
                ref progressWriteUtc,
                out var finalProgress,
                out var finalDiagnostic))
        {
            telemetry(finalProgress.Telemetry);
        }
        else if (!string.IsNullOrWhiteSpace(finalDiagnostic)
                 && !string.Equals(
                     lastProgressDiagnostic,
                     finalDiagnostic,
                     StringComparison.Ordinal))
        {
            AuraToolsLog.Warn(
                "[AutoBattle][Foundation][Worker][Progress] "
                + finalDiagnostic);
            progressDiagnostic(finalDiagnostic);
        }
        var result = ReadResult(job);
        if (!CombatFoundationWorkerProtocol.TryValidateResult(
                result,
                job.JobId,
                out var resultDiagnostic))
        {
            throw new InvalidOperationException(resultDiagnostic);
        }
        if (cancellationToken.IsCancellationRequested || result.Cancelled)
        {
            AuraToolsLog.Info(
                "[AutoBattle][Foundation][Worker] cancelled pid="
                + process.Id
                + ", resumable="
                + result.Resumable
                + ", checkpoint="
                + result.CheckpointPath);
            throw new OperationCanceledException(cancellationToken);
        }
        if (process.ExitCode != 0 || !result.Success || result.Training == null)
        {
            throw new InvalidOperationException(
                "独立训练器失败，exitCode="
                + process.ExitCode
                + "："
                + result.Message
                + Environment.NewLine
                + OutputTail(outputGate, output));
        }
        if (!string.Equals(
                result.RulesetHash,
                job.ExpectedRulesetHash,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "独立训练器结果规则哈希不匹配："
                + result.RulesetHash);
        }
        AuraToolsLog.Info(
            "[AutoBattle][Foundation][Worker] completed pid="
            + process.Id
            + ", runtime="
            + result.Runtime);
        return result;
    }

    private static bool TryReadProgress(
        CombatFoundationWorkerJob job,
        ref DateTime observedWriteUtc,
        out CombatFoundationWorkerProgress progress,
        out string diagnostic)
    {
        progress = new CombatFoundationWorkerProgress();
        diagnostic = "";
        try
        {
            if (!File.Exists(job.ProgressPath))
            {
                return false;
            }
            var writeUtc = File.GetLastWriteTimeUtc(job.ProgressPath);
            if (writeUtc <= observedWriteUtc)
            {
                return false;
            }
            var parsed = AuraSharedJson.Deserialize<CombatFoundationWorkerProgress>(
                File.ReadAllText(job.ProgressPath));
            observedWriteUtc = writeUtc;
            if (!CombatFoundationWorkerProtocol.TryValidateProgress(
                    parsed,
                    job.JobId,
                    out diagnostic))
            {
                return false;
            }
            progress = parsed!;
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = "无法读取底模训练进度："
                         + ex.GetType().Name
                         + "："
                         + ex.Message;
            return false;
        }
    }

    private static CombatFoundationWorkerResult ReadResult(
        CombatFoundationWorkerJob job)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (File.Exists(job.ResultPath))
                {
                    return AuraSharedJson.Deserialize<CombatFoundationWorkerResult>(
                               File.ReadAllText(job.ResultPath))
                           ?? throw new InvalidOperationException(
                               "独立训练器结果为空");
                }
            }
            catch (IOException ex)
            {
                last = ex;
            }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException(
            "独立训练器没有生成有效结果",
            last);
    }

    private static void CaptureOutput(
        object gate,
        Queue<string> output,
        string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }
        lock (gate)
        {
            output.Enqueue(line!);
            while (output.Count > 30)
            {
                output.Dequeue();
            }
        }
    }

    private static string OutputTail(object gate, Queue<string> output)
    {
        lock (gate)
        {
            return string.Join(Environment.NewLine, output);
        }
    }

    private static void WriteAtomic(string path, string text)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("输出目录不存在"));
        using var storage = new AuraSharedStorageCoordinator(
            AuraSharedPaths.RootDirectory);
        storage.WriteTextAtomic(fullPath, text, createBackup: false);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A stale diagnostic file does not make the worker unavailable.
        }
    }
}
