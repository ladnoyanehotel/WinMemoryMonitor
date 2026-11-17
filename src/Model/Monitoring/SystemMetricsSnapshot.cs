using System;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Immutable performance metrics snapshot taken by the monitoring service.
    /// All byte values are in bytes; percentages are 0-100. Invalid/unavailable metrics are marked
    /// with the corresponding *Valid flags instead of throwing.
    /// </summary>
    public class SystemMetricsSnapshot
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SystemMetricsSnapshot"/> class.
        /// </summary>
        public SystemMetricsSnapshot()
        {
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets the UTC timestamp of the sample
        /// </summary>
        public DateTime Timestamp { get; internal set; }

        // CPU
        public bool CpuValid { get; internal set; }
        public double CpuUsage { get; internal set; }                 // 0-100, all logical processors

        // RAM
        public long RamTotalBytes { get; internal set; }
        public long RamAvailableBytes { get; internal set; }
        public long RamUsedBytes { get; internal set; }
        public int MemoryLoad { get; internal set; }                  // 0-100 as reported by the OS

        // Commit
        public long CommitTotalBytes { get; internal set; }
        public long CommitLimitBytes { get; internal set; }

        // Hard page faults (pages read from disk per second = real paging pressure)
        public bool HardFaultsValid { get; internal set; }
        public double HardFaultsPerSecond { get; internal set; }

        // Disk
        public bool DiskValid { get; internal set; }
        public double DiskActiveTimePercent { get; internal set; }     // 0-100
        public double DiskLatencyMs { get; internal set; }

        // GPU
        public bool GpuValid { get; internal set; }
        public double GpuUsagePercent { get; internal set; }           // 0-100
        public long VramUsedBytes { get; internal set; }
        public long VramTotalBytes { get; internal set; }              // 0 when unknown

        // System-wide object counts
        public int HandleCount { get; internal set; }
        public int ProcessCount { get; internal set; }
        public int ThreadCount { get; internal set; }

        /// <summary>
        /// Gets RAM usage percentage (0-100)
        /// </summary>
        public double RamUsagePercent
        {
            get { return RamTotalBytes > 0 ? Math.Round(100.0 * RamUsedBytes / RamTotalBytes, 1) : 0.0; }
        }

        /// <summary>
        /// Gets available RAM percentage (0-100)
        /// </summary>
        public double RamAvailablePercent
        {
            get { return RamTotalBytes > 0 ? Math.Round(100.0 * RamAvailableBytes / RamTotalBytes, 1) : 100.0; }
        }

        /// <summary>
        /// Gets commit charge percentage (0-100)
        /// </summary>
        public double CommitPercent
        {
            get { return CommitLimitBytes > 0 ? Math.Round(100.0 * CommitTotalBytes / CommitLimitBytes, 1) : 0.0; }
        }
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
