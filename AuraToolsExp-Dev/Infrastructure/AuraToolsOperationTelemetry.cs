using System;
using System.Diagnostics;
using System.Globalization;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsOperationTelemetry
{
    public static IDisposable Track(string operation, double slowThresholdMs = 50d)
    {
        return new OperationScope(operation ?? "", Math.Max(1d, slowThresholdMs));
    }

    private sealed class OperationScope : IDisposable
    {
        private readonly string operation;
        private readonly double slowThresholdMs;
        private readonly Stopwatch stopwatch;
        private bool disposed;

        public OperationScope(string operation, double slowThresholdMs)
        {
            this.operation = operation;
            this.slowThresholdMs = slowThresholdMs;
            stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stopwatch.Stop();
            if (stopwatch.Elapsed.TotalMilliseconds < slowThresholdMs)
            {
                return;
            }

            AuraToolsLog.Warn("[Telemetry] slow operation. name="
                              + operation
                              + ", elapsedMs="
                              + stopwatch.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)
                              + ", thresholdMs="
                              + slowThresholdMs.ToString("F0", CultureInfo.InvariantCulture)
                              + ".");
        }
    }
}
