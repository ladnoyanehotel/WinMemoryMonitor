using System;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Evaluates real memory pressure from a sliding window of samples (default 10 seconds).
    /// Never reacts to a single short spike: CRITICAL requires sustained low available RAM
    /// combined with a high commit charge. Hard page faults are used as supporting evidence only.
    /// </summary>
    public class MemoryPressureService : IMemoryPressureService, IDisposable
    {
        #region Fields

        private readonly IMonitoringService _monitoringService;
        private readonly int _windowSize;
        private readonly long[] _availableBytes;
        private readonly double[] _commitPercent;
        private readonly double[] _hardFaults;
        private int _sampleCount;
        private int _head = -1;

        private int _warningConditionSeconds;
        private int _criticalConditionSeconds;
        private Enums.Memory.Pressure _level = Enums.Memory.Pressure.Normal;

        private readonly object _sync = new object();
        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryPressureService"/> class.
        /// </summary>
        /// <param name="monitoringService">Monitoring service providing snapshots</param>
        public MemoryPressureService(IMonitoringService monitoringService) : this(monitoringService, 10)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryPressureService"/> class.
        /// </summary>
        /// <param name="monitoringService">Monitoring service</param>
        /// <param name="windowSizeSeconds">Sliding window size in seconds</param>
        internal MemoryPressureService(IMonitoringService monitoringService, int windowSizeSeconds)
        {
            if (monitoringService == null)
                throw new ArgumentNullException("monitoringService");

            _monitoringService = monitoringService;
            _windowSize = Math.Max(1, windowSizeSeconds);
            _availableBytes = new long[_windowSize];
            _commitPercent = new double[_windowSize];
            _hardFaults = new double[_windowSize];

            _monitoringService.OnSnapshot += OnSnapshot;
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs when the pressure level changes
        /// </summary>
        public event Action<Enums.Memory.Pressure> OnLevelChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the current memory pressure level
        /// </summary>
        public Enums.Memory.Pressure Level
        {
            get
            {
                lock (_sync)
                {
                    return _level;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether hard page faults are elevated in the current window (supporting evidence)
        /// </summary>
        public bool IsHardFaultsElevated
        {
            get
            {
                lock (_sync)
                {
                    if (_sampleCount < _windowSize)
                        return false;

                    var sum = 0.0;

                    for (var i = 0; i < _windowSize; i++)
                        sum += _hardFaults[i];

                    return sum / _windowSize > 50.0; // Pages input/sec averaged over the window
                }
            }
        }

        /// <summary>
        /// Gets the seconds the critical condition has been continuously sustained
        /// </summary>
        public int CriticalConditionSeconds
        {
            get
            {
                lock (_sync)
                {
                    return _criticalConditionSeconds;
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
                var criticalThresholdBytes = Math.Max(1024L * 1024 * 1024, (long)(0.08 * snapshot.RamTotalBytes)); // max(1 GB, 8% of total)
                var availableIsCritical = snapshot.RamAvailableBytes < criticalThresholdBytes && snapshot.CommitPercent >= 90.0;
                var warningCondition = snapshot.RamAvailablePercent < 15.0 || snapshot.CommitPercent >= 85.0;

                Enums.Memory.Pressure newLevel;

                lock (_sync)
                {
                    if (_disposed)
                        return;

                    _head = (_head + 1) % _windowSize;
                    _availableBytes[_head] = snapshot.RamAvailableBytes;
                    _commitPercent[_head] = snapshot.CommitPercent;
                    _hardFaults[_head] = snapshot.HardFaultsValid ? snapshot.HardFaultsPerSecond : 0;

                    if (_sampleCount < _windowSize)
                        _sampleCount++;

                    _criticalConditionSeconds = availableIsCritical ? _criticalConditionSeconds + 1 : 0;
                    _warningConditionSeconds = warningCondition ? _warningConditionSeconds + 1 : 0;

                    // Sustained conditions only: 10 seconds at or above the threshold
                    if (_criticalConditionSeconds >= _windowSize)
                        newLevel = Enums.Memory.Pressure.Critical;
                    else if (_warningConditionSeconds >= _windowSize)
                        newLevel = Enums.Memory.Pressure.Warning;
                    else
                        newLevel = Enums.Memory.Pressure.Normal;

                    if (newLevel == _level)
                        return;

                    _level = newLevel;
                }

                var handler = OnLevelChanged;

                if (handler != null)
                    handler(newLevel);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
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

            lock (_sync)
            {
                _disposed = true;
            }

            _monitoringService.OnSnapshot -= OnSnapshot;
        }

        #endregion
    }
}
