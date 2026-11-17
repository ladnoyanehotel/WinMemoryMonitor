using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Threading;
using Cursors = System.Windows.Input.Cursors;
using Mouse = System.Windows.Input.Mouse;
using WpfApplication = System.Windows.Application;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Notification Service: tray icon that reflects the system status with a hardcoded color scheme
    /// (green = normal, yellow = warning, red = critical, blue = optimizing) plus the RAM usage
    /// percentage. The menu contains exactly: Open, Smart Cleanup, Performance Mode, Exit.
    /// </summary>
    public class NotificationService : INotificationService
    {
        #region Fields

        private Icon _currentIcon;
        private bool _disposed;
        private readonly Icon _imageIcon;
        private readonly NotifyIcon _notifyIcon;
        private readonly object _disposeLock = new object();

        // Cached drawing objects (created once, disposed with the service)
        private readonly Font _cachedFont;
        private readonly StringFormat _cachedFormat;

        // The icon bitmap is regenerated only when the status level or the displayed percentage
        // actually changes; a redraw every second would burn GDI churn and CPU for nothing.
        private Enums.Status.Level _lastIconLevel = (Enums.Status.Level)(-1);
        private int _lastIconRamPercent = -1;

        // Hardcoded status colors; the user cannot change them
        private static readonly Color StatusNormal = Color.FromArgb(46, 160, 67);     // green
        private static readonly Color StatusWarning = Color.FromArgb(214, 158, 46);   // yellow
        private static readonly Color StatusCritical = Color.FromArgb(211, 47, 47);   // red
        private static readonly Color StatusOptimizing = Color.FromArgb(0, 174, 247); // blue

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationService" /> class.
        /// </summary>
        /// <param name="notifyIcon">Notify Icon</param>
        public NotificationService(NotifyIcon notifyIcon)
        {
            _imageIcon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            _notifyIcon = notifyIcon;
            _cachedFont = new Font("Consolas", 14F, FontStyle.Regular, GraphicsUnit.Pixel);
            _cachedFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        }

        /// <summary>
        /// Initializes the notification service
        /// </summary>
        public void Initialize()
        {
            if (_notifyIcon == null)
                return;

            lock (_disposeLock)
            {
                if (_disposed)
                    return;
            }

            // Notification Areas (Menu)
            _notifyIcon.ContextMenuStrip = new TrayIconContextMenuControl();

            // Open
            _notifyIcon.ContextMenuStrip.Items.Add(Localizer.String.Open, null, (sender, args) =>
            {
                if (WpfApplication.Current != null && WpfApplication.Current.MainWindow != null)
                {
                    WpfApplication.Current.MainWindow.Show();
                    WpfApplication.Current.MainWindow.WindowState = System.Windows.WindowState.Normal;
                    WpfApplication.Current.MainWindow.Activate();
                }
            });

            // Smart Cleanup
            _notifyIcon.ContextMenuStrip.Items.Add(Localizer.String.SmartCleanup, null, (sender, args) =>
            {
                var mainViewModel = DependencyInjection.Container.TryResolve<MainViewModel>();

                if (mainViewModel != null && mainViewModel.SmartCleanupCommand.CanExecute(null))
                    mainViewModel.SmartCleanupCommand.Execute(null);
            });

            // Performance Mode
            _notifyIcon.ContextMenuStrip.Items.Add(Localizer.String.PerformanceMode, null, (sender, args) =>
            {
                var mainViewModel = DependencyInjection.Container.TryResolve<MainViewModel>();

                if (mainViewModel != null && mainViewModel.TogglePerformanceModeCommand.CanExecute(null))
                    mainViewModel.TogglePerformanceModeCommand.Execute(null);
            });

            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

            // Exit
            _notifyIcon.ContextMenuStrip.Items.Add(Localizer.String.Exit, null, (sender, args) =>
            {
                App.Shutdown();
            });

            _notifyIcon.Text = Constants.App.DisplayName;
            _notifyIcon.Icon = _imageIcon;
            _notifyIcon.Visible = true;
        }

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
        /// Releases managed resources (tray icon, cached drawing objects).
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_disposeLock)
                {
                    if (_disposed)
                        return;

                    _disposed = true;
                }

                DisposeQuietly(_cachedFont);
                DisposeQuietly(_cachedFormat);

                try
                {
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Icon = null;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex);
                }

                if (_currentIcon != null && _currentIcon != _imageIcon)
                    DisposeQuietly(_currentIcon);

                DisposeQuietly(_imageIcon);

                try
                {
                    if (_notifyIcon != null)
                        _notifyIcon.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex);
                }
            }
        }

        private static void DisposeQuietly(IDisposable disposable)
        {
            try
            {
                if (disposable != null)
                    disposable.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        #endregion

        #region Methods

        private static Color GetStatusColor(Enums.Status.Level level)
        {
            switch (level)
            {
                case Enums.Status.Level.Critical:
                    return StatusCritical;

                case Enums.Status.Level.Warning:
                    return StatusWarning;

                case Enums.Status.Level.Optimizing:
                    return StatusOptimizing;

                default:
                    return StatusNormal;
            }
        }

        /// <summary>
        /// Gets a custom icon displaying the RAM usage percentage on a status-colored rounded square
        /// </summary>
        private Icon GetStatusIcon(Enums.Status.Level level, int ramUsagePercent)
        {
            try
            {
                using (var image = new Bitmap(16, 16))
                using (var graphics = Graphics.FromImage(image))
                using (var backgroundBrush = new SolidBrush(GetStatusColor(level)))
                using (var textBrush = new SolidBrush(Color.White))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(0, 0, 10, 10, 180, 90);
                        path.AddArc(5, 0, 10, 10, 270, 90);
                        path.AddArc(5, 5, 10, 10, 0, 90);
                        path.AddArc(0, 5, 10, 10, 90, 90);
                        path.CloseFigure();

                        graphics.FillPath(backgroundBrush, path);
                    }

                    graphics.DrawString(string.Format(CultureInfo.InvariantCulture, "{0:00}", ramUsagePercent >= 100 ? 99 : ramUsagePercent), _cachedFont, textBrush, 8F, 9F, _cachedFormat);

                    var handle = image.GetHicon();

                    using (var icon = Icon.FromHandle(handle))
                    {
                        var clonedIcon = (Icon)icon.Clone();

                        NativeMethods.DestroyIcon(handle);

                        return clonedIcon;
                    }
                }
            }
            catch
            {
                return _imageIcon;
            }
        }

        /// <summary>
        /// Shows or hides the loading cursor
        /// </summary>
        /// <param name="running">if set to <c>true</c> shows the loading cursor</param>
        public void Loading(bool running)
        {
            if (WpfApplication.Current == null || WpfApplication.Current.Dispatcher == null)
                return;

            WpfApplication.Current.Dispatcher.Invoke((Action)delegate
            {
                Mouse.OverrideCursor = running ? Cursors.Wait : null;
            });
        }

        /// <summary>
        /// Displays a balloon tip notification from the system tray
        /// </summary>
        /// <param name="message">The notification message text</param>
        /// <param name="title">The notification title</param>
        /// <param name="timeout">The time period in seconds to display the notification</param>
        /// <param name="icon">The notification icon type</param>
        public void Notify(string message, string title = null, int timeout = 5, Enums.Icon.Notification icon = Enums.Icon.Notification.None)
        {
            if (_notifyIcon == null)
                return;

            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Visible = true;

                _notifyIcon.ShowBalloonTip(timeout * 1000, title, message, (ToolTipIcon)icon);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex);
            }
        }

        /// <summary>
        /// Updates the tray icon and tooltip from the current system status. Called from the UI
        /// thread about once per second.
        /// </summary>
        /// <param name="level">System status level</param>
        /// <param name="snapshot">Latest metrics snapshot</param>
        /// <param name="statusText">Localized status text for the tooltip</param>
        public void Update(Enums.Status.Level level, SystemMetricsSnapshot snapshot, string statusText)
        {
            lock (_disposeLock)
            {
                if (_disposed || _notifyIcon == null)
                    return;

                try
                {
                    var ramPercent = snapshot != null ? (int)Math.Round(snapshot.RamUsagePercent) : 0;
                    var cpuPercent = snapshot != null && snapshot.CpuValid ? (int)Math.Round(snapshot.CpuUsage) : 0;

                    var text = string.Format(CultureInfo.InvariantCulture, "{0}{1}CPU {2}% · RAM {3}%{4}{5}", Constants.App.DisplayName, Environment.NewLine, cpuPercent, ramPercent, Environment.NewLine, statusText ?? string.Empty);

                    if (text.Length > 63)
                        text = text.Substring(0, 63);

                    _notifyIcon.Text = text;

                    if (level == _lastIconLevel && ramPercent == _lastIconRamPercent)
                        return; // Nothing visible changed; keep the current icon

                    _lastIconLevel = level;
                    _lastIconRamPercent = ramPercent;

                    var newIcon = GetStatusIcon(level, ramPercent);
                    var oldIcon = _currentIcon;

                    _notifyIcon.Icon = newIcon;
                    _currentIcon = newIcon;

                    if (oldIcon != null && oldIcon != _imageIcon && oldIcon != newIcon)
                        DisposeQuietly(oldIcon);
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex);
                }
            }
        }

        #endregion
    }
}
