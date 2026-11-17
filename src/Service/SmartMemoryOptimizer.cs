using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Smart memory optimizer. Decides whether a cleanup is necessary, performs it through the
    /// isolated worker process (level 1: low priority standby list; level 2: standby list, only when
    /// pressure remains critical), verifies the result and applies cooldowns. At most one cleanup
    /// runs at a time: entry is guarded by an atomic gate, repeated calls are ignored instead of queued.
    ///
    /// Never performs global working-set trims, registry/system file cache purges or modified/combined
    /// page list purges: those cause page faults and disk IO that hurt real performance.
    /// </summary>
    public class SmartMemoryOptimizer : ISmartMemoryOptimizer, IDisposable
    {
        #region Fields

        private const int AutoCooldownMs = 10 * 60 * 1000;               // 10 minutes
        private const long MinUsefulFreedBytes = 256L * 1024 * 1024;     // 256 MB
        private const int RecheckDelayMs = 1000;                         // Wait for the next 1-second sample cadence
        private const int Verification5SecondsMs = 5000;

        private readonly IWorkerProcessRunner _worker;
        private readonly IMonitoringService _monitoringService;
        private readonly IMemoryPressureService _memoryPressureService;
        private readonly IProcessMonitorService _processMonitorService;
        private readonly SamplingScheduler _scheduler;

        private readonly object _sync = new object();
        private int _optimizationGate;                                    // 0 = free, 1 = running; atomic
        private DateTime _lastAutoCleanupUtc = DateTime.MinValue;
        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SmartMemoryOptimizer"/> class.
        /// </summary>
        /// <param name="worker">Worker process runner</param>
        /// <param name="monitoringService">Monitoring service</param>
        /// <param name="memoryPressureService">Memory pressure service</param>
        /// <param name="processMonitorService">Process monitor service</param>
        /// <param name="scheduler">Shared sampling scheduler used for delayed verification</param>
        public SmartMemoryOptimizer(IWorkerProcessRunner worker, IMonitoringService monitoringService, IMemoryPressureService memoryPressureService, IProcessMonitorService processMonitorService, SamplingScheduler scheduler)
        {
            _worker = worker;
            _monitoringService = monitoringService;
            _memoryPressureService = memoryPressureService;
            _processMonitorService = processMonitorService;
            _scheduler = scheduler;

            // Automatic cleanup only on sustained CRITICAL memory pressure, with cooldown
            _memoryPressureService.OnLevelChanged += OnPressureLevelChanged;
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs when a cleanup finishes (any outcome). Raised on a background thread.
        /// </summary>
        public event Action<SmartCleanupResult> OnCompleted;

        /// <summary>
        /// Occurs when a cleanup starts. Raised on a background thread.
        /// </summary>
        public event Action IsRunningChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether a cleanup is currently running
        /// </summary>
        public bool IsRunning
        {
            get { return Thread.VolatileRead(ref _optimizationGate) == 1; }
        }

        /// <summary>
        /// Test hook: forces the "cleanup needed" decision so the full worker path can be
        /// exercised deterministically regardless of the real machine state.
        /// </summary>
        internal bool ForceCleanupRequired { get; set; }

        #endregion

        #region Methods

        private void OnPressureLevelChanged(Enums.Memory.Pressure level)
        {
            if (level != Enums.Memory.Pressure.Critical)
                return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    AutoCleanup();
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Automatic cleanup failed");
                }
            });
        }

        private void AutoCleanup()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                if (DateTime.UtcNow - _lastAutoCleanupUtc < TimeSpan.FromMilliseconds(AutoCooldownMs))
                {
                    Logger.Information("Automatic cleanup skipped: cooldown active");
                    return;
                }

                _lastAutoCleanupUtc = DateTime.UtcNow;
            }

            Logger.Information("Automatic cleanup: critical memory pressure detected");

            var result = Cleanup(Enums.Memory.Optimization.Reason.LowMemory);

            if (result != null && result.Outcome == CleanupOutcome.Ineffective)
                Logger.Information("Automatic cleanup was ineffective; high memory usage is caused by active applications. Cooldown stays active.");
        }

        /// <summary>
        /// Runs the smart cleanup. Must be called from a background thread. Thread-safe:
        /// returns null immediately when another cleanup is already running (no queueing).
        /// </summary>
        /// <param name="reason">Cleanup reason (manual or automatic)</param>
        public SmartCleanupResult Cleanup(Enums.Memory.Optimization.Reason reason)
        {
            // Atomic optimization gate: exactly one cleanup at a time, repeated calls are ignored
            if (Interlocked.CompareExchange(ref _optimizationGate, 1, 0) != 0)
            {
                Logger.Information("Cleanup requested while another cleanup is running; ignored");
                return null;
            }

            var handler = IsRunningChanged;

            if (handler != null)
                handler();

            try
            {
                return RunCleanup(reason);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Smart cleanup failed");

                return Complete(new SmartCleanupResult
                {
                    Reason = reason,
                    Outcome = CleanupOutcome.Failed,
                    Message = e.Message,
                    TopProcessName = GetTopMemoryProcessName(),
                    TopProcessPrivateGb = GetTopMemoryProcessGb()
                });
            }
            finally
            {
                Interlocked.Exchange(ref _optimizationGate, 0);

                handler = IsRunningChanged;

                if (handler != null)
                    handler();
            }
        }

        private SmartCleanupResult RunCleanup(Enums.Memory.Optimization.Reason reason)
        {
            var stopwatch = Stopwatch.StartNew();
            var before = MeasureMemory();

            Logger.Information(string.Format("Smart cleanup ({0}): available {1:F0} MB, commit {2:F1}%", reason, before.AvailableBytes / 1048576.0, before.CommitPercent));

            var topName = GetTopMemoryProcessName();
            var topGb = GetTopMemoryProcessGb();

            // Analyze first: when RAM is fine there is nothing to do
            if (!NeedsCleanup(before))
            {
                Logger.Information("Smart cleanup: not needed");

                return Complete(new SmartCleanupResult
                {
                    Reason = reason,
                    Outcome = CleanupOutcome.NotNeeded,
                    AvailableBeforeBytes = before.AvailableBytes,
                    Message = "RAM is working normally; cleanup is not required.",
                    TopProcessName = topName,
                    TopProcessPrivateGb = topGb
                });
            }

            // Level 1: clear the low priority standby list
            var level1 = _worker.Run(WorkerOperation.LowPriorityStandby);

            if (level1.TimedOut)
                return Complete(TimeoutResult(reason, before, topName, topGb, stopwatch));

            if (!level1.Success)
                return Complete(FailedResult(reason, before, topName, topGb, "Low priority standby list cleanup failed: " + level1.Message));

            var mid = MeasureMemory();
            var freed = mid.AvailableBytes - before.AvailableBytes;
            var usedLevel2 = false;

            Logger.Information(string.Format("Level 1 done in {0} ms: available {1:F0} MB (freed {2:F0} MB)", level1.Duration.TotalMilliseconds, mid.AvailableBytes / 1048576.0, freed / 1048576.0));

            // Level 2: full standby list, only if pressure is still critical after level 1
            if (IsCritical(mid) && freed < MinUsefulFreedBytes * 4)
            {
                Thread.Sleep(RecheckDelayMs); // Let the memory manager settle before the recheck
                mid = MeasureMemory();

                if (IsCritical(mid))
                {
                    var level2 = _worker.Run(WorkerOperation.Standby);
                    usedLevel2 = true;

                    if (level2.TimedOut)
                        return Complete(TimeoutResult(reason, before, topName, topGb, stopwatch));

                    if (!level2.Success)
                        Logger.Warning("Standby list cleanup failed: " + level2.Message);

                    mid = MeasureMemory();
                    freed = mid.AvailableBytes - before.AvailableBytes;

                    Logger.Information(string.Format("Level 2 done in {0} ms: available {1:F0} MB (total freed {2:F0} MB)", level2.Duration.TotalMilliseconds, mid.AvailableBytes / 1048576.0, freed / 1048576.0));
                }
            }

            stopwatch.Stop();

            var result = new SmartCleanupResult
            {
                Reason = reason,
                AvailableBeforeBytes = before.AvailableBytes,
                AvailableAfterBytes = mid.AvailableBytes,
                FreedBytes = Math.Max(0, freed),
                Duration = stopwatch.Elapsed,
                UsedLevel2 = usedLevel2,
                TopProcessName = topName,
                TopProcessPrivateGb = topGb
            };

            if (freed < MinUsefulFreedBytes)
            {
                // Cleanup freed almost nothing: the memory is held by active applications.
                // No aggressive retries; report the main consumer instead.
                result.Outcome = CleanupOutcome.Ineffective;
                result.Message = "High memory usage is caused by active applications.";

                Logger.Information(string.Format("Cleanup freed only {0:F0} MB; reporting top consumer {1}", freed / 1048576.0, topName));
            }
            else
            {
                result.Outcome = CleanupOutcome.Completed;
                result.Message = string.Format("Freed {0:F1} GB", freed / 1073741824.0);
            }

            ScheduleVerification(result, stopwatch);

            return Complete(result);
        }

        private void ScheduleVerification(SmartCleanupResult result, Stopwatch stopwatch)
        {
            // Effectiveness check: measure again after 1 s and 5 s without holding the gate
            if (_scheduler == null)
                return; // No scheduler available (unit tests)

            _scheduler.ScheduleOnce(1000, delegate
            {
                var available = MeasureMemory().AvailableBytes;
                Logger.Information(string.Format("Verification +1s: available {0:F0} MB", available / 1048576.0));
            });

            _scheduler.ScheduleOnce(Verification5SecondsMs, delegate
            {
                var available = MeasureMemory().AvailableBytes;
                result.AvailableAfter5Seconds = available;

                Logger.Information(string.Format("Verification +5s: available {0:F0} MB (freed {1:F0} MB, still available {2:F0} MB)", available / 1048576.0, result.FreedBytes / 1048576.0, Math.Max(0, available - result.AvailableBeforeBytes) / 1048576.0));

                // If the memory was handed straight back to applications, remember it in the diagnosis:
                // further aggressive cleanups would be pointless.
                if (available - result.AvailableBeforeBytes < MinUsefulFreedBytes / 2 && result.FreedBytes >= MinUsefulFreedBytes)
                    Logger.Information("Freed memory was quickly reclaimed by applications; cleanup effectiveness was low");
            });
        }

        private SmartCleanupResult TimeoutResult(Enums.Memory.Optimization.Reason reason, MemoryMeasure before, string topName, double topGb, Stopwatch stopwatch)
        {
            stopwatch.Stop();

            Logger.Warning("Worker timed out; optimization stopped. The application keeps running normally.");

            return new SmartCleanupResult
            {
                Reason = reason,
                Outcome = CleanupOutcome.Timeout,
                AvailableBeforeBytes = before.AvailableBytes,
                Duration = stopwatch.Elapsed,
                Message = "The memory optimization did not respond within " + (WorkerProcessRunner.TimeoutMs / 1000) + " seconds. Operation stopped.",
                TopProcessName = topName,
                TopProcessPrivateGb = topGb
            };
        }

        private static SmartCleanupResult FailedResult(Enums.Memory.Optimization.Reason reason, MemoryMeasure before, string topName, double topGb, string message)
        {
            Logger.Warning("Cleanup failed: " + message);

            return new SmartCleanupResult
            {
                Reason = reason,
                Outcome = CleanupOutcome.Failed,
                AvailableBeforeBytes = before.AvailableBytes,
                Message = message,
                TopProcessName = topName,
                TopProcessPrivateGb = topGb
            };
        }

        private SmartCleanupResult Complete(SmartCleanupResult result)
        {
            var handler = OnCompleted;

            if (handler != null)
                handler(result);

            return result;
        }

        private bool NeedsCleanup(MemoryMeasure measure)
        {
            if (ForceCleanupRequired)
                return true;

            return measure.AvailablePercent < 15.0 || measure.CommitPercent >= 85.0;
        }

        private static bool IsCritical(MemoryMeasure measure)
        {
            var criticalThresholdBytes = Math.Max(1024L * 1024 * 1024, (long)(0.08 * measure.TotalBytes));
            return measure.AvailableBytes < criticalThresholdBytes && measure.CommitPercent >= 90.0;
        }

        /// <summary>
        /// Directly measures RAM and commit; independent of the sampling cadence
        /// </summary>
        private static MemoryMeasure MeasureMemory()
        {
            var measure = new MemoryMeasure();

            try
            {
                var memoryStatusEx = new Structs.Windows.MemoryStatusEx();

                if (NativeMethods.GlobalMemoryStatusEx(memoryStatusEx))
                {
                    measure.TotalBytes = memoryStatusEx.TotalPhys;
                    measure.AvailableBytes = memoryStatusEx.AvailPhys;
                }

                var performanceInfo = new Structs.Windows.PerformanceInformation();

                if (NativeMethods.GetPerformanceInfo(performanceInfo, performanceInfo.Length))
                {
                    var pageSize = performanceInfo.PageSize.ToInt64();
                    var commitTotal = performanceInfo.CommitTotal.ToInt64() * pageSize;
                    var commitLimit = performanceInfo.CommitLimit.ToInt64() * pageSize;

                    measure.CommitPercent = commitLimit > 0 ? 100.0 * commitTotal / commitLimit : 0.0;
                }

                measure.AvailablePercent = measure.TotalBytes > 0 ? 100.0 * measure.AvailableBytes / measure.TotalBytes : 100.0;
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }

            return measure;
        }

        private string GetTopMemoryProcessName()
        {
            var stats = GetTopMemoryProcess();
            return stats != null ? stats.Name : string.Empty;
        }

        private double GetTopMemoryProcessGb()
        {
            var stats = GetTopMemoryProcess();
            return stats != null ? Math.Round(stats.PrivateBytes / 1073741824.0, 1) : 0.0;
        }

        private ProcessStats GetTopMemoryProcess()
        {
            var snapshot = _processMonitorService != null ? _processMonitorService.LastSnapshot : null;

            if (snapshot == null || !snapshot.Valid || snapshot.TopByMemory.Count == 0)
                return null;

            return snapshot.TopByMemory[0];
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Unsubscribes from the pressure service.
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
                _disposed = true;
            }

            _memoryPressureService.OnLevelChanged -= OnPressureLevelChanged;
        }

        #endregion

        #region Nested types

        private sealed class MemoryMeasure
        {
            public long TotalBytes;
            public long AvailableBytes;
            public double AvailablePercent;
            public double CommitPercent;
        }

        #endregion
    }
}
