using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Central performance monitoring service. Samples cheap Windows APIs on a single scheduler:
    /// core metrics (CPU, RAM, commit, PDH counters) every second, GPU values consumed at a slower cadence.
    /// No WMI queries on hot paths.
    /// </summary>
    public class MonitoringService : IMonitoringService, IDisposable
    {
        #region Fields

        private readonly SamplingScheduler _scheduler;

        private IntPtr _cpuBuffer = IntPtr.Zero;
        private int _cpuBufferSize;
        private long _lastCpuIdleTime;
        private long _lastCpuTotalTime;
        private bool _cpuValid;

        private IntPtr _pdhQuery = IntPtr.Zero;
        private IntPtr _diskActiveTimeCounter = IntPtr.Zero;
        private IntPtr _diskLatencyCounter = IntPtr.Zero;
        private IntPtr _hardFaultsCounter = IntPtr.Zero;
        private IntPtr _gpuUtilizationCounter = IntPtr.Zero;
        private IntPtr _gpuMemoryCounter = IntPtr.Zero;
        private IntPtr _pdhItemBuffer = IntPtr.Zero;
        private uint _pdhItemBufferSize;
        private bool _pdhDiskValid = true;
        private bool _pdhGpuValid = true;

        private readonly SystemMetricsSnapshot _latest = new SystemMetricsSnapshot();
        private readonly object _sync = new object();

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="MonitoringService"/> class.
        /// </summary>
        /// <param name="scheduler">Shared sampling scheduler</param>
        public MonitoringService(SamplingScheduler scheduler)
        {
            _scheduler = scheduler;
            _latest.VramTotalBytes = GetVramTotalBytes();

            _scheduler.Register(SampleCore, 1000);
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs after every core sample (about once per second).
        /// Raised on the scheduler thread; subscribers must marshal to the UI thread themselves.
        /// </summary>
        public event Action<SystemMetricsSnapshot> OnSnapshot;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the metric histories (60 samples each) for sparklines
        /// </summary>
        public MetricHistory CpuHistory { get { return _cpuHistoryField; } }
        public MetricHistory RamHistory { get { return _ramHistoryField; } }
        public MetricHistory GpuHistory { get { return _gpuHistoryField; } }
        public MetricHistory DiskHistory { get { return _diskHistoryField; } }

        private readonly MetricHistory _cpuHistoryField = new MetricHistory(60);
        private readonly MetricHistory _ramHistoryField = new MetricHistory(60);
        private readonly MetricHistory _gpuHistoryField = new MetricHistory(60);
        private readonly MetricHistory _diskHistoryField = new MetricHistory(60);

        /// <summary>
        /// Gets the latest snapshot (thread-safe copy of the reference)
        /// </summary>
        public SystemMetricsSnapshot Latest
        {
            get
            {
                lock (_sync)
                {
                    return _latest;
                }
            }
        }

        #endregion

        #region Methods

        private static long GetVramTotalBytes()
        {
            // Read dedicated video memory size from the display adapter class key. Cached once at startup.
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (key == null)
                        return 0;

                    long total = 0;

                    foreach (var name in key.GetSubKeyNames())
                    {
                        if (name.Length != 4 || !name.StartsWith("000", StringComparison.Ordinal))
                            continue;

                        using (var subKey = key.OpenSubKey(name))
                        {
                            if (subKey == null)
                                continue;

                            var value = subKey.GetValue("HardwareInformation.qwMemorySize") as long?;

                            if (value.HasValue && value.Value > 0)
                                total = Math.Max(total, value.Value);
                        }
                    }

                    return total;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return 0;
            }
        }

        private void SampleCore()
        {
            var snapshot = new SystemMetricsSnapshot { VramTotalBytes = _latest.VramTotalBytes };

            SampleMemory(snapshot);
            SampleCpu(snapshot);
            CollectPdh(snapshot);

            snapshot.RamUsedBytes = Math.Max(0, snapshot.RamTotalBytes - snapshot.RamAvailableBytes);

            lock (_sync)
            {
                CopyToLatest(snapshot);
            }

            _cpuHistoryField.Add(snapshot.CpuValid ? (float)Math.Round(snapshot.CpuUsage, 1) : 0f);
            _ramHistoryField.Add((float)snapshot.RamUsagePercent);
            _gpuHistoryField.Add(snapshot.GpuValid ? (float)Math.Round(snapshot.GpuUsagePercent, 1) : 0f);
            _diskHistoryField.Add(snapshot.DiskValid ? (float)Math.Round(snapshot.DiskActiveTimePercent, 1) : 0f);

            var handler = OnSnapshot;

            if (handler != null)
                handler(Latest);
        }

        private void CopyToLatest(SystemMetricsSnapshot snapshot)
        {
            _latest.Timestamp = snapshot.Timestamp;
            _latest.CpuValid = snapshot.CpuValid;
            _latest.CpuUsage = snapshot.CpuUsage;
            _latest.RamTotalBytes = snapshot.RamTotalBytes;
            _latest.RamAvailableBytes = snapshot.RamAvailableBytes;
            _latest.RamUsedBytes = snapshot.RamUsedBytes;
            _latest.MemoryLoad = snapshot.MemoryLoad;
            _latest.CommitTotalBytes = snapshot.CommitTotalBytes;
            _latest.CommitLimitBytes = snapshot.CommitLimitBytes;
            _latest.HardFaultsValid = snapshot.HardFaultsValid;
            _latest.HardFaultsPerSecond = snapshot.HardFaultsPerSecond;
            _latest.DiskValid = snapshot.DiskValid;
            _latest.DiskActiveTimePercent = snapshot.DiskActiveTimePercent;
            _latest.DiskLatencyMs = snapshot.DiskLatencyMs;
            _latest.GpuValid = snapshot.GpuValid;
            _latest.GpuUsagePercent = snapshot.GpuUsagePercent;
            _latest.VramUsedBytes = snapshot.VramUsedBytes;
            _latest.VramTotalBytes = snapshot.VramTotalBytes;
            _latest.HandleCount = snapshot.HandleCount;
            _latest.ProcessCount = snapshot.ProcessCount;
            _latest.ThreadCount = snapshot.ThreadCount;
        }

        private void SampleMemory(SystemMetricsSnapshot snapshot)
        {
            try
            {
                var memoryStatusEx = new Structs.Windows.MemoryStatusEx();

                if (NativeMethods.GlobalMemoryStatusEx(memoryStatusEx))
                {
                    snapshot.RamTotalBytes = memoryStatusEx.TotalPhys;
                    snapshot.RamAvailableBytes = memoryStatusEx.AvailPhys;
                    snapshot.MemoryLoad = memoryStatusEx.MemoryLoad;
                }

                var performanceInfo = new Structs.Windows.PerformanceInformation();

                if (NativeMethods.GetPerformanceInfo(performanceInfo, performanceInfo.Length))
                {
                    var pageSize = performanceInfo.PageSize.ToInt64();

                    snapshot.CommitTotalBytes = performanceInfo.CommitTotal.ToInt64() * pageSize;
                    snapshot.CommitLimitBytes = performanceInfo.CommitLimit.ToInt64() * pageSize;
                    snapshot.HandleCount = performanceInfo.HandleCount;
                    snapshot.ProcessCount = performanceInfo.ProcessCount;
                    snapshot.ThreadCount = performanceInfo.ThreadCount;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void SampleCpu(SystemMetricsSnapshot snapshot)
        {
            try
            {
                var structureSize = Marshal.SizeOf(typeof(Structs.Windows.SystemProcessorPerformanceInformation));

                if (_cpuBufferSize == 0)
                {
                    _cpuBufferSize = (Environment.ProcessorCount + 8) * structureSize;
                    _cpuBuffer = Marshal.AllocHGlobal(_cpuBufferSize);
                }

                uint returnLength;
                var status = NativeMethods.NtQuerySystemInformation(Constants.Windows.SystemInformationClass.SystemProcessorPerformanceInformation, _cpuBuffer, (uint)_cpuBufferSize, out returnLength);

                if (status != 0)
                {
                    snapshot.CpuValid = _cpuValid;
                    snapshot.CpuUsage = _latest.CpuUsage;
                    return;
                }

                var structureCount = (int)returnLength / structureSize;

                if (structureCount <= 0)
                    structureCount = Environment.ProcessorCount;

                var idleTime = 0L;
                var totalTime = 0L;

                for (var i = 0; i < structureCount; i++)
                {
                    var information = (Structs.Windows.SystemProcessorPerformanceInformation)Marshal.PtrToStructure(new IntPtr(_cpuBuffer.ToInt64() + i * structureSize), typeof(Structs.Windows.SystemProcessorPerformanceInformation));

                    idleTime += information.IdleTime;
                    totalTime += information.KernelTime + information.UserTime;
                }

                var deltaIdle = idleTime - _lastCpuIdleTime;
                var deltaTotal = totalTime - _lastCpuTotalTime;

                _lastCpuIdleTime = idleTime;
                _lastCpuTotalTime = totalTime;

                if (!_cpuValid || deltaTotal <= 0)
                {
                    _cpuValid = true;
                    snapshot.CpuValid = false; // First sample only provides a baseline
                    snapshot.CpuUsage = 0;
                    return;
                }

                var usage = 100.0 * (deltaTotal - deltaIdle) / deltaTotal;

                if (usage < 0)
                    usage = 0;

                if (usage > 100)
                    usage = 100;

                snapshot.CpuValid = true;
                snapshot.CpuUsage = Math.Round(usage, 1);
            }
            catch (Exception e)
            {
                Logger.Error(e);
                snapshot.CpuValid = false;
            }
        }

        private void CollectPdh(SystemMetricsSnapshot snapshot)
        {
            // Disk / hard faults / GPU counters share one PDH query; counters that do not exist on this
            // Windows version are permanently disabled instead of retried.
            try
            {
                if (_pdhQuery == IntPtr.Zero)
                {
                    var status = NativeMethods.PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _pdhQuery);

                    if (status != Constants.Windows.PerformanceDataHelper.ErrorSuccess)
                    {
                        _pdhQuery = IntPtr.Zero;
                        _pdhDiskValid = false;
                        _pdhGpuValid = false;
                    }
                    else
                    {
                        _diskActiveTimeCounter = AddCounter(Constants.Windows.PerformanceDataHelper.CounterPath.DiskActiveTime);
                        _diskLatencyCounter = AddCounter(Constants.Windows.PerformanceDataHelper.CounterPath.DiskLatency);
                        _hardFaultsCounter = AddCounter(Constants.Windows.PerformanceDataHelper.CounterPath.HardFaults);
                        _gpuUtilizationCounter = AddCounter(Constants.Windows.PerformanceDataHelper.CounterPath.GpuUtilization);
                        _gpuMemoryCounter = AddCounter(Constants.Windows.PerformanceDataHelper.CounterPath.GpuLocalMemory);

                        _pdhDiskValid = _diskActiveTimeCounter != IntPtr.Zero && _diskLatencyCounter != IntPtr.Zero;
                        _pdhGpuValid = _gpuUtilizationCounter != IntPtr.Zero;
                        snapshot.HardFaultsValid = _hardFaultsCounter != IntPtr.Zero;
                    }
                }

                if (_pdhQuery == IntPtr.Zero)
                {
                    snapshot.DiskValid = false;
                    snapshot.GpuValid = false;
                    snapshot.HardFaultsValid = false;
                    return;
                }

                var collectStatus = NativeMethods.PdhCollectQueryData(_pdhQuery);

                if (collectStatus == Constants.Windows.PerformanceDataHelper.InvalidData || collectStatus == Constants.Windows.PerformanceDataHelper.NoData)
                {
                    // Rate counters need two samples before they produce values
                    snapshot.DiskValid = false;
                    snapshot.GpuValid = false;
                    snapshot.HardFaultsValid = false;
                    return;
                }

                if (collectStatus != Constants.Windows.PerformanceDataHelper.ErrorSuccess)
                {
                    snapshot.DiskValid = false;
                    snapshot.GpuValid = false;
                    snapshot.HardFaultsValid = false;
                    return;
                }

                if (_hardFaultsCounter != IntPtr.Zero)
                {
                    double hardFaults;
                    snapshot.HardFaultsValid = TryGetSingleValue(_hardFaultsCounter, out hardFaults);
                    snapshot.HardFaultsPerSecond = snapshot.HardFaultsValid ? Math.Round(hardFaults, 1) : 0;
                }

                if (_pdhDiskValid)
                {
                    double activeTime;
                    double latency;

                    if (TryGetSingleValue(_diskActiveTimeCounter, out activeTime) && TryGetSingleValue(_diskLatencyCounter, out latency))
                    {
                        snapshot.DiskValid = true;
                        snapshot.DiskActiveTimePercent = activeTime < 0 ? 0 : (activeTime > 100 ? 100 : Math.Round(activeTime, 1));
                        snapshot.DiskLatencyMs = Math.Round(latency * 1000.0, 1); // sec/transfer -> ms
                    }
                }

                if (_pdhGpuValid)
                {
                    double gpuUsage;
                    double vramUsed = 0;

                    var usageOk = TryGetSummedValue(_gpuUtilizationCounter, out gpuUsage);
                    var memoryOk = _gpuMemoryCounter != IntPtr.Zero && TryGetSummedValue(_gpuMemoryCounter, out vramUsed);

                    if (usageOk)
                    {
                        snapshot.GpuValid = true;
                        snapshot.GpuUsagePercent = gpuUsage < 0 ? 0 : Math.Round(gpuUsage, 1);
                    }

                    if (memoryOk)
                        snapshot.VramUsedBytes = (long)vramUsed;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
                snapshot.DiskValid = false;
                snapshot.GpuValid = false;
                snapshot.HardFaultsValid = false;
            }
        }

        private IntPtr AddCounter(string path)
        {
            IntPtr counter;

            var status = NativeMethods.PdhAddEnglishCounterW(_pdhQuery, path, IntPtr.Zero, out counter);

            if (status != Constants.Windows.PerformanceDataHelper.ErrorSuccess)
                return IntPtr.Zero;

            return counter;
        }

        private bool TryGetSingleValue(IntPtr counter, out double value)
        {
            value = 0;

            try
            {
                IntPtr counterType;
                var counterValue = default(Structs.Windows.PdhCounterValue);
                var status = NativeMethods.PdhGetFormattedCounterValue(counter, Constants.Windows.PerformanceDataHelper.FormatDouble, out counterType, ref counterValue);

                if (status != Constants.Windows.PerformanceDataHelper.ErrorSuccess)
                    return false;

                value = counterValue.DoubleValue;
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return false;
            }
        }

        private bool TryGetSummedValue(IntPtr counter, out double value)
        {
            // Wildcard counters return one item per instance; the total is the sum of all instances
            value = 0;

            try
            {
                var bufferSize = 0u;
                var itemCount = 0u;

                var status = NativeMethods.PdhGetFormattedCounterArrayW(counter, Constants.Windows.PerformanceDataHelper.FormatDouble, ref bufferSize, out itemCount, IntPtr.Zero);

                if (status == Constants.Windows.PerformanceDataHelper.ErrorSuccess)
                    return false;

                if (status != Constants.Windows.PerformanceDataHelper.MoreData || bufferSize == 0)
                    return false;

                if (_pdhItemBuffer == IntPtr.Zero || _pdhItemBufferSize < bufferSize)
                {
                    if (_pdhItemBuffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(_pdhItemBuffer);

                    _pdhItemBufferSize = bufferSize + 1024; // Headroom for instance names
                    _pdhItemBuffer = Marshal.AllocHGlobal((int)_pdhItemBufferSize);
                }

                status = NativeMethods.PdhGetFormattedCounterArrayW(counter, Constants.Windows.PerformanceDataHelper.FormatDouble, ref bufferSize, out itemCount, _pdhItemBuffer);

                if (status != Constants.Windows.PerformanceDataHelper.ErrorSuccess || itemCount == 0)
                    return false;

                var itemSize = Marshal.SizeOf(typeof(Structs.Windows.PdhCounterValueItem));
                var sum = 0.0;
                var valid = false;

                for (var i = 0; i < itemCount; i++)
                {
                    var item = (Structs.Windows.PdhCounterValueItem)Marshal.PtrToStructure(new IntPtr(_pdhItemBuffer.ToInt64() + i * itemSize), typeof(Structs.Windows.PdhCounterValueItem));
                    sum += item.Value.DoubleValue;
                    valid = true;
                }

                value = sum;
                return valid;
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return false;
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Releases PDH handles and unmanaged buffers.
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

            try
            {
                if (_gpuUtilizationCounter != IntPtr.Zero)
                    NativeMethods.PdhRemoveCounter(_gpuUtilizationCounter);

                if (_gpuMemoryCounter != IntPtr.Zero)
                    NativeMethods.PdhRemoveCounter(_gpuMemoryCounter);

                if (_diskActiveTimeCounter != IntPtr.Zero)
                    NativeMethods.PdhRemoveCounter(_diskActiveTimeCounter);

                if (_diskLatencyCounter != IntPtr.Zero)
                    NativeMethods.PdhRemoveCounter(_diskLatencyCounter);

                if (_hardFaultsCounter != IntPtr.Zero)
                    NativeMethods.PdhRemoveCounter(_hardFaultsCounter);

                if (_pdhQuery != IntPtr.Zero)
                    NativeMethods.PdhCloseQuery(_pdhQuery);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                _gpuUtilizationCounter = IntPtr.Zero;
                _gpuMemoryCounter = IntPtr.Zero;
                _diskActiveTimeCounter = IntPtr.Zero;
                _diskLatencyCounter = IntPtr.Zero;
                _hardFaultsCounter = IntPtr.Zero;
                _pdhQuery = IntPtr.Zero;
            }

            if (_pdhItemBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_pdhItemBuffer);
                _pdhItemBuffer = IntPtr.Zero;
                _pdhItemBufferSize = 0;
            }

            if (_cpuBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_cpuBuffer);
                _cpuBuffer = IntPtr.Zero;
                _cpuBufferSize = 0;
            }
        }

        #endregion
    }
}
