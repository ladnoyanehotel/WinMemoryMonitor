using System;

namespace WinMemoryCleaner
{
    /// <summary>
    /// INotificationService
    /// </summary>
    public interface INotificationService : IDisposable
    {
        /// <summary>
        /// Initializes
        /// </summary>
        void Initialize();

        /// <summary>
        /// Show/Hide Loading
        /// </summary>
        /// <param name="running">True (ON) / False (OFF)</param>
        void Loading(bool running);

        /// <summary>
        /// Displays a Notification
        /// </summary>
        /// <param name="message">The text</param>
        /// <param name="title">The title</param>
        /// <param name="timeout">The time period, in seconds</param>
        /// <param name="icon">The icon</param>
        void Notify(string message, string title = null, int timeout = 5, Enums.Icon.Notification icon = Enums.Icon.Notification.None);

        /// <summary>
        /// Updates the tray icon and tooltip from the current system status
        /// </summary>
        /// <param name="level">System status level (drives the hardcoded icon color)</param>
        /// <param name="snapshot">Latest metrics snapshot</param>
        /// <param name="statusText">Localized status text for the tooltip</param>
        void Update(Enums.Status.Level level, SystemMetricsSnapshot snapshot, string statusText);
    }
}
