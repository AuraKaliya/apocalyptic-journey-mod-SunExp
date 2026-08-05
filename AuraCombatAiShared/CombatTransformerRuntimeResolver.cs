using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatTransformerRuntimeProtocol
{
    public const string Version = "aura.transformer-runtime-probe.v1";

    public const string AutomaticExecutable = "auto";
}

public sealed class CombatTransformerRuntimeProbe
{
    public string Protocol { get; set; } = CombatTransformerRuntimeProtocol.Version;

    public bool Success { get; set; }

    public string RequestedBackend { get; set; } = "";

    public string EffectiveBackend { get; set; } = "";

    public string ExecutablePath { get; set; } = "";

    public string ResolutionSource { get; set; } = "";

    public string PythonVersion { get; set; } = "";

    public string TorchVersion { get; set; } = "";

    public string NumpyVersion { get; set; } = "";

    public bool CudaAvailable { get; set; }

    public string DeviceName { get; set; } = "";

    public long DeviceMemoryBytes { get; set; }

    public string Message { get; set; } = "";
}

public static class CombatTransformerRuntimeResolver
{
    private const string ProbePrefix = "AURA_TF_RUNTIME_V1|";

    public static CombatTransformerRuntimeProbe Resolve(
        string? configuredExecutable,
        string? backend,
        IEnumerable<string>? searchRoots = null,
        int timeoutMilliseconds = 20_000)
    {
        var normalizedBackend = CombatTransformerTeacherBackendNames.Normalize(
            backend);
        if (string.Equals(
                normalizedBackend,
                CombatTransformerTeacherBackendNames.Disabled,
                StringComparison.Ordinal))
        {
            return new CombatTransformerRuntimeProbe
            {
                RequestedBackend = normalizedBackend,
                Message = "Transformer teacher backend is disabled."
            };
        }

        var configured = (configuredExecutable ?? "").Trim();
        var automatic = string.IsNullOrWhiteSpace(configured)
                        || string.Equals(
                            configured,
                            CombatTransformerRuntimeProtocol.AutomaticExecutable,
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            configured,
                            "python",
                            StringComparison.OrdinalIgnoreCase);
        if (!automatic)
        {
            return ProbeCandidate(
                configured,
                "explicit",
                normalizedBackend,
                timeoutMilliseconds);
        }

        var candidates = BuildCandidates(normalizedBackend, searchRoots);
        var failures = new List<string>();
        var valid = new List<CombatTransformerRuntimeProbe>();
        foreach (var candidate in candidates)
        {
            var probe = ProbeCandidate(
                candidate.Executable,
                candidate.Source,
                normalizedBackend,
                timeoutMilliseconds);
            if (probe.Success)
            {
                valid.Add(probe);
            }
            else if (!string.IsNullOrWhiteSpace(probe.Message))
            {
                failures.Add(candidate.Source + ": " + probe.Message);
            }
        }

        var selected = string.Equals(
                normalizedBackend,
                CombatTransformerTeacherBackendNames.Auto,
                StringComparison.Ordinal)
            ? valid.FirstOrDefault(item => item.CudaAvailable)
              ?? valid.FirstOrDefault()
            : valid.FirstOrDefault();
        if (selected != null)
        {
            return selected;
        }
        return new CombatTransformerRuntimeProbe
        {
            RequestedBackend = normalizedBackend,
            Message = failures.Count == 0
                ? "No usable Python/PyTorch runtime was discovered."
                : string.Join("; ", failures.Take(4))
        };
    }

    public static string DisplayText(CombatTransformerRuntimeProbe? probe)
    {
        if (probe?.Success != true)
        {
            return probe?.Message ?? "Transformer runtime has not been probed.";
        }
        var deviceMemory = probe.DeviceMemoryBytes <= 0
            ? ""
            : " / "
              + Math.Round(probe.DeviceMemoryBytes / (1024d * 1024d * 1024d), 1)
              + " GB";
        return probe.ResolutionSource
               + " · "
               + probe.EffectiveBackend.ToUpperInvariant()
               + " · Python "
               + probe.PythonVersion
               + " · Torch "
               + probe.TorchVersion
               + " · "
               + probe.DeviceName
               + deviceMemory
               + Environment.NewLine
               + probe.ExecutablePath;
    }

    private static IReadOnlyList<RuntimeCandidate> BuildCandidates(
        string backend,
        IEnumerable<string>? searchRoots)
    {
        var result = new List<RuntimeCandidate>();
        var registered = Environment.GetEnvironmentVariable(
            "AURA_TRANSFORMER_PYTHON");
        Add(result, registered, "environment");

        var local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var runtimeNames = string.Equals(
            backend,
            CombatTransformerTeacherBackendNames.Cuda,
            StringComparison.Ordinal)
            ? new[] { "cuda" }
            : string.Equals(
                backend,
                CombatTransformerTeacherBackendNames.Cpu,
                StringComparison.Ordinal)
                ? new[] { "cpu", "cuda" }
                : new[] { "cuda", "cpu" };
        foreach (var runtimeName in runtimeNames)
        {
            Add(
                result,
                VirtualEnvironmentPython(
                    Path.Combine(local, "AuraTF", runtimeName)),
                "managed-" + runtimeName);
        }

        foreach (var root in searchRoots ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }
            var fullRoot = Path.GetFullPath(root);
            Add(
                result,
                VirtualEnvironmentPython(Path.Combine(fullRoot, ".venv")),
                "local-.venv");
            Add(
                result,
                VirtualEnvironmentPython(Path.Combine(fullRoot, "venv")),
                "local-venv");
        }

