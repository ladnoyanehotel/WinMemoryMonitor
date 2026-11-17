using System;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Performance Mode: while enabled (and on AC power) the Windows power plan is switched to
    /// High Performance (falling back to Ultimate Performance). The previous plan is remembered in the
    /// registry and restored when the mode is disabled, when the application closes, or at the next
    /// start after a crash. On battery the mode is limited: monitoring keeps running, the power plan
    /// is not changed. The application itself always stays lightweight and automatic cleanups remain
    /// restricted to CRITICAL memory pressure.
    /// </summary>
    public class PerformanceModeService : IPerformanceModeService, IDisposable
    {
        #region Fields

        private static readonly Guid HighPerformanceGuid = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        private static readonly Guid UltimatePerformanceGuid = new Guid("ded944d1-4b1d-443f-a6b3-8ddaba453199");

        private readonly object _sync = new object();
        private Enums.PerformanceMode.State _state = Enums.PerformanceMode.State.Off;
        private Guid? _previousScheme;
        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="PerformanceModeService"/> class and performs
        /// crash recovery: when the previous run ended with Performance Mode active, the saved power
        /// plan is restored.
        /// </summary>
        public PerformanceModeService()
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            RecoverAfterCrash();
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs when the Performance Mode state changes
        /// </summary>
        public event Action<Enums.PerformanceMode.State> OnChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the current Performance Mode state
        /// </summary>
        public Enums.PerformanceMode.State State
        {
            get
            {
                lock (_sync)
                {
                    return _state;
                }
            }
        }

        #endregion

        #region Methods

        private void RecoverAfterCrash()
        {
            try
            {
                if (!Settings.PerformanceModeActive)
                    return;

                var previous = Settings.PreviousPowerScheme;

                if (string.IsNullOrWhiteSpace(previous))
                {
                    Settings.PerformanceModeActive = false;
                    Settings.Save();
                    return;
                }

                Guid guid;

                if (Guid.TryParseExact(previous, "D", out guid))
                {
                    Logger.Information("Performance Mode was active at shutdown; restoring the previous power plan");
                    SetActiveScheme(guid);
                }

                Settings.PerformanceModeActive = false;
                Settings.PreviousPowerScheme = string.Empty;
                Settings.Save();
            }
            catch (Exception e)
            {
                Logger.Error(e, "Performance Mode crash recovery failed");
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.StatusChange)
                return;

            lock (_sync)
            {
                if (_state != Enums.PerformanceMode.State.On && _state != Enums.PerformanceMode.State.Limited)
                    return;
            }

            // Re-evaluate AC/battery when the power source changes while the mode is active
            ApplyToPowerSource();
        }

        private static bool IsOnAcPower()
        {
            try
            {
                var status = new Structs.Windows.SystemPowerStatus();

                if (NativeMethods.GetSystemPowerStatus(status))
                    return status.AcLineStatus == 1;

                return true; // Unknown state: assume desktop/AC
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return true;
            }
        }

        private static Guid? GetActiveScheme()
        {
            IntPtr guidPtr;

            var result = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out guidPtr);

            if (result != 0 || guidPtr == IntPtr.Zero)
                return null;

            try
            {
                return (Guid)MarshalPtrToGuid(guidPtr);
            }
            finally
            {
                NativeMethods.LocalFree(guidPtr);
            }
        }

        private static object MarshalPtrToGuid(IntPtr guidPtr)
        {
            return System.Runtime.InteropServices.Marshal.PtrToStructure(guidPtr, typeof(Guid));
        }

        private static bool SetActiveScheme(Guid guid)
        {
            var target = guid;
            var result = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref target);

            if (result == 0)
                return true;

            Logger.Warning(string.Format(CultureInfo.InvariantCulture, "Failed to activate power scheme {0}: error {1}", guid, result));
            return false;
        }

        private static Guid? FindPerformanceScheme()
        {
            // Prefer the built-in High Performance plan; some systems only expose Ultimate Performance
            var probe = HighPerformanceGuid;

            if (NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref probe) == 0)
                return HighPerformanceGuid;

            probe = UltimatePerformanceGuid;

            if (NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref probe) == 0)
                return UltimatePerformanceGuid;

            // Last resort: duplicate the hidden High Performance plan via powercfg
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "-duplicatescheme " + HighPerformanceGuid.ToString("D"),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using (var process = Process.Start(startInfo))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10000);

                    var index = output.IndexOf("GUID:", StringComparison.OrdinalIgnoreCase);

                    if (index >= 0)
                    {
                        var start = output.IndexOf('-', index);

                        if (start >= 0)
                        {
                            var end = output.IndexOfAny(new[] { ' ', '\r', '\n' }, start);

                            if (end > start)
                            {
                                Guid guid;

                                if (Guid.TryParseExact(output.Substring(start, end - start), "D", out guid))
                                    return guid;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "powercfg duplicatescheme failed");
            }

            return null;
        }

        private void ApplyToPowerSource()
        {
            lock (_sync)
            {
                if (_state != Enums.PerformanceMode.State.On && _state != Enums.PerformanceMode.State.Limited)
                    return;
            }

            if (IsOnAcPower())
                ActivateSchemeForMode();
            else
                Transition(Enums.PerformanceMode.State.Limited, "Performance Mode limited: running on battery");
        }

        private void ActivateSchemeForMode()
        {
            var scheme = FindPerformanceScheme();

            if (!scheme.HasValue)
            {
                Transition(Enums.PerformanceMode.State.Unavailable, "Performance Mode is not supported on this system");
                return;
            }

            SetActiveScheme(scheme.Value);
            Transition(Enums.PerformanceMode.State.On, "Performance Mode on: " + scheme.Value);
        }

        /// <summary>
        /// Enables Performance Mode
        /// </summary>
        public void Enable()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                if (_state == Enums.PerformanceMode.State.On || _state == Enums.PerformanceMode.State.Limited)
                    return;

                // Remember the current plan for restore; persist immediately for crash recovery
                var current = GetActiveScheme();

                if (current.HasValue)
                {
                    _previousScheme = current;
                    Settings.PreviousPowerScheme = current.Value.ToString("D");
                    Settings.PerformanceModeActive = true;
                    Settings.Save();
                    Logger.Information("Performance Mode: previous power plan saved (" + current.Value + ")");
                }
            }

            if (!IsOnAcPower())
            {
                Transition(Enums.PerformanceMode.State.Limited, "Performance Mode limited: running on battery. The power plan is not changed.");
                return;
            }

            ActivateSchemeForMode();
        }

        /// <summary>
        /// Disables Performance Mode and restores the previous power plan
        /// </summary>
        public void Disable()
        {
            Guid? previous;

            lock (_sync)
            {
                if (_state == Enums.PerformanceMode.State.Off)
                    return;

                previous = _previousScheme;
                _previousScheme = null;

                Settings.PerformanceModeActive = false;
                Settings.PreviousPowerScheme = string.Empty;
                Settings.Save();
            }

            if (previous.HasValue)
            {
                SetActiveScheme(previous.Value);
                Logger.Information("Performance Mode off: power plan restored (" + previous.Value + ")");
            }

            Transition(Enums.PerformanceMode.State.Off, "Performance Mode off");
        }

        /// <summary>
        /// Restores the previous power plan if the mode is active (called on application shutdown)
        /// </summary>
        public void ShutdownRestore()
        {
            lock (_sync)
            {
                if (_state == Enums.PerformanceMode.State.Off)
                    return;
            }

            Disable();
        }

        private void Transition(Enums.PerformanceMode.State newState, string message)
        {
            lock (_sync)
            {
                if (_state == newState)
                    return;

                _state = newState;
            }

            Logger.Information(message);

            var handler = OnChanged;

            if (handler != null)
                handler(newState);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Unsubscribes from system power events and restores the power plan when active.
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
                if (_disposed)
                    return;

                _disposed = true;
            }

            SystemEvents.PowerModeChanged -= OnPowerModeChanged;

            ShutdownRestore();
        }

        #endregion
    }
}
