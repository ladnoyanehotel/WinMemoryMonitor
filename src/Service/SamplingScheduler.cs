using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Single background scheduler that runs registered sampling callbacks at fixed intervals.
    /// Replaces multiple infinite ThreadPool loops: exactly one dedicated thread, waits on a
    /// cancellation token (never busy-spins), and isolates callback failures so one broken
    /// sampler cannot stop the others.
    /// </summary>
    public sealed class SamplingScheduler : IDisposable
    {
        #region Fields

        private readonly List<Registration> _registrations = new List<Registration>();
        private readonly object _sync = new object();
        private readonly Thread _thread;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private bool _disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SamplingScheduler"/> class and starts its background thread.
        /// </summary>
        public SamplingScheduler()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "WinMemoryCleaner.SamplingScheduler", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Registers a callback to be executed every <paramref name="intervalMs"/> milliseconds.
        /// </summary>
        /// <param name="callback">Callback to execute; must not throw (failures are logged and swallowed)</param>
        /// <param name="intervalMs">Interval in milliseconds; values below 250 are raised to 250</param>
        public void Register(Action callback, int intervalMs)
        {
            if (callback == null)
                throw new ArgumentNullException("callback");

            if (intervalMs < 250)
                intervalMs = 250;

            lock (_sync)
            {
                if (_disposed)
                    return;

                _registrations.Add(new Registration(callback, intervalMs));
            }
        }

        /// <summary>
        /// Schedules a one-shot callback after <paramref name="delayMs"/> milliseconds.
        /// </summary>
        /// <param name="delayMs">Delay in milliseconds</param>
        /// <param name="callback">Callback to execute once</param>
        public void ScheduleOnce(int delayMs, Action callback)
        {
            if (callback == null)
                throw new ArgumentNullException("callback");

            if (delayMs < 1)
                delayMs = 1;

            lock (_sync)
            {
                if (_disposed)
                    return;

                _registrations.Add(new Registration(callback, delayMs) { Once = true });
            }
        }

        private void Run()
        {
            var stopwatch = Stopwatch.StartNew();
            var due = new List<Registration>(4);

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                due.Clear();
                var wait = 1000;

                lock (_sync)
                {
                    if (_registrations.Count > 0)
                    {
                        var elapsed = stopwatch.ElapsedMilliseconds;
                        var earliest = long.MaxValue;

                        for (var i = _registrations.Count - 1; i >= 0; i--)
                        {
                            var registration = _registrations[i];
                            var remaining = registration.IntervalMs - (elapsed - registration.LastRunMs);

                            if (remaining <= 0)
                            {
                                due.Add(registration);
                                registration.LastRunMs = elapsed;

                                if (registration.Once)
                                    _registrations.RemoveAt(i);
                            }
                            else if (remaining < earliest)
                            {
                                earliest = remaining;
                            }
                        }

                        if (due.Count > 0 && earliest == long.MaxValue)
                            earliest = 1; // Something just ran; recompute deadlines immediately

                        if (earliest != long.MaxValue)
                            wait = (int)Math.Min(Math.Max(earliest, 1), 60000);
                    }
                }

                if (due.Count > 0)
                {
                    foreach (var registration in due)
                    {
                        try
                        {
                            registration.Callback();
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Sampling callback failed");
                        }
                    }

                    continue;
                }

                if (_cancellationTokenSource.Token.WaitHandle.WaitOne(wait))
                    break;
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Stops the scheduler and releases its resources.
        /// </summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
            }

            try
            {
                _cancellationTokenSource.Cancel();
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }

            try
            {
                _thread.Join(2000);
            }
            catch
            {
                // ignored
            }

            _cancellationTokenSource.Dispose();
        }

        #endregion

        #region Nested types

        private sealed class Registration
        {
            public readonly Action Callback;
            public readonly int IntervalMs;
            public long LastRunMs;
            public bool Once;

            public Registration(Action callback, int intervalMs)
            {
                Callback = callback;
                IntervalMs = intervalMs;
                LastRunMs = -intervalMs; // Run as soon as the scheduler starts
            }
        }

        #endregion
    }
}
