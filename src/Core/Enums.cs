using System;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Enumerators
    /// </summary>
    public static class Enums
    {
        public static class Dialog
        {
            public enum Button
            {
                None,
                Yes,
                No
            }
        }

        public static class Icon
        {
            public enum Notification
            {
                None,
                Information,
                Warning,
                Error
            }
        }

        public static class Log
        {
            [Flags]
            public enum Levels
            {
                Debug = 1,
                Information = 2,
                Warning = 4,
                Error = 8
            }
        }

        public static class Memory
        {
            [Flags]
            public enum Areas
            {
                None = 0,
                CombinedPageList = 1,
                ModifiedFileCache = 2,
                ModifiedPageList = 4,
                RegistryCache = 8,
                StandbyList = 16,
                StandbyListLowPriority = 32,
                SystemFileCache = 64,
                WorkingSet = 128,
            }

            public static class Optimization
            {
                public enum Reason
                {
                    LowMemory,
                    Manual,
                    Schedule
                }
            }

            public enum Pressure
            {
                Normal,
                Warning,
                Critical
            }

            public enum Unit { B, KB, MB, GB, TB, PB, EB, ZB, YB }
        }

        public static class PerformanceMode
        {
            public enum State
            {
                Off,
                On,
                Limited,       // Active, but running on battery: power scheme is not changed
                Unavailable    // The system does not support switching power schemes
            }
        }

        public static class Status
        {
            public enum Level
            {
                Normal,
                Warning,
                Critical,
                Optimizing
            }

            public enum Reason
            {
                None,
                SystemNormal,
                ElevatedLoad,
                CpuLoad,
                MemoryLow,
                DiskLoad,
                MemoryPressure,
                Optimizing,
                CleanupResult,
                CleanupNotNeeded,
                CleanupTimeout,
                CleanupApps,
                PerformanceModeLimited
            }
        }

        public enum StartupType
        {
            App,
            Reset,
            Silent
        }

        public enum Theme
        {
            Dark,
            Light
        }
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member