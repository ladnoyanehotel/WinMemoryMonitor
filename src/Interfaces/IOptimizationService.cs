using System;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Worker process runner interface
    /// </summary>
    public interface IWorkerProcessRunner
    {
        event Action<string, WorkerResult> OnCompleted;

        WorkerResult Run(string operation);
    }

    /// <summary>
    /// Smart memory optimizer interface
    /// </summary>
    public interface ISmartMemoryOptimizer
    {
        event Action<SmartCleanupResult> OnCompleted;
        event Action IsRunningChanged;

        bool IsRunning { get; }
        SmartCleanupResult Cleanup(Enums.Memory.Optimization.Reason reason);
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
