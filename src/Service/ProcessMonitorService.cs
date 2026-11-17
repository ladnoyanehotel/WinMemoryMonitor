using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Samples per-process statistics every 2 seconds (CPU %, private bytes, working set)
    /// and aggregates them by process name. Every <see cref="Process"/> object is disposed;
    /// failures on individual processes (access denied, exited, invalid handle) never abort the pass.
    /// Also runs a slow memory-growth detector that reports suspicious increases as possible leaks.
    /// </summary>
    public class ProcessMonitorService : IProcessMonitorService, IDisposable
    {
        #region Fields

        private const int TopCount = 8;
        private const int LeakDetectorIntervalMs = 30000;      // 30 seconds
        private const int LeakDetectorWindow = 40;             // 40 x 30s = 20 minutes of history
        private const long LeakGrowthThresholdBytes = 1024L * 1024 * 1024; // 1 GB
        private const int LeakGrowthWindowMinutes = 15;
        private static readonly TimeSpan LeakReportCooldown = new TimeSpan(1, 0, 0);

        private readonly SamplingScheduler _scheduler;
        private readonly Dictionary<int, ProcessCpuState> _cpuStates = new Dictionary<int, ProcessCpuState>(256);
        private readonly Dictionary<string, NameAggregate> _currentAggregates = new Dictionary<string, NameAggregate>(256, StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GrowthTracker> _growthTrackers = new Dictionary<string, GrowthTracker>(64, StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _leakReportedAt = new Dictionary<string, DateTime>(8, StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly object _sync = new object();

        private long _lastElapsedMs;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessMonitorService"/> class.
        /// </summary>
        /// <param name="scheduler">Shared sampling scheduler</param>
        public ProcessMonitorService(SamplingScheduler scheduler)
        {
            _scheduler = scheduler;

            _scheduler.Register(SampleProcesses, 2000);
            _scheduler.Register(DetectGrowth, LeakDetectorIntervalMs);
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs after every process sample (about once per 2 seconds)
        /// </summary>
        public event Action<ProcessStatsSnapshot> OnSnapshot;

        /// <summary>
        /// Occurs when a suspicious memory growth is detected (possible leak; never asserted as a fact)
        /// </summary>
        public event Action<MemoryLeakSuspect> OnLeakSuspected;

        #endregion

        #region Methods

        private void SampleProcesses()
        {
            var snapshot = new ProcessStatsSnapshot { Timestamp = DateTime.UtcNow, Valid = false };

            try
            {
                var elapsedMs = _stopwatch.ElapsedMilliseconds;
                var deltaMs = elapsedMs - _lastElapsedMs;
                _lastElapsedMs = elapsedMs;

                if (deltaMs <= 0)
                    deltaMs = 2000;

                var aggregates = _currentAggregates;
                aggregates.Clear();

                // CPU deltas need the previous cumulative times per PID
                var livePids = new HashSet<int>();
                Process[] processes = null;

                try
                {
                    processes = Process.GetProcesses();

                    foreach (var process in processes)
                    {
                        int id;

                        try
                        {
                            id = process.Id;
                            livePids.Add(id);

                            string name;
                            long privateBytes;
                            long workingSet;
                            long totalProcessorTicks;

                            try
                            {
                                name = process.ProcessName;
                                privateBytes = TryRead(() => process.PrivateMemorySize64);
                                workingSet = TryRead(() => process.WorkingSet64);
                                totalProcessorTicks = -1;

                                try
                                {
                                    totalProcessorTicks = process.TotalProcessorTime.Ticks;
                                }
                                catch (Exception)
                                {
                                    totalProcessorTicks = -1; // Access denied or process exiting
                                }
                            }
                            catch (Exception)
                            {
                                continue; // Process exited while being enumerated
                            }

                            NameAggregate aggregate;

                            if (!aggregates.TryGetValue(name, out aggregate))
                            {
                                aggregate = new NameAggregate { Name = name, FirstId = id };
                                aggregates.Add(name, aggregate);
                            }

                            aggregate.InstanceCount++;
                            aggregate.PrivateBytes += Math.Max(0, privateBytes);
                            aggregate.WorkingSetBytes += Math.Max(0, workingSet);

                            if (totalProcessorTicks >= 0)
                            {
                                ProcessCpuState state;

                                if (_cpuStates.TryGetValue(id, out state))
                                {
                                    var deltaTicks = totalProcessorTicks - state.LastTotalTicks;

                                    if (deltaTicks > 0)
                                    {
                                        // Ticks are 100ns; normalize to % of ALL logical processors
                                        var usedMs = deltaTicks / 10000.0;
                                        aggregate.CpuMs += usedMs;
                                    }
                                }

                                _cpuStates[id] = new ProcessCpuState { LastTotalTicks = totalProcessorTicks };
                            }
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }

                    snapshot.Valid = true;
                }
                finally
                {
                    if (processes != null)
                    {
                        foreach (var process in processes)
                            process.Dispose();
                    }
                }

                // Drop CPU states of processes that no longer exist (bounded dictionary)
                if (_cpuStates.Count > livePids.Count + 64)
                {
                    var dead = new List<int>();

                    foreach (var pid in _cpuStates.Keys)
                    {
                        if (!livePids.Contains(pid))
                            dead.Add(pid);
                    }

                    foreach (var pid in dead)
                        _cpuStates.Remove(pid);
                }

                var elapsedSeconds = deltaMs / 1000.0;

                foreach (var aggregate in aggregates.Values)
                {
                    var cpuPercent = elapsedSeconds > 0 ? 100.0 * (aggregate.CpuMs / 1000.0) / (elapsedSeconds * Environment.ProcessorCount) : 0;

                    if (cpuPercent < 0)
                        cpuPercent = 0;

                    if (cpuPercent > 100)
                        cpuPercent = 100;

                    aggregate.CpuPercent = Math.Round(cpuPercent, 1);
                }

                snapshot.TopByCpu = BuildTop(aggregates.Values, true);
                snapshot.TopByMemory = BuildTop(aggregates.Values, false);

                lock (_sync)
                {
                    LastSnapshot = snapshot;
                }

                var handler = OnSnapshot;

                if (handler != null)
                    handler(snapshot);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private static long TryRead(Func<long> reader)
        {
            try
            {
                return reader();
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static List<ProcessStats> BuildTop(IEnumerable<NameAggregate> aggregates, bool byCpu)
        {
            var list = new List<ProcessStats>();

            foreach (var aggregate in aggregates)
            {
                if (aggregate.PrivateBytes <= 0 && aggregate.CpuPercent <= 0)
                    continue;

                list.Add(new ProcessStats
                {
                    Name = aggregate.Name,
                    Id = aggregate.FirstId,
                    InstanceCount = aggregate.InstanceCount,
                    CpuPercent = aggregate.CpuPercent,
                    PrivateBytes = aggregate.PrivateBytes,
                    WorkingSetBytes = aggregate.WorkingSetBytes
                });
            }

            list.Sort(byCpu
                ? (Comparison<ProcessStats>)((a, b) => b.CpuPercent.CompareTo(a.CpuPercent))
                : (a, b) => b.PrivateBytes.CompareTo(a.PrivateBytes));

            if (list.Count > TopCount)
                list.RemoveRange(TopCount, list.Count - TopCount);

            return list;
        }

        private void DetectGrowth()
        {
            try
            {
                ProcessStatsSnapshot snapshot;

                lock (_sync)
                {
                    snapshot = LastSnapshot;
                }

                if (snapshot == null || !snapshot.Valid)
                    return;

                var now = DateTime.UtcNow;

                foreach (var stats in snapshot.TopByMemory)
                {
                    // The detector works on the aggregated top set only; full per-process tracking
                    // of hundreds of background processes would cost more than it is worth.
                    GrowthTracker tracker;

                    if (!_growthTrackers.TryGetValue(stats.Name, out tracker))
                    {
                        tracker = new GrowthTracker();
                        _growthTrackers.Add(stats.Name, tracker);
                    }

                    tracker.Add(now, stats.PrivateBytes);

                    var first = tracker.FirstTimestamp;
                    var firstBytes = tracker.FirstBytes;

                    if (!first.HasValue)
                        continue;

                    var duration = now - first.Value;

                    if (duration.TotalMinutes < LeakGrowthWindowMinutes)
                        continue;

                    var growth = stats.PrivateBytes - firstBytes;

                    if (growth < LeakGrowthThresholdBytes)
                        continue;

                    // Only report when the process has NOT given the memory back
                    if (tracker.MaxBytes - stats.PrivateBytes > LeakGrowthThresholdBytes / 4)
                        continue;

                    DateTime reportedAt;
                    var recentlyReported = _leakReportedAt.TryGetValue(stats.Name, out reportedAt) && now - reportedAt < LeakReportCooldown;

                    if (recentlyReported)
                        continue;

                    _leakReportedAt[stats.Name] = now;
                    tracker.Reset();

                    var handler = OnLeakSuspected;

                    if (handler != null)
                        handler(new MemoryLeakSuspect { Name = stats.Name, GrowthBytes = growth, Duration = duration });
                }

                // Bound the tracker dictionary
                if (_growthTrackers.Count > 128)
                {
                    var stale = new List<string>();

                    foreach (var pair in _growthTrackers)
                    {
                        if (!WasRecentlySeen(pair.Value, now))
                            stale.Add(pair.Key);
                    }

                    foreach (var name in stale)
                        _growthTrackers.Remove(name);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private static bool WasRecentlySeen(GrowthTracker tracker, DateTime now)
        {
            lock (tracker)
            {
                return tracker.LastTimestamp.HasValue && now - tracker.LastTimestamp.Value < new TimeSpan(0, 10, 0);
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the last process snapshot
        /// </summary>
        public ProcessStatsSnapshot LastSnapshot { get; private set; }

        #endregion

        #region IDisposable

        /// <summary>
        /// Releases resources. Sampling is stopped by disposing the shared scheduler.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            lock (_sync)
            {
                _cpuStates.Clear();
                _growthTrackers.Clear();
                _leakReportedAt.Clear();
            }
        }

        #endregion

        #region Nested types

        private sealed class ProcessCpuState
        {
            public long LastTotalTicks;
        }

        private sealed class NameAggregate
        {
            public string Name;
            public int FirstId;
            public int InstanceCount;
            public double CpuMs;
            public double CpuPercent;
            public long PrivateBytes;
            public long WorkingSetBytes;
        }

        private sealed class GrowthTracker
        {
            private readonly Queue<GrowthSample> _samples = new Queue<GrowthSample>(LeakDetectorWindow);

            public DateTime? FirstTimestamp { get; private set; }
            public long FirstBytes { get; private set; }
            public long MaxBytes { get; private set; }
            public DateTime? LastTimestamp { get; private set; }

            public void Add(DateTime timestamp, long bytes)
            {
                lock (this)
                {
                    _samples.Enqueue(new GrowthSample { Timestamp = timestamp, Bytes = bytes });

                    while (_samples.Count > LeakDetectorWindow)
                        _samples.Dequeue();

                    var oldest = _samples.Peek();

                    FirstTimestamp = oldest.Timestamp;
                    FirstBytes = oldest.Bytes;
                    LastTimestamp = timestamp;

                    if (bytes > MaxBytes)
                        MaxBytes = bytes;
                }
            }

            public void Reset()
            {
                lock (this)
                {
                    _samples.Clear();
                    FirstTimestamp = null;
                    FirstBytes = 0;
                    MaxBytes = 0;
                }
            }
        }

        private struct GrowthSample
        {
            public DateTime Timestamp;
            public long Bytes;
        }

        #endregion
    }
}
