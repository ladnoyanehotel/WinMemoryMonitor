using System;
using System.Globalization;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Main View Model: a thin layer that projects monitoring snapshots onto UI properties
    /// (max one batched update per second, always marshalled through the dispatcher) and wires the
    /// Smart Cleanup and Performance Mode commands to their services. All decisions live in services.
    /// </summary>
    public class MainViewModel : ViewModel, IDisposable
    {
        #region Fields

        private readonly IMonitoringService _monitoringService;
        private readonly IProcessMonitorService _processMonitorService;
        private readonly ISystemStatusEvaluator _statusEvaluator;
        private readonly ISmartMemoryOptimizer _smartOptimizer;
        private readonly IPerformanceModeService _performanceModeService;
        private readonly IHotkeyService _hotkeyService;

        private readonly object _statusOverrideLock = new object();
        private SystemStatus _statusOverride;
        private DateTime _statusOverrideUntil;
        private int _hiddenTickCounter;

        private Hotkey _hotkey;
        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="MainViewModel"/> class.
        /// </summary>
        /// <param name="monitoringService">Monitoring service</param>
        /// <param name="processMonitorService">Process monitor service</param>
        /// <param name="statusEvaluator">System status evaluator</param>
        /// <param name="smartOptimizer">Smart memory optimizer</param>
        /// <param name="performanceModeService">Performance mode service</param>
        /// <param name="hotkeyService">Hotkey service</param>
        /// <param name="notificationService">Notification service</param>
        public MainViewModel(IMonitoringService monitoringService, IProcessMonitorService processMonitorService, ISystemStatusEvaluator statusEvaluator, ISmartMemoryOptimizer smartOptimizer, IPerformanceModeService performanceModeService, IHotkeyService hotkeyService, INotificationService notificationService)
            : base(notificationService)
        {
            _monitoringService = monitoringService;
            _processMonitorService = processMonitorService;
            _statusEvaluator = statusEvaluator;
            _smartOptimizer = smartOptimizer;
            _performanceModeService = performanceModeService;
            _hotkeyService = hotkeyService;

            // Commands
            SmartCleanupCommand = new RelayCommand(SmartCleanup, () => !IsSmartCleanupRunning);
            TogglePerformanceModeCommand = new RelayCommand(TogglePerformanceMode);
            ToggleCompactModeCommand = new RelayCommand(ToggleCompactMode);
            ToggleAlwaysOnTopCommand = new RelayCommand(ToggleAlwaysOnTop);

            // Service events (raised on background threads; marshalled to the dispatcher)
            _monitoringService.OnSnapshot += OnMonitoringSnapshot;
            _processMonitorService.OnLeakSuspected += OnLeakSuspected;
            _smartOptimizer.IsRunningChanged += OnOptimizerRunningChanged;
            _smartOptimizer.OnCompleted += OnCleanupCompleted;
            _performanceModeService.OnChanged += OnPerformanceModeChanged;

            RegisterHotkey();
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs once per second after all UI properties have been refreshed (for sparklines)
        /// </summary>
        public event Action MetricsUpdated;

        /// <summary>
        /// Occurs when a cleanup finished and CloseAfterOptimization is enabled
        /// </summary>
        public event Action OnSmartCleanupCommandCompleted;

        #endregion

        #region Properties (UI state)

        /// <summary>
        /// Gets the window title with the version
        /// </summary>
        public string Title
        {
            get { return string.Format(CultureInfo.InvariantCulture, "{0} {1}", Constants.App.DisplayName, string.Format(CultureInfo.InvariantCulture, Constants.App.VersionFormat, App.Version.Major, App.Version.Minor, App.Version.Build)); }
        }

        public Enums.Status.Level StatusLevel { get; private set; }

        public string StatusText { get; private set; }

        public string StatusDetail { get; private set; }

        // Metric cards
        public string CpuText { get; private set; }

        public string RamText { get; private set; }

        public string RamDetail { get; private set; }

        public string RamAvailableText { get; private set; }

        public string HardFaultsText { get; private set; }

        public double RamUsedPercent { get; private set; }

        public string CommitText { get; private set; }

        public string CommitDetail { get; private set; }

        public string DiskText { get; private set; }

        public string GpuText { get; private set; }

        public string VramText { get; private set; }

        public bool GpuAvailable { get; private set; }

        public string TopProcessText { get; private set; }

        public bool IsSmartCleanupRunning { get; private set; }

        /// <summary>
        /// Gets the smart cleanup button text ("smart cleanup" / "cleaning..." while running)
        /// </summary>
        public string SmartCleanupButtonText
        {
            get { return IsSmartCleanupRunning ? Localizer.String.CleanupRunning : Localizer.String.SmartCleanup; }
        }

        public bool IsPerformanceModeOn
        {
            get { return _performanceModeService.State == Enums.PerformanceMode.State.On || _performanceModeService.State == Enums.PerformanceMode.State.Limited; }
        }

        public string PerformanceModeText { get; private set; }

        public bool CompactMode
        {
            get { return Settings.CompactMode; }
            set
            {
                if (Settings.CompactMode == value)
                    return;

                Settings.CompactMode = value;
                Settings.Save();
                RaisePropertyChanged(() => CompactMode);
            }
        }

        public bool AlwaysOnTop
        {
            get { return Settings.AlwaysOnTop; }
            set
            {
                if (Settings.AlwaysOnTop == value)
                    return;

                Settings.AlwaysOnTop = value;
                Settings.Save();
                RaisePropertyChanged(() => AlwaysOnTop);
            }
        }

        // Histories for sparklines (60 samples)
        public MetricHistory CpuHistory
        {
            get { return _monitoringService.CpuHistory; }
        }

        public MetricHistory RamHistory
        {
            get { return _monitoringService.RamHistory; }
        }

        public MetricHistory GpuHistory
        {
            get { return _monitoringService.GpuHistory; }
        }

        public MetricHistory DiskHistory
        {
            get { return _monitoringService.DiskHistory; }
        }

        #endregion

        #region Commands

        public ICommand SmartCleanupCommand { get; private set; }

        public ICommand TogglePerformanceModeCommand { get; private set; }

        public ICommand ToggleCompactModeCommand { get; private set; }

        public ICommand ToggleAlwaysOnTopCommand { get; private set; }

        #endregion

        #region Methods (commands)

        private void SmartCleanup()
        {
            // The optimizer's atomic gate ignores repeated calls; the work runs on the ThreadPool
            // so the UI thread never waits and no queue of jobs is created.
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    _smartOptimizer.Cleanup(Enums.Memory.Optimization.Reason.Manual);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Smart cleanup failed");
                }
            });
        }

        private void TogglePerformanceMode()
        {
            if (IsPerformanceModeOn)
                _performanceModeService.Disable();
            else
                _performanceModeService.Enable();
        }

        private void ToggleCompactMode()
        {
            CompactMode = !CompactMode;
        }

        private void ToggleAlwaysOnTop()
        {
            AlwaysOnTop = !AlwaysOnTop;
        }

        #endregion

        #region Methods (service event handlers)

        private void OnMonitoringSnapshot(SystemMetricsSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            // Copy everything needed on the sampler thread, then marshal one batched UI update.
            // UI updates are throttled to the snapshot cadence (about 1 per second).
            try
            {
                var dispatcher = System.Windows.Application.Current != null ? System.Windows.Application.Current.Dispatcher : null;

                if (dispatcher == null)
                    return;

                var cpu = snapshot.CpuValid ? snapshot.CpuUsage : -1;
                var ramPercent = snapshot.RamUsagePercent;
                var ramUsedText = FormatBytes(snapshot.RamUsedBytes);
                var ramTotalText = FormatBytes(snapshot.RamTotalBytes);
                var ramAvailableText = FormatBytes(snapshot.RamAvailableBytes);
                var commitPercent = snapshot.CommitPercent;
                var commitTotal = FormatBytes(snapshot.CommitTotalBytes);
                var commitLimit = FormatBytes(snapshot.CommitLimitBytes);
                var diskActive = snapshot.DiskValid ? snapshot.DiskActiveTimePercent : -1;
                var diskLatency = snapshot.DiskValid ? snapshot.DiskLatencyMs : -1;
                var gpu = snapshot.GpuValid ? snapshot.GpuUsagePercent : -1;
                var vramUsed = FormatBytes(snapshot.VramUsedBytes);
                var vramTotal = snapshot.VramTotalBytes > 0 ? FormatBytes(snapshot.VramTotalBytes) : null;
                var hardFaults = snapshot.HardFaultsValid ? snapshot.HardFaultsPerSecond : -1;

                dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
                {
                    try
                    {
                        // When the window is hidden (tray only) skip the per-second UI refresh:
                        // background sampling continues, the tray is refreshed every ~5 seconds.
                        var mainWindow = System.Windows.Application.Current.MainWindow;

                        if (mainWindow == null || !mainWindow.IsVisible)
                        {
                            _hiddenTickCounter++;

                            if (_hiddenTickCounter % 5 != 0)
                                return;
                        }
                        else
                        {
                            _hiddenTickCounter = 0;
                        }

                        CpuText = cpu < 0 ? "N/A" : string.Format(Localizer.Culture, "{0:0}%", cpu);
                        RamText = string.Format(Localizer.Culture, "{0:0}%", ramPercent);
                        RamDetail = string.Format(Localizer.Culture, "{0} / {1}", ramUsedText, ramTotalText);
                        RamAvailableText = string.Format(Localizer.Culture, "{0}: {1}", Localizer.String.Available, ramAvailableText);
                        HardFaultsText = hardFaults < 0 ? "Hard faults: N/A" : string.Format(Localizer.Culture, "{0}: {1:0}/s", Localizer.String.HardFaults, hardFaults);
                        RamUsedPercent = ramPercent;

                        CommitText = string.Format(Localizer.Culture, "{0:0}%", commitPercent);
                        CommitDetail = string.Format(Localizer.Culture, "{0} / {1}", commitTotal, commitLimit);

                        DiskText = diskActive < 0 ? "N/A" : string.Format(Localizer.Culture, "{0:0}%  ·  {1:0} ms", diskActive, diskLatency);

                        GpuAvailable = gpu >= 0;
                        GpuText = gpu < 0 ? "N/A" : string.Format(Localizer.Culture, "{0:0}%", gpu);
                        VramText = vramTotal == null ? vramUsed : string.Format(Localizer.Culture, "{0}/{1}", vramUsed, vramTotal);

                        RefreshStatus();
                        RefreshTopProcess();

                        RaisePropertyChanged(string.Empty); // Batched update of all metric properties

                        var handler = MetricsUpdated;

                        if (handler != null)
                            handler();

                        UpdateTray();
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                });
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void RefreshStatus()
        {
            var status = _statusEvaluator != null ? _statusEvaluator.Current : SystemStatus.Normal();

            lock (_statusOverrideLock)
            {
                if (_statusOverride != null)
                {
                    if (DateTime.UtcNow <= _statusOverrideUntil)
                        status = _statusOverride;
                    else
                        _statusOverride = null;
                }
            }

            StatusLevel = status.Level;
            StatusText = BuildStatusText(status);
            StatusDetail = BuildStatusDetail(status);
        }

        private static string BuildStatusText(SystemStatus status)
        {
            if (!string.IsNullOrEmpty(status.TextOverride))
                return status.TextOverride;

            switch (status.Reason)
            {
                case Enums.Status.Reason.Optimizing:
                    return Localizer.String.Optimizing;

                case Enums.Status.Reason.CpuLoad:
                    return status.Level == Enums.Status.Level.Critical ? Localizer.String.StatusCpuHigh : Localizer.String.StatusCpuLoad;

                case Enums.Status.Reason.MemoryLow:
                    return Localizer.String.StatusMemoryLow;

                case Enums.Status.Reason.DiskLoad:
                    return Localizer.String.StatusDiskLoad;

                case Enums.Status.Reason.MemoryPressure:
                    return Localizer.String.StatusMemoryPressure;

                case Enums.Status.Reason.ElevatedLoad:
                    return Localizer.String.StatusElevatedLoad;

                case Enums.Status.Reason.CleanupResult:
                case Enums.Status.Reason.SystemNormal:
                default:
                    return Localizer.String.StatusNormal;
            }
        }

        private static string BuildStatusDetail(SystemStatus status)
        {
            if (status.Level == Enums.Status.Level.Optimizing || string.IsNullOrEmpty(status.ProcessName))
                return null;

            switch (status.Reason)
            {
                case Enums.Status.Reason.CpuLoad:
                    return string.Format(Localizer.Culture, "{0} — CPU {1:0}%", status.ProcessName, status.ContextValue);

                case Enums.Status.Reason.MemoryLow:
                case Enums.Status.Reason.MemoryPressure:
                    return string.Format(Localizer.Culture, "{0} — {1:0.0} GB", status.ProcessName, status.ContextValue);

                case Enums.Status.Reason.DiskLoad:
                    return string.Format(Localizer.Culture, "{0}: {1:0} ms", Localizer.String.StatusDiskLatency, status.ContextValue);

                default:
                    return null;
            }
        }

        private void RefreshTopProcess()
        {
            var snapshot = _processMonitorService != null ? _processMonitorService.LastSnapshot : null;

            if (snapshot == null || !snapshot.Valid || snapshot.TopByMemory.Count == 0)
            {
                TopProcessText = null;
                return;
            }

            var topMemory = snapshot.TopByMemory[0];
            var topCpu = snapshot.TopByCpu.Count > 0 ? snapshot.TopByCpu[0] : null;

            // Show whichever process is the main load right now: CPU-driven when CPU is the bottleneck
            if (topCpu != null && topCpu.CpuPercent >= 25 && (topMemory == null || topCpu.Name != topMemory.Name))
            {
                TopProcessText = string.Format(Localizer.Culture, "{0}   ·   CPU {1:0}%   ·   RAM {2:0.0} GB", topCpu.Name, topCpu.CpuPercent, topCpu.PrivateBytes / 1073741824.0);
                return;
            }

            TopProcessText = string.Format(Localizer.Culture, "{0}   ·   CPU {1:0}%   ·   RAM {2:0.0} GB", topMemory.Name, topMemory.CpuPercent, topMemory.PrivateBytes / 1073741824.0);
        }

        private void UpdateTray()
        {
            try
            {
                NotificationService.Update(StatusLevel, _monitoringService.Latest, StatusText);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void OnOptimizerRunningChanged()
        {
            RunOnDispatcher(delegate
            {
                IsSmartCleanupRunning = _smartOptimizer.IsRunning;

                if (IsSmartCleanupRunning)
                {
                    _statusEvaluator.IsOptimizing = true;
                    SetStatusOverride(new SystemStatus { Level = Enums.Status.Level.Optimizing, Reason = Enums.Status.Reason.Optimizing }, TimeSpan.FromSeconds(60));
                }
                else
                {
                    _statusEvaluator.IsOptimizing = false;
                }

                RaisePropertyChanged(() => IsSmartCleanupRunning);
                RaisePropertyChanged(() => SmartCleanupButtonText);
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private void OnCleanupCompleted(SmartCleanupResult result)
        {
            if (result == null)
                return;

            RunOnDispatcher(delegate
            {
                ShowCleanupResult(result);

                if (Settings.ShowOptimizationNotifications)
                {
                    var message = GetCleanupResultText(result);
                    NotificationService.Notify(message, Constants.App.DisplayName, 5, result.Outcome == CleanupOutcome.Timeout || result.Outcome == CleanupOutcome.Failed ? Enums.Icon.Notification.Warning : Enums.Icon.Notification.Information);
                }

                var handler = OnSmartCleanupCommandCompleted;

                if (handler != null && Settings.CloseAfterOptimization)
                    handler();
            });
        }

        private void ShowCleanupResult(SmartCleanupResult result)
        {
            var status = new SystemStatus { Level = Enums.Status.Level.Normal, Reason = Enums.Status.Reason.CleanupNotNeeded, TextOverride = GetCleanupResultText(result) };

            if (result.Outcome == CleanupOutcome.Completed)
            {
                // Blue: information about a finished operation
                status = new SystemStatus { Level = Enums.Status.Level.Optimizing, Reason = Enums.Status.Reason.CleanupResult, TextOverride = GetCleanupResultText(result) };
            }

            if (result.Outcome == CleanupOutcome.Timeout)
                status = new SystemStatus { Level = Enums.Status.Level.Warning, Reason = Enums.Status.Reason.CleanupTimeout, TextOverride = GetCleanupResultText(result) };

            SetStatusOverride(status, TimeSpan.FromSeconds(5));
            RefreshStatus();
            RaisePropertyChanged(() => StatusText);
            RaisePropertyChanged(() => StatusDetail);
        }

        private void SetStatusOverride(SystemStatus status, TimeSpan duration)
        {
            lock (_statusOverrideLock)
            {
                _statusOverride = status;
                _statusOverrideUntil = DateTime.UtcNow.Add(duration);
            }
        }

        private static string GetCleanupResultText(SmartCleanupResult result)
        {
            switch (result.Outcome)
            {
                case CleanupOutcome.NotNeeded:
                    return Localizer.String.CleanupNotNeeded;

                case CleanupOutcome.Completed:
                    return string.Format(Localizer.Culture, Localizer.String.CleanupCompleted, FormatBytes(result.FreedBytes), result.Duration.TotalSeconds);

                case CleanupOutcome.Ineffective:
                    return string.IsNullOrEmpty(result.TopProcessName)
                        ? Localizer.String.CleanupIneffective
                        : string.Format(Localizer.Culture, "{0} {1}: {2:0.0} GB", Localizer.String.CleanupIneffective, result.TopProcessName, result.TopProcessPrivateGb);

                case CleanupOutcome.Timeout:
                    return Localizer.String.CleanupTimeout;

                default:
                    return string.IsNullOrEmpty(result.Message) ? Localizer.String.Error : result.Message;
            }
        }

        private void OnPerformanceModeChanged(Enums.PerformanceMode.State state)
        {
            RunOnDispatcher(delegate
            {
                PerformanceModeText = state == Enums.PerformanceMode.State.Limited
                    ? Localizer.String.PerformanceModeLimited
                    : null;

                RaisePropertyChanged(() => IsPerformanceModeOn);
                RaisePropertyChanged(() => PerformanceModeText);
            });
        }

        private void OnLeakSuspected(MemoryLeakSuspect suspect)
        {
            if (suspect == null)
                return;

            Logger.Information(string.Format(Localizer.Culture, "Suspicious memory growth: {0} +{1:0.0} GB in {2:0} min", suspect.Name, suspect.GrowthBytes / 1073741824.0, suspect.Duration.TotalMinutes));

            if (!Settings.ShowOptimizationNotifications)
                return;

            RunOnDispatcher(delegate
            {
                NotificationService.Notify(string.Format(Localizer.Culture, Localizer.String.MemoryLeakDetail, suspect.Name, suspect.GrowthBytes / 1073741824.0, suspect.Duration.TotalMinutes), Localizer.String.MemoryLeakSuspected, 10, Enums.Icon.Notification.Warning);
            });
        }

        private static void RunOnDispatcher(Action action)
        {
            var dispatcher = System.Windows.Application.Current != null ? System.Windows.Application.Current.Dispatcher : null;

            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
        }

        private static string FormatBytes(long bytes)
        {
            const long gb = 1024L * 1024 * 1024;
            const long mb = 1024L * 1024;

            if (bytes <= 0)
                return "0";

            if (bytes >= gb)
                return string.Format(Localizer.Culture, "{0:0.0} GB", bytes / (double)gb);

            if (bytes >= mb)
                return string.Format(Localizer.Culture, "{0:0} MB", bytes / (double)mb);

            return string.Format(Localizer.Culture, "{0:0} KB", bytes / 1024.0);
        }

        #endregion

        #region Methods (hotkey)

        /// <summary>
        /// Registers the smart cleanup hotkey according to settings
        /// </summary>
        public void RegisterHotkey()
        {
            try
            {
                if (_hotkeyService == null)
                    return;

                if (!Settings.UseHotkey)
                {
                    UnregisterHotkey();
                    return;
                }

                UnregisterHotkey();

                _hotkey = new Hotkey(Settings.OptimizationModifiers, Settings.OptimizationKey);
                var registered = _hotkeyService.Register(_hotkey, SmartCleanup);

                if (!registered)
                    Logger.Warning(string.Format(Localizer.Culture, Localizer.String.HotkeyIsInUseByOperatingSystem, _hotkey));
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void UnregisterHotkey()
        {
            try
            {
                if (_hotkey != null && _hotkeyService != null)
                    _hotkeyService.Unregister(_hotkey);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                _hotkey = null;
            }
        }

        /// <summary>
        /// Reinitializes after hibernation resume
        /// </summary>
        public void ReinitializeAfterHibernation()
        {
            RegisterHotkey();
            App.ReleaseMemory();
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Unsubscribes from all service events.
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

            if (_disposed)
                return;

            _disposed = true;

            _monitoringService.OnSnapshot -= OnMonitoringSnapshot;
            _processMonitorService.OnLeakSuspected -= OnLeakSuspected;
            _smartOptimizer.IsRunningChanged -= OnOptimizerRunningChanged;
            _smartOptimizer.OnCompleted -= OnCleanupCompleted;
            _performanceModeService.OnChanged -= OnPerformanceModeChanged;

            UnregisterHotkey();
        }

        #endregion
    }
}
