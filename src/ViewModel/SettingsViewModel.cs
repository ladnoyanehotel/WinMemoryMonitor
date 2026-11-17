using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Settings View Model: a small set of useful options. Technical options from the
    /// old interface (memory areas, cleanup intervals, tray colors, exclusions) are gone.
    /// </summary>
    public class SettingsViewModel : ViewModel
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
        /// </summary>
        /// <param name="notificationService">Notification service</param>
        public SettingsViewModel(INotificationService notificationService)
            : base(notificationService)
        {
        }

        #endregion

        #region Properties

        public List<Language> Languages
        {
            get { return Localizer.Languages; }
        }

        public Language Language
        {
            get
            {
                return Localizer.Languages.Find(candidate => candidate != null && string.Equals(candidate.Name, Settings.Language, StringComparison.OrdinalIgnoreCase));
            }
            set
            {
                if (value == null)
                    return;

                Localizer.Language = value;
                RaisePropertyChanged(() => Language);
            }
        }

        public bool RunOnStartup
        {
            get { return Settings.RunOnStartup; }
            set { Settings.RunOnStartup = value; Settings.Save(); App.RunOnStartup(value); RaisePropertyChanged(() => RunOnStartup); }
        }

        public bool StartMinimized
        {
            get { return Settings.StartMinimized; }
            set { Settings.StartMinimized = value; Settings.Save(); RaisePropertyChanged(() => StartMinimized); }
        }

        public bool CloseToTheNotificationArea
        {
            get { return Settings.CloseToTheNotificationArea; }
            set { Settings.CloseToTheNotificationArea = value; Settings.Save(); RaisePropertyChanged(() => CloseToTheNotificationArea); }
        }

        public bool Notifications
        {
            get { return Settings.ShowOptimizationNotifications; }
            set { Settings.ShowOptimizationNotifications = value; Settings.Save(); RaisePropertyChanged(() => Notifications); }
        }

        public bool CloseAfterOptimization
        {
            get { return Settings.CloseAfterOptimization; }
            set { Settings.CloseAfterOptimization = value; Settings.Save(); RaisePropertyChanged(() => CloseAfterOptimization); }
        }

        public bool AlwaysOnTop
        {
            get { return Settings.AlwaysOnTop; }
            set { Settings.AlwaysOnTop = value; Settings.Save(); RaisePropertyChanged(() => AlwaysOnTop); }
        }

        public bool UseHotkey
        {
            get { return Settings.UseHotkey; }
            set
            {
                Settings.UseHotkey = value;
                Settings.Save();
                RaisePropertyChanged(() => UseHotkey);
                ReinitializeHotkey();
            }
        }

        public List<Key> Keys
        {
            get
            {
                var hotkeyService = DependencyInjection.Container.TryResolve<IHotkeyService>();

                return hotkeyService != null ? hotkeyService.Keys : new List<Key>();
            }
        }

        public Dictionary<ModifierKeys, string> Modifiers
        {
            get
            {
                var hotkeyService = DependencyInjection.Container.TryResolve<IHotkeyService>();

                return hotkeyService != null ? hotkeyService.Modifiers : new Dictionary<ModifierKeys, string>();
            }
        }

        public ModifierKeys OptimizationModifiers
        {
            get { return Settings.OptimizationModifiers; }
            set
            {
                Settings.OptimizationModifiers = value;
                Settings.Save();
                RaisePropertyChanged(() => OptimizationModifiers);
                ReinitializeHotkey();
            }
        }

        public Key OptimizationKey
        {
            get { return Settings.OptimizationKey; }
            set
            {
                Settings.OptimizationKey = value;
                Settings.Save();
                RaisePropertyChanged(() => OptimizationKey);
                ReinitializeHotkey();
            }
        }

        public string HotkeyDisplay
        {
            get { return new Hotkey(Settings.OptimizationModifiers, Settings.OptimizationKey).ToString(); }
        }

        #endregion

        #region Methods

        private static void ReinitializeHotkey()
        {
            var mainViewModel = DependencyInjection.Container.TryResolve<MainViewModel>();

            if (mainViewModel != null)
                mainViewModel.RegisterHotkey();
        }

        #endregion
    }
}
