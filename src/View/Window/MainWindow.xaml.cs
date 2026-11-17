using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Main Window: compact system monitor with smart cleanup and performance mode.
    /// The code-behind only handles view concerns (window placement, chrome buttons,
    /// sparkline invalidation); all logic lives in services behind the view model.
    /// </summary>
    public partial class MainWindow : View
    {
        #region Fields

        private MainViewModel _viewModel;
        private SettingsWindow _settingsWindow;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            _viewModel = DataContext as MainViewModel;
            Topmost = Settings.AlwaysOnTop;

            if (_viewModel != null)
            {
                _viewModel.MetricsUpdated += OnMetricsUpdated;
                _viewModel.OnSmartCleanupCommandCompleted += OnSmartCleanupCompleted;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            ApplyWindowPlacement();
            ApplyCompactSize();
        }

        #endregion

        #region Methods (window placement and mode)

        private void ApplyWindowPlacement()
        {
            if (!Settings.WindowPositionSaved)
                return;

            try
            {
                // Keep the window on screen when the display layout changed since the last run
                var virtualLeft = SystemParameters.VirtualScreenLeft;
                var virtualTop = SystemParameters.VirtualScreenTop;
                var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
                var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

                var left = Settings.WindowLeft;
                var top = Settings.WindowTop;

                if (left >= virtualLeft - 100 && left <= virtualRight - 100 && top >= virtualTop - 30 && top <= virtualBottom - 60)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = left;
                    Top = top;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void SaveWindowPlacement()
        {
            try
            {
                Settings.WindowLeft = Left;
                Settings.WindowTop = Top;
                Settings.Save();
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void ApplyCompactSize()
        {
            if (Settings.CompactMode)
            {
                Width = 420;
                Height = 180;
            }
            else
            {
                Width = 620;
                Height = 470;
            }
        }

        #endregion

        #region Methods (view events)

        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CompactMode")
                ApplyCompactSize();
        }

        private void OnMetricsUpdated()
        {
            // Sparklines re-render from their ring buffers; no per-point allocations
            CpuSparkline.InvalidateVisual();
            RamSparkline.InvalidateVisual();
            GpuSparkline.InvalidateVisual();
            DiskSparkline.InvalidateVisual();
        }

        private void OnSmartCleanupCompleted()
        {
            // Honor CloseAfterOptimization without blocking the UI thread
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

            timer.Tick += delegate
            {
                timer.Stop();

                if (Settings.CloseToTheNotificationArea)
                {
                    Hide();
                    ShowInTaskbar = false;
                }
                else
                {
                    App.Shutdown();
                }
            };

            timer.Start();
        }

        private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void OnAlwaysOnTopButtonClick(object sender, RoutedEventArgs e)
        {
            // Keep the visual toggle and the actual window flag in sync
            var pinButton = sender as ToggleButton;

            if (pinButton != null)
                Topmost = pinButton.IsChecked == true;
        }

        private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsWindow != null)
                {
                    _settingsWindow.Activate();
                    return;
                }

                _settingsWindow = new SettingsWindow(this) { Owner = this };
                _settingsWindow.Closed += delegate { _settingsWindow = null; };
                _settingsWindow.Show();
            }
            catch (Exception exception)
            {
                Logger.Error(exception);
            }
        }

        private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            if (Settings.CloseToTheNotificationArea)
            {
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                App.Shutdown();
            }
        }

        /// <summary>
        /// Sets the keyboard focus to the smart cleanup button (used when restoring from tray)
        /// </summary>
        public void SetFocusToSmartCleanButton()
        {
            Keyboard.Focus(SmartCleanButton);
        }

        /// <summary>
        /// Saves the window placement on close
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Settings.WindowPositionSaved || Left != 0 || Top != 0)
                SaveWindowPlacement();

            if (_settingsWindow != null)
            {
                _settingsWindow.Close();
                _settingsWindow = null;
            }

            base.OnClosing(e);
        }

        /// <summary>
        /// Releases view model event subscriptions
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.MetricsUpdated -= OnMetricsUpdated;
                _viewModel.OnSmartCleanupCommandCompleted -= OnSmartCleanupCompleted;
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            base.OnClosed(e);
        }

        #endregion
    }
}
