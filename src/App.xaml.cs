using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Windows Memory Cleaner
    /// </summary>
    public partial class App : IDisposable
    {
        #region Fields

        private static bool _isRunning;
        private static Mutex _mutex;
        private static NotifyIcon _notifyIcon;
        private static readonly List<string> _notifications = new List<string>();
        private static readonly object _showHidelock = new object();

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="App" /> class.
        /// </summary>
        public App()
        {
            // Log
            Logger.Level = IsInDebugMode ? Enums.Log.Levels.Debug : Enums.Log.Levels.Information;

            // Events
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            Dispatcher.UnhandledException += OnDispatcherUnhandledException;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether this instance is in debug mode.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is in debug mode; otherwise, <c>false</c>.
        /// </value>
        public static bool IsInDebugMode
        {
            get
            {
#if DEBUG
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is in design mode.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is in design mode; otherwise, <c>false</c>.
        /// </value>
        public static bool IsInDesignMode
        {
            get
            {
                return DesignerProperties.GetIsInDesignMode(new DependencyObject());
            }
        }

        /// <summary>
        /// App path
        /// </summary>
        public static string Path { get; private set; }

        /// <summary>
        /// App version
        /// </summary>
        public static Version Version { get; private set; }

        #endregion

        #region IDisposable

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases managed resources: background services (monitoring, worker, performance mode) and the tray icon.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Stop services in reverse startup order; the scheduler goes last because
                // the other services run on its thread.
                TryDisposeService<SmartMemoryOptimizer>();
                TryDisposeService<SystemStatusEvaluator>();
                TryDisposeService<MemoryPressureService>();
                TryDisposeService<ProcessMonitorService>();
                TryDisposeService<MonitoringService>();
                TryDisposeService<WorkerProcessRunner>();

                // Performance Mode restores the previous power plan when active
                TryDisposeService<PerformanceModeService>();

                TryDisposeService<SamplingScheduler>();

                Logger.Dispose();

                try
                {
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                }
                catch
                {
                    // ignored
                }

                if (_mutex != null)
                {
                    try
                    {
                        _mutex.ReleaseMutex();
                    }
                    catch
                    {
                        // ignored
                    }

                    try
                    {
                        _mutex.Dispose();
                    }
                    catch
                    {
                        // ignored
                    }

                    _mutex = null;
                }

                try
                {
                    if (_notifyIcon != null)
                        _notifyIcon.Dispose();
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static void TryDisposeService<T>()
        {
            try
            {
                var service = DependencyInjection.Container.TryResolve<T>();

                if (service is IDisposable)
                    ((IDisposable)service).Dispose();
            }
            catch
            {
                // Services may not be registered in early-exit paths
            }
        }

        #endregion

        #region Methods

        private void Initialize()
        {
            // DI/IOC: monitoring and optimization pipeline
            var scheduler = new SamplingScheduler();

            DependencyInjection.Container.Register(scheduler);
            DependencyInjection.Container.Register<IComputerService, ComputerService>(true);
            DependencyInjection.Container.Register<IHotkeyService, HotkeyService>();
            DependencyInjection.Container.Register<INotificationService, NotificationService>(true);
            DependencyInjection.Container.Register<IMonitoringService, MonitoringService>(true);
            DependencyInjection.Container.Register<IProcessMonitorService, ProcessMonitorService>(true);
            DependencyInjection.Container.Register<IMemoryPressureService, MemoryPressureService>(true);
            DependencyInjection.Container.Register<ISystemStatusEvaluator, SystemStatusEvaluator>(true);
            DependencyInjection.Container.Register<IWorkerProcessRunner, WorkerProcessRunner>(true);
            DependencyInjection.Container.Register<ISmartMemoryOptimizer, SmartMemoryOptimizer>(true);
            DependencyInjection.Container.Register<IPerformanceModeService, PerformanceModeService>(true);

            // App properties
            Path = Helper.GetExecutablePath();
            Version = Helper.GetVersion();

            // Check if app is already running
            bool createdNew;

            _mutex = new Mutex(true, Constants.App.Id, out createdNew);
            _isRunning = !createdNew;

            // App Migration
            if (!_isRunning)
                Migrator.Run();
        }

        /// <summary>
        /// Navigates the specified URI.
        /// </summary>
        /// <param name="uri">The URI.</param>
        public static void Navigate(Uri uri)
        {
            if (uri == null)
                return;

            using (Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            })) { }
        }

        /// <summary>
        /// Called when [dispatcher unhandled exception].
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="DispatcherUnhandledExceptionEventArgs" /> instance containing the event data.</param>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            Logger.Error(e.Exception);
        }

        /// <summary>
        /// Handles power mode changes (suspend/resume from hibernation).
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="PowerModeChangedEventArgs" /> instance containing the event data.</param>
        private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            try
            {
                switch (e.Mode)
                {
                    case PowerModes.Resume:
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try
                            {
                                Thread.Sleep(Environment.OSVersion.Version.Major < 6 ? 10000 : 5000);

                                var mainViewModel = DependencyInjection.Container.Resolve<MainViewModel>();

                                if (mainViewModel != null)
                                    mainViewModel.ReinitializeAfterHibernation();
                            }
                            catch (Exception threadEx)
                            {
                                Logger.Error("Error in power mode resume handling: " + threadEx.GetMessage());
                            }
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error handling power mode change: " + ex.GetMessage());
            }
        }

        /// <summary>
        /// Called when [process exit].
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void OnProcessExit(object sender, EventArgs e)
        {
            Dispose();
        }

        /// <summary>
        /// Called when [notify icon click]. Left click shows/hides the window.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="EventArgs" /> instance containing the event data.</param>
        private void OnNotifyIconClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            lock (_showHidelock)
            {
                if (MainWindow == null)
                    return;

                if (MainWindow.OwnedWindows.Cast<View>().Any(window => window != null && window.IsDialog))
                {
                    MainWindow.Activate();
                    MainWindow.Topmost = true;
                    MainWindow.Topmost = Settings.AlwaysOnTop;

                    return;
                }

                switch (MainWindow.Visibility)
                {
                    case Visibility.Collapsed:
                    case Visibility.Hidden:
                        MainWindow.Show();

                        MainWindow.WindowState = WindowState.Normal;

                        MainWindow.Activate();
                        MainWindow.Focus();

                        MainWindow.Topmost = true;
                        MainWindow.Topmost = Settings.AlwaysOnTop;
                        MainWindow.ShowInTaskbar = true;

                        MainWindow.Dispatcher.BeginInvoke((Action)(() =>
                        {
                            var mainWindow = MainWindow as MainWindow;

                            if (mainWindow != null)
                                mainWindow.SetFocusToSmartCleanButton();
                        }), DispatcherPriority.ApplicationIdle);
                        break;

                    case Visibility.Visible:
                        MainWindow.Hide();

                        MainWindow.ShowInTaskbar = false;
                        break;
                }

                ReleaseMemory();
            }
        }

        /// <summary>
        /// Raises the <see cref="E:Startup" /> event.
        /// </summary>
        /// <param name="startupEvent">The <see cref="StartupEventArgs" /> instance containing the event data.</param>
        protected override void OnStartup(StartupEventArgs startupEvent)
        {
            var startupType = Enums.StartupType.App;

            try
            {
                Initialize();

                var commandLineArguments = startupEvent != null ? startupEvent.Args.Select(arg => arg.Replace("-", "/").Trim()).ToArray() : null;

                if (commandLineArguments != null)
                {
                    // Process command‑line arguments
                    foreach (var argument in commandLineArguments.Select(arg => arg.Replace("/", string.Empty)))
                    {
                        // Silent smart cleanup: any former memory-area argument maps to a single smart cleanup
                        if (argument.Equals("Optimize", StringComparison.OrdinalIgnoreCase) || argument.Equals("Cleanup", StringComparison.OrdinalIgnoreCase) || argument.Equals("Silent", StringComparison.OrdinalIgnoreCase))
                            startupType = Enums.StartupType.Silent;

                        if (argument.Equals(Constants.App.CommandLineArgument.Reset, StringComparison.OrdinalIgnoreCase))
                            startupType = Enums.StartupType.Reset;
                    }
                }

                switch (startupType)
                {
                    case Enums.StartupType.App:
                        if (_isRunning)
                        {
                            try
                            {
                                var appHandle = NativeMethods.FindWindow(null, Constants.App.Title);

                                if (appHandle != IntPtr.Zero && NativeMethods.IsWindowVisible(appHandle))
                                {
                                    int appId;

                                    if (NativeMethods.GetWindowThreadProcessId(appHandle, out appId) != Constants.Windows.SystemErrorCode.ErrorSuccess)
                                        NativeMethods.AllowSetForegroundWindow(appId);

                                    NativeMethods.ShowWindowAsync(appHandle, Constants.Windows.ShowWindow.Restore);
                                    NativeMethods.SetForegroundWindow(appHandle);
                                }
                            }
                            finally
                            {
                                Shutdown(true);
                            }
                        }

                        NativeMethods.AllowSetForegroundWindow(Process.GetCurrentProcess().Id);

                        // App startup shortcut
                        Helper.StartMenuShortcut(Settings.CreateStartMenuShortcut);

                        // Theme
                        ThemeManager.Theme = Enums.Theme.Dark;

                        // Notification Areas
                        _notifyIcon = new NotifyIcon();
                        _notifyIcon.MouseUp += OnNotifyIconClick;

                        // DI/IOC
                        DependencyInjection.Container.Register(_notifyIcon);

                        var mainWindow = new MainWindow();

                        // WINMEMORYCLEANER_SHOW=1 forces the window even when starting minimized (troubleshooting aid)
                        if (!Settings.StartMinimized || Environment.GetEnvironmentVariable("WINMEMORYCLEANER_SHOW") == "1")
                        {
                            mainWindow.Show();
                            mainWindow.Activate();
                            mainWindow.Focus();
                            mainWindow.Topmost = true;
                            mainWindow.Topmost = Settings.AlwaysOnTop;
                        }

                        // Subscribe to power events
                        SystemEvents.PowerModeChanged += OnPowerModeChanged;

                        // Process notifications
                        foreach (var notification in _notifications)
                        {
                            Logger.Information(notification);
                            DependencyInjection.Container.Resolve<INotificationService>().Notify(notification);
                        }

                        RunOnStartup(Settings.RunOnStartup);
                        ReleaseMemory();
                        break;

                    case Enums.StartupType.Reset:
                        try
                        {
                            Logger.EnableConsoleOutput();

                            // Kill all other running instances of the application to prevent conflicts and infinite loops
                            var currentProcess = Process.GetCurrentProcess();

                            using (currentProcess)
                            {
                                var processes = Process.GetProcessesByName(currentProcess.ProcessName).Where(p => p.Id != currentProcess.Id).ToArray();

                                foreach (var process in processes)
                                {
                                    try
                                    {
                                        process.Kill();
                                        process.WaitForExit(5000);
                                    }
                                    catch
                                    {
                                        // ignored
                                    }
                                    finally
                                    {
                                        process.Dispose();
                                    }
                                }
                            }

                            Settings.Reset(keepLanguage: true); // Reset settings to defaults while preserving language preference

                            Settings.Save();

                            Logger.Information(Localizer.String.ResetCommand);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(string.Format(Localizer.Culture, Localizer.String.ErrorResetCommand, ex.GetMessage()));
                        }
                        finally
                        {
                            Shutdown(true);
                        }

                        break;

                    case Enums.StartupType.Silent:
                        // Headless smart cleanup (task scheduler friendly)
                        var optimizer = DependencyInjection.Container.Resolve<ISmartMemoryOptimizer>();
                        var result = optimizer.Cleanup(Enums.Memory.Optimization.Reason.Manual);

                        if (result != null)
                            Logger.Information(string.Format(Localizer.Culture, "Silent cleanup: {0} ({1})", result.Outcome, result.Message));

                        Shutdown();
                        break;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);

                if (startupType == Enums.StartupType.App)
                    ShowDialog(e);

                Shutdown(true);
            }
        }

        /// <summary>
        /// Called when [unhandled exception].
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="UnhandledExceptionEventArgs" /> instance containing the event data.</param>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.Error((Exception)e.ExceptionObject);
        }

        /// <summary>
        /// Releases the app memory
        /// </summary>
        public static void ReleaseMemory()
        {
            // Garbage Collector
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            }
            catch
            {
                // ignored
            }

            // Optimize App Working Set
            try
            {
                var process = Process.GetCurrentProcess();

                using (process)
                    NativeMethods.EmptyWorkingSet(process.Handle);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        /// <summary>
        /// Runs the app on startup
        /// </summary>
        /// <param name="enable">if set to <c>true</c> [enable].</param>
        public static void RunOnStartup(bool enable)
        {
            try
            {
                if (enable)
                {
                    var isTaskCreated = false;

                    try
                    {
                        var taskXml = string.Format
                            (
                                CultureInfo.InvariantCulture,
                                @"<?xml version=""1.0"" encoding=""UTF-16""?>
                                <Task version=""1.2""
		                                xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
		                                <RegistrationInfo>
			                                <Author>{3}</Author>
			                                <Description>Runs {0} at logon.</Description>
			                                <Date>{4}</Date>
			                                </RegistrationInfo>
		                                <Triggers>
			                                <LogonTrigger>
				                                <Enabled>true</Enabled>
				                                </LogonTrigger>
			                                </Triggers>
		                                <Principals>
			                                <Principal id=""Author"">
				                                <UserId>{2}</UserId>
				                                <LogonType>InteractiveToken</LogonType>
				                                <RunLevel>HighestAvailable</RunLevel>
			                                </Principal>
			                                </Principals>
		                                <Settings>
			                                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
			                                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
			                                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
			                                <AllowHardTerminate>true</AllowHardTerminate>
			                                <StartWhenAvailable>true</StartWhenAvailable>
			                                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
			                                <IdleSettings>
				                                <WaitTimeout>PT10M</WaitTimeout>
				                                <StopOnIdleEnd>false</StopOnIdleEnd>
				                                <RestartOnIdle>false</RestartOnIdle>
				                                </IdleSettings>
			                                <AllowStartOnDemand>true</AllowStartOnDemand>
			                                <Enabled>true</Enabled>
			                                <Hidden>false</Hidden>
			                                <RunOnlyIfIdle>false</RunOnlyIfIdle>
			                                <WakeToRun>false</WakeToRun>
			                                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
			                                <Priority>7</Priority>
			                                </Settings>
		                                <Actions Context=""Author"">
			                                <Exec>
				                                <Command>""{1}""</Command>
				                                </Exec>
			                                </Actions>
                                </Task>",
                                Constants.App.Title,
                                Path,
                                WindowsIdentity.GetCurrent().User.Value,
                                string.Format(CultureInfo.InvariantCulture, "WMC {0} ({1})", string.Format(Localizer.Culture, Constants.App.VersionFormat, Version.Major, Version.Minor, Version.Build), Environment.UserName),
                                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
                            );

                        var tempXmlFile = System.IO.Path.GetTempFileName();

                        File.WriteAllText(tempXmlFile, taskXml);

                        var createStartInfo = new ProcessStartInfo("schtasks")
                        {
                            Arguments = string.Format(CultureInfo.InvariantCulture, @"/CREATE /F /TN ""{0}"" /XML ""{1}""", Constants.App.Title, tempXmlFile),
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            RedirectStandardError = true
                        };

                        using (var createProcess = Process.Start(createStartInfo))
                        {
                            var errorMessage = createProcess.StandardError.ReadToEnd();
                            createProcess.WaitForExit();

                            if (createProcess.ExitCode == Constants.Windows.SystemErrorCode.ErrorSuccess)
                                isTaskCreated = true;
                            else
                                Logger.Error(string.Format(Localizer.Culture, "XML task creation failed (will attempt fallback). Error: {0}", errorMessage));
                        }

                        Helper.DeleteFile(tempXmlFile);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(string.Format(Localizer.Culture, "An exception occurred during XML task creation (will attempt fallback): {0}", ex.GetMessage()));
                    }

                    if (!isTaskCreated)
                    {
                        Logger.Information("Attempting basic fallback method to create startup task.");

                        var createStartInfo = new ProcessStartInfo("schtasks")
                        {
                            Arguments = string.Format(CultureInfo.InvariantCulture, @"/CREATE /F /SC ONLOGON /TN ""{0}"" /TR ""{1}"" /RU ""{2}""", Constants.App.Title, Path, Environment.UserName),
                            CreateNoWindow = true,
                            UseShellExecute = false,
                            WindowStyle = ProcessWindowStyle.Hidden,
                            RedirectStandardError = true
                        };

                        using (var createProcess = Process.Start(createStartInfo))
                        {
                            var errorMessage = createProcess.StandardError.ReadToEnd();
                            createProcess.WaitForExit();

                            if (createProcess.ExitCode != Constants.Windows.SystemErrorCode.ErrorSuccess)
                                Logger.Error(string.Format(Localizer.Culture, "Fallback task creation also failed for '{0}'. Error: {1}", Constants.App.Title, errorMessage));
                        }
                    }
                }
                else
                {
                    var deleteStartInfo = new ProcessStartInfo("schtasks")
                    {
                        Arguments = string.Format(CultureInfo.InvariantCulture, @"/DELETE /F /TN ""{0}""", Constants.App.Title),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using (var deleteProcess = Process.Start(deleteStartInfo))
                    {
                        deleteProcess.WaitForExit();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(string.Format(Localizer.Culture, "An error occurred while managing the scheduled task for app startup. Error: {0}", e.GetMessage()));
            }
        }

        /// <summary>
        /// Shows a dialog
        /// </summary>
        /// <param name="exception">Exception</param>
        /// <param name="message">The message.</param>
        private void ShowDialog(Exception exception, string message = null)
        {
            ShowDialog(message ?? exception.GetMessage(), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// Shows the dialog.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="button">The button.</param>
        /// <param name="icon">The icon.</param>
        /// <param name="defaultResult">The default result.</param>
        /// <param name="options">The options.</param>
        private void ShowDialog(string message, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.OK, System.Windows.MessageBoxOptions options = System.Windows.MessageBoxOptions.None)
        {
            try
            {
                System.Windows.MessageBox.Show(message, Constants.App.Title, button, icon, defaultResult, options);
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>
        /// Shuts down the app
        /// </summary>
        /// <param name="force">if set to <c>true</c> [force].</param>
        public static void Shutdown(bool force = false)
        {
            try
            {
                if (force)
                    Environment.Exit(Constants.Windows.SystemErrorCode.ErrorSuccess);
                else
                    Current.Shutdown();
            }
            catch
            {
                Environment.Exit(Constants.Windows.SystemErrorCode.ErrorSuccess);
            }
        }

        #endregion
    }
}
