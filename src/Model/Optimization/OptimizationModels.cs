using System;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Native memory operation identifiers understood by the worker process
    /// </summary>
    public static class WorkerOperation
    {
        public const string LowPriorityStandby = "low-priority-standby";
        public const string Standby = "standby";
    }

    /// <summary>
    /// Result of a single worker process run
    /// </summary>
    public class WorkerResult
    {
        public bool Success { get; internal set; }
        public bool TimedOut { get; internal set; }
        public int ExitCode { get; internal set; }
        public int WorkerProcessId { get; internal set; }
        public string Message { get; internal set; }
        public TimeSpan Duration { get; internal set; }

        public static WorkerResult FromSuccess(int exitCode, int processId, string message, TimeSpan duration)
        {
            return new WorkerResult { Success = exitCode == 0, TimedOut = false, ExitCode = exitCode, WorkerProcessId = processId, Message = message, Duration = duration };
        }

        public static WorkerResult FromTimeout(int processId, TimeSpan duration)
        {
            return new WorkerResult { Success = false, TimedOut = true, ExitCode = -1, WorkerProcessId = processId, Message = "Timeout", Duration = duration };
        }

        public static WorkerResult FromFailure(string message)
        {
            return new WorkerResult { Success = false, TimedOut = false, ExitCode = -1, WorkerProcessId = 0, Message = message, Duration = TimeSpan.Zero };
        }
    }

    /// <summary>
    /// Outcome of a smart cleanup
    /// </summary>
    public enum CleanupOutcome
    {
        NotNeeded,
        Completed,
        Ineffective,
        Timeout,
        Failed
    }

    /// <summary>
    /// Full result of a smart cleanup run, including effectiveness measurements
    /// </summary>
    public class SmartCleanupResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SmartCleanupResult"/> class.
        /// </summary>
        public SmartCleanupResult()
        {
            Message = string.Empty;
            TopProcessName = string.Empty;
        }

        public Enums.Memory.Optimization.Reason Reason { get; internal set; }
        public CleanupOutcome Outcome { get; internal set; }
        public long FreedBytes { get; internal set; }
        public long AvailableAfter5Seconds { get; internal set; }
        public long AvailableBeforeBytes { get; internal set; }
        public long AvailableAfterBytes { get; internal set; }
        public TimeSpan Duration { get; internal set; }
        public string Message { get; internal set; }
        public string TopProcessName { get; internal set; }
        public double TopProcessPrivateGb { get; internal set; }
        public bool UsedLevel2 { get; internal set; }
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
