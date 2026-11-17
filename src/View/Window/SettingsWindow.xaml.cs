using System;
using System.Windows;
using System.Windows.Input;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Small settings window opened from the main window chrome
    /// </summary>
    public partial class SettingsWindow : View
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsWindow"/> class.
        /// </summary>
        /// <param name="owner">Owner window</param>
        public SettingsWindow(Window owner)
        {
            InitializeComponent();

            var notificationService = DependencyInjection.Container.TryResolve<INotificationService>();
            DataContext = new SettingsViewModel(notificationService);

            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
        }

        #endregion

        #region Methods

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}
