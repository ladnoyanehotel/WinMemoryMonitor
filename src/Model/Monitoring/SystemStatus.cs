using System;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Result of the system status evaluation: a level plus a machine-readable reason with context
    /// </summary>
    public class SystemStatus
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SystemStatus"/> class.
        /// </summary>
        public SystemStatus()
        {
            Reason = Enums.Status.Reason.None;
            ProcessName = string.Empty;
        }

        public Enums.Status.Level Level { get; internal set; }
        public Enums.Status.Reason Reason { get; internal set; }

        /// <summary>
        /// When set, shown as the status text verbatim (used for transient cleanup result messages)
        /// </summary>
        public string TextOverride { get; internal set; }

        /// <summary>
        /// Process name that causes the load (top CPU/RAM process), when applicable
        /// </summary>
        public string ProcessName { get; internal set; }

        /// <summary>
        /// Additional numeric context depending on the reason (e.g. CPU %, latency ms, GB used)
        /// </summary>
        public double ContextValue { get; internal set; }

        /// <summary>
        /// Second context value depending on the reason (e.g. RAM GB used by the top process)
        /// </summary>
        public double ContextValue2 { get; internal set; }

        /// <summary>
        /// Creates a normal status
        /// </summary>
        public static SystemStatus Normal()
        {
            return new SystemStatus { Level = Enums.Status.Level.Normal, Reason = Enums.Status.Reason.SystemNormal };
        }

        /// <summary>
        /// Creates an optimizing status
        /// </summary>
        public static SystemStatus Optimizing()
        {
            return new SystemStatus { Level = Enums.Status.Level.Optimizing, Reason = Enums.Status.Reason.Optimizing };
        }
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
