using System;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Combines CPU, RAM, commit, disk and top-process data into a single user-facing status
    /// with a machine-readable reason. Thresholds require sustained conditions (no single-spike reactions):
    /// CPU WARNING &gt; 85% for 10 s, CRITICAL &gt; 95% for 30 s; disk WARNING &gt; 70% active time with
    /// &gt; 20 ms latency, CRITICAL &gt; 90% with &gt; 50 ms.
    /// </summary>
    public class SystemStatusEvaluator : ISystemStatusEvaluator, IDisposable
    {
        #region Fields

        private const int CpuWarningSustainSeconds = 10;
        private const int CpuCriticalSustainSeconds = 30;
        private const int DiskSustainSeconds = 5;

        private readonly IMonitoringService _monitoringService;
        private readonly IProcessMonitorService _processMonitorService;
        private readonly IMemoryPressureService _memoryPressureService;

        private readonly object _sync = new object();
        private int _cpuWarningSeconds;
        private int _cpuCriticalSeconds;
        private int _diskWarningSeconds;
        private int _diskCriticalSeconds;
        private SystemStatus _current = SystemStatus.Normal();
        private bool _isOptimizing;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemStatusEvaluator"/> class.
        /// </summary>
        /// <param name="monitoringService">Monitoring service</param>
        /// <param name="processMonitorService">Process monitor service</param>
        /// <param name="memoryPressureService">Memory pressure service</param>
        public SystemStatusEvaluator(IMonitoringService monitoringService, IProcessMonitorService processMonitorService, IMemoryPressureService memoryPressureService)
        {
            _monitoringService = monitoringService;
            _processMonitorService = processMonitorService;
            _memoryPressureService = memoryPressureService;

            _monitoringService.OnSnapshot += OnSnapshot;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the current evaluated status
        /// </summary>
        public SystemStatus Current
        {
            get
            {
                lock (_sync)
                {
                    return _current;
                }
            }
        }

        /// <summary>
        /// Sets or clears the optimizing override shown while a cleanup is running
        /// </summary>
        public bool IsOptimizing
        {
            set
            {
                lock (_sync)
                {
                    _isOptimizing = value;
                }
            }
        }

        #endregion

        #region Methods

        private void OnSnapshot(SystemMetricsSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            try
            {
                var status = EvaluateInternal(snapshot);

                lock (_sync)
                {
                    _current = status;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        /// <summary>
        /// Evaluates the system status from the given snapshot and tracked sustain counters
        /// </summary>
        /// <param name="snapshot">Latest metrics snapshot</param>
        /// <param name="pressureLevel">Memory pressure level; when null, pressure is not evaluated</param>
        public SystemStatus Evaluate(SystemMetricsSnapshot snapshot, Enums.Memory.Pressure? pressureLevel)
        {
            if (snapshot == null)
                return SystemStatus.Normal();

            var cpu = snapshot.CpuValid ? snapshot.CpuUsage : 0.0;
            var cpuHigh = cpu > 85.0;
            var cpuCritical = cpu > 95.0;

            var diskHigh = snapshot.DiskValid && snapshot.DiskActiveTimePercent > 70.0 && snapshot.DiskLatencyMs > 20.0;
            var diskCritical = snapshot.DiskValid && snapshot.DiskActiveTimePercent > 90.0 && snapshot.DiskLatencyMs > 50.0;

            _cpuCriticalSeconds = cpuCritical ? _cpuCriticalSeconds + 1 : 0;
            _cpuWarningSeconds = cpuHigh ? _cpuWarningSeconds + 1 : 0;
            _diskCriticalSeconds = diskCritical ? _diskCriticalSeconds + 1 : 0;
            _diskWarningSeconds = diskHigh ? _diskWarningSeconds + 1 : 0;

            var topCpu = GetTopProcess(true);
            var topMemory = GetTopProcess(false);

            lock (_sync)
            {
                if (_isOptimizing)
                    return SystemStatus.Optimizing();
            }

            // Priority: RAM/commit pressure > CPU > Disk > memory warning > generic elevated load
            if (pressureLevel == Enums.Memory.Pressure.Critical)
            {
                return new SystemStatus
                {
                    Level = Enums.Status.Level.Critical,
                    Reason = Enums.Status.Reason.MemoryPressure,
                    ProcessName = topMemory != null ? topMemory.Name : string.Empty,
                    ContextValue = RoundGb(topMemory != null ? topMemory.PrivateBytes : 0)
                };
            }

            if (_cpuCriticalSeconds >= CpuCriticalSustainSeconds)
            {
                return new SystemStatus
                {
                    Level = Enums.Status.Level.Critical,
                    Reason = Enums.Status.Reason.CpuLoad,
                    ProcessName = topCpu != null ? topCpu.Name : string.Empty,
                    ContextValue = Math.Round(cpu, 0)
                };
            }

            if (_diskCriticalSeconds >= DiskSustainSeconds)
            {
                return new SystemStatus
                {
                    Level = Enums.Status.Level.Critical,
                    Reason = Enums.Status.Reason.DiskLoad,
                    ContextValue = Math.Round(snapshot.DiskLatencyMs, 0)
                };
            }

            if (pressureLevel == Enums.Memory.Pressure.Warning)
            {
                return new SystemStatus
                {
                    Level = Enums.Status.Level.Warning,
                    Reason = Enums.Status.Reason.MemoryLow,
                    ProcessName = topMemory != null ? topMemory.Name : string.Empty,
                    ContextValue = RoundGb(topMemory != null ? topMemory.PrivateBytes : 0)
                };
            }

            if (_cpuWarningSeconds >= CpuWarningSustainSeconds)
            {
                return new SystemStatus
                {
                    Level = Enums.Status.Level.Warning,
                    Reason = Enums.Status.Reason.CpuLoad,
                    ProcessName = topCpu != null ? topCpu.Name : string.Empty,
                    ContextValue = Math.Round(cpu, 0)
                };
            }

            if (_diskWarningSeconds >= DiskSustainSeconds)
            {
                return new SystemStatus
                {
                    Level = Enums.Status.Level.Warning,
                    Reason = Enums.Status.Reason.DiskLoad,
                    ContextValue = Math.Round(snapshot.DiskLatencyMs, 0)
                };
            }

            return new SystemStatus
            {
                Level = Enums.Status.Level.Normal,
                Reason = cpu > 60.0 || snapshot.DiskActiveTimePercent > 60.0 ? Enums.Status.Reason.ElevatedLoad : Enums.Status.Reason.SystemNormal,
                ContextValue = Math.Round(cpu, 0)
            };
        }

        private SystemStatus EvaluateInternal(SystemMetricsSnapshot snapshot)
        {
            var pressure = _memoryPressureService != null ? (Enums.Memory.Pressure?)_memoryPressureService.Level : null;
            return Evaluate(snapshot, pressure);
        }

        private static double RoundGb(long bytes)
        {
            return Math.Round(bytes / 1073741824.0, 1);
        }

        private ProcessStats GetTopProcess(bool byCpu)
        {
            ProcessStatsSnapshot processSnapshot = null;

            if (_processMonitorService != null)
                processSnapshot = _processMonitorService.LastSnapshot;

            if (processSnapshot == null || !processSnapshot.Valid)
                return null;

            var list = byCpu ? processSnapshot.TopByCpu : processSnapshot.TopByMemory;

            if (list == null || list.Count == 0)
                return null;

            var top = list[0];

            if (byCpu && top.CpuPercent < 1.0)
                return null;

            if (!byCpu && top.PrivateBytes < 100L * 1024 * 1024)
                return null;

            return top;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Unsubscribes from the monitoring service.
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

            _monitoringService.OnSnapshot -= OnSnapshot;
        }

        #endregion
    }
}