        Add(result, FindOnPath("python"), "path-python");
        Add(result, FindOnPath("python3"), "path-python3");
        if (IsWindows())
        {
            Add(result, FindOnPath("py"), "path-launcher");
        }
        return result;
    }

    private static CombatTransformerRuntimeProbe ProbeCandidate(
        string executable,
        string source,
        string backend,
        int timeoutMilliseconds)
    {
        var executableValue = executable ?? "";
        var result = new CombatTransformerRuntimeProbe
        {
            RequestedBackend = backend,
            ExecutablePath = executableValue,
            ResolutionSource = source ?? ""
        };
        if (string.IsNullOrWhiteSpace(executableValue))
        {
            result.Message = "candidate is empty";
            return result;
        }
        try
        {
            var command =
                "import platform,torch,numpy;"
                + "c=bool(torch.cuda.is_available());"
                + "n=(torch.cuda.get_device_name(0) if c else (platform.processor() or platform.machine()));"
                + "m=(torch.cuda.get_device_properties(0).total_memory if c else 0);"
                + "print('"
                + ProbePrefix
                + "'+platform.python_version()+'|'+str(torch.__version__)+'|'+str(numpy.__version__)+'|'+('1' if c else '0')+'|'+str(n).replace('|','/')+'|'+str(m))";
            var start = new ProcessStartInfo
            {
                FileName = executableValue,
                Arguments = "-c " + Quote(command),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(start);
            if (process == null)
            {
                result.Message = "process could not be started";
                return result;
            }
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Math.Max(1_000, timeoutMilliseconds)))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                }
                result.Message = "runtime probe timed out";
                return result;
            }
            standardOutput.Wait(Math.Max(1_000, timeoutMilliseconds));
            standardError.Wait(Math.Max(1_000, timeoutMilliseconds));
            if (process.ExitCode != 0)
            {
                result.Message = Tail(standardError.Result, 600);
                return result;
            }
            var payload = (standardOutput.Result ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(line => line.StartsWith(
                    ProbePrefix,
                    StringComparison.Ordinal));
            if (payload == null)
            {
                result.Message = "runtime probe returned no capability record";
                return result;
            }
            var fields = payload.Substring(ProbePrefix.Length).Split('|');
            if (fields.Length < 6)
            {
                result.Message = "runtime capability record is incomplete";
                return result;
            }
            result.PythonVersion = fields[0];
            result.TorchVersion = fields[1];
            result.NumpyVersion = fields[2];
            result.CudaAvailable = fields[3] == "1";
            result.EffectiveBackend = string.Equals(
                backend,
                CombatTransformerTeacherBackendNames.Cpu,
                StringComparison.Ordinal)
                ? CombatTransformerTeacherBackendNames.Cpu
                : result.CudaAvailable
                    ? CombatTransformerTeacherBackendNames.Cuda
                    : CombatTransformerTeacherBackendNames.Cpu;
            result.DeviceName = fields[4];
            long.TryParse(fields[5], out var memory);
            result.DeviceMemoryBytes = Math.Max(0L, memory);
            if (string.Equals(
                    backend,
                    CombatTransformerTeacherBackendNames.Cuda,
                    StringComparison.Ordinal)
                && !result.CudaAvailable)
            {
                result.Message = "CUDA was requested but torch.cuda.is_available() is false";
                return result;
            }
            result.ExecutablePath = ResolveDisplayPath(executableValue);
            result.Success = true;
            result.Message = "Transformer runtime is ready.";
            return result;
        }
        catch (Exception ex)
        {
            result.Message = ex.Message;
            return result;
        }
    }

    private static void Add(
        ICollection<RuntimeCandidate> destination,
        string? executable,
        string source)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }
        var value = (executable ?? "").Trim();
        if (destination.Any(item => string.Equals(
                ResolveDisplayPath(item.Executable),
                ResolveDisplayPath(value),
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        destination.Add(new RuntimeCandidate(value, source));
    }

    private static string VirtualEnvironmentPython(string directory)
    {
        return Path.Combine(
            directory,
            IsWindows() ? "Scripts" : "bin",
            IsWindows() ? "python.exe" : "python");
    }

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };
        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim(), name + extension);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                }
            }
        }
        return null;
    }

    private static string ResolveDisplayPath(string value)
    {
        try
        {
            return File.Exists(value) ? Path.GetFullPath(value) : value;
        }
        catch
        {
            return value;
        }
    }

    private static string Quote(string value)
    {
        return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string Tail(string value, int maximum)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maximum
            ? text
            : text.Substring(text.Length - maximum);
    }

    private static bool IsWindows()
    {
        return Path.DirectorySeparatorChar == '\\';
    }

    private sealed class RuntimeCandidate
    {
        public RuntimeCandidate(string executable, string source)
        {
            Executable = executable;
            Source = source;
        }

        public string Executable { get; }

        public string Source { get; }
    }
}
