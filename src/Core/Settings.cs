using Microsoft.Win32;
using System;
using System.Globalization;
using System.Windows.Input;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    public static class Settings
    {
        private static readonly CultureInfo _culture = new CultureInfo(Constants.Windows.Locale.Name.English);

        #region Constructors

        static Settings()
        {
            Load();
        }

        #endregion

        #region Properties

        public static bool AlwaysOnTop { get; set; }


        public static bool CloseAfterOptimization { get; set; }

        public static bool CloseToTheNotificationArea { get; set; }

        public static bool CompactMode { get; set; }

        public static bool CreateStartMenuShortcut { get; set; }

        public static string Language { get; set; }

        public static Key OptimizationKey { get; set; }

        public static ModifierKeys OptimizationModifiers { get; set; }

        /// <summary>
        /// Performance Mode crash-recovery flag: true when the mode was active and the previous
        /// power scheme is stored in <see cref="PreviousPowerScheme" />. Cleared on clean restore.
        /// </summary>
        public static bool PerformanceModeActive { get; set; }

        /// <summary>
        /// Power scheme GUID (string form) to restore when Performance Mode is disabled
        /// </summary>
        public static string PreviousPowerScheme { get; set; }

        public static bool RunOnStartup { get; set; }

        public static bool ShowOptimizationNotifications { get; set; }

        public static bool StartMinimized { get; set; }

        public static bool UseHotkey { get; set; }

        public static double WindowLeft { get; set; }

        public static double WindowTop { get; set; }

        public static bool WindowPositionSaved { get; set; }

        #endregion

        #region Methods

        private static void Load(bool loadUserValues = true)
        {
            // Default values
            AlwaysOnTop = false;
            CloseAfterOptimization = false;
            CloseToTheNotificationArea = false;
            CompactMode = false;
            CreateStartMenuShortcut = true;
            Language = Constants.Windows.Locale.Name.English;
            OptimizationKey = Key.M;
            OptimizationModifiers = ModifierKeys.Control | ModifierKeys.Shift;
            PerformanceModeActive = false;
            PreviousPowerScheme = string.Empty;
            RunOnStartup = false;
            ShowOptimizationNotifications = true;
            StartMinimized = false;
            UseHotkey = false;
            WindowLeft = double.NaN;
            WindowTop = double.NaN;
            WindowPositionSaved = false;

            // User values. Unknown/obsolete registry values from older versions are simply ignored.
            try
            {
                if (!loadUserValues)
                    return;

                using (var key = Registry.LocalMachine.OpenSubKey(Constants.App.Registry.Key.Settings))
                {
                    if (key != null)
                    {
                        AlwaysOnTop = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => AlwaysOnTop), AlwaysOnTop), _culture);
                        CloseAfterOptimization = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => CloseAfterOptimization), CloseAfterOptimization), _culture);
                        CloseToTheNotificationArea = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => CloseToTheNotificationArea), CloseToTheNotificationArea), _culture);
                        CompactMode = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => CompactMode), CompactMode), _culture);
                        CreateStartMenuShortcut = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => CreateStartMenuShortcut), CreateStartMenuShortcut), _culture);
                        Language = Convert.ToString(key.GetValue(Helper.NameOf(() => Language), Language), CultureInfo.InvariantCulture);

                        Key optimizationKey;

                        if (Enum.TryParse(Convert.ToString(key.GetValue(Helper.NameOf(() => OptimizationKey), OptimizationKey), _culture), out optimizationKey) && optimizationKey.IsValid())
                            OptimizationKey = optimizationKey;

                        ModifierKeys optimizationModifiers;

                        if (Enum.TryParse(Convert.ToString(key.GetValue(Helper.NameOf(() => OptimizationModifiers), OptimizationModifiers), _culture), out optimizationModifiers) && optimizationModifiers.IsValid())
                            OptimizationModifiers = optimizationModifiers;

                        PerformanceModeActive = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => PerformanceModeActive), PerformanceModeActive), _culture);
                        PreviousPowerScheme = Convert.ToString(key.GetValue(Helper.NameOf(() => PreviousPowerScheme), PreviousPowerScheme), CultureInfo.InvariantCulture);
                        RunOnStartup = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => RunOnStartup), RunOnStartup), _culture);
                        ShowOptimizationNotifications = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => ShowOptimizationNotifications), ShowOptimizationNotifications), _culture);
                        StartMinimized = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => StartMinimized), StartMinimized), _culture);
                        UseHotkey = Convert.ToBoolean(key.GetValue(Helper.NameOf(() => UseHotkey), UseHotkey), _culture);

                        var windowLeft = Convert.ToDouble(key.GetValue(Helper.NameOf(() => WindowLeft), double.NaN), _culture);
                        var windowTop = Convert.ToDouble(key.GetValue(Helper.NameOf(() => WindowTop), double.NaN), _culture);

                        if (!double.IsNaN(windowLeft) && !double.IsNaN(windowTop))
                        {
                            WindowLeft = windowLeft;
                            WindowTop = windowTop;
                            WindowPositionSaved = true;
                        }
                    }
                    else
                    {
                        // Smart language setter for the first run
                        var culture = CultureInfo.CurrentCulture;

                        do
                        {
                            if (Localizer.LanguageExists(culture.Name))
                            {
                                Language = culture.Name;
                                break;
                            }

                            culture = culture.Parent;
                        }
                        while (culture.LCID != CultureInfo.InvariantCulture.LCID);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        public static void Reset(bool keepLanguage = false)
        {
            var language = Language;

            Load(false);

            if (keepLanguage)
                Language = language;

            Save();
        }

        public static void Save()
        {
            try
            {
                using (var key = Registry.LocalMachine.CreateSubKey(Constants.App.Registry.Key.Settings))
                {
                    if (key != null)
                    {
                        key.SetValue(Helper.NameOf(() => AlwaysOnTop), AlwaysOnTop ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => CloseAfterOptimization), CloseAfterOptimization ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => CloseToTheNotificationArea), CloseToTheNotificationArea ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => CompactMode), CompactMode ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => CreateStartMenuShortcut), CreateStartMenuShortcut ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => Language), Language);
                        key.SetValue(Helper.NameOf(() => OptimizationKey), (int)OptimizationKey);
                        key.SetValue(Helper.NameOf(() => OptimizationModifiers), (int)OptimizationModifiers);
                        key.SetValue(Helper.NameOf(() => PerformanceModeActive), PerformanceModeActive ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => PreviousPowerScheme), PreviousPowerScheme ?? string.Empty);
                        key.SetValue(Helper.NameOf(() => RunOnStartup), RunOnStartup ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => ShowOptimizationNotifications), ShowOptimizationNotifications ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => StartMinimized), StartMinimized ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => UseHotkey), UseHotkey ? 1 : 0);
                        key.SetValue(Helper.NameOf(() => WindowLeft), WindowLeft);
                        key.SetValue(Helper.NameOf(() => WindowTop), WindowTop);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        #endregion
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
