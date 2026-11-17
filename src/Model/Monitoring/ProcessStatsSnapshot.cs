using System;
using System.Collections.Generic;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Aggregated statistics of a single process (or a group of processes with the same name)
    /// </summary>
    public class ProcessStats
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessStats"/> class.
        /// </summary>
        public ProcessStats()
        {
            Name = string.Empty;
        }

        public string Name { get; internal set; }
        public int Id { get; internal set; }                          // First process id of the group, 0 for aggregated groups
        public int InstanceCount { get; internal set; }
        public double CpuPercent { get; internal set; }               // 0-100 of total system CPU
        public long PrivateBytes { get; internal set; }
        public long WorkingSetBytes { get; internal set; }
    }

    /// <summary>
    /// Process monitoring snapshot: top CPU and top memory consumers
    /// </summary>
    public class ProcessStatsSnapshot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessStatsSnapshot"/> class.
        /// </summary>
        public ProcessStatsSnapshot()
        {
            TopByCpu = new List<ProcessStats>();
            TopByMemory = new List<ProcessStats>();
        }

        /// <summary>
        /// Gets the UTC timestamp of the sample
        /// </summary>
        public DateTime Timestamp { get; internal set; }

        public bool Valid { get; internal set; }
        public List<ProcessStats> TopByCpu { get; internal set; }
        public List<ProcessStats> TopByMemory { get; internal set; }
    }

    /// <summary>
    /// Suspicious memory growth report for a process (possible leak; never asserted as a fact)
    /// </summary>
    public class MemoryLeakSuspect
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryLeakSuspect"/> class.
        /// </summary>
        public MemoryLeakSuspect()
        {
            Name = string.Empty;
        }

        public string Name { get; internal set; }
        public long GrowthBytes { get; internal set; }
        public TimeSpan Duration { get; internal set; }
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
