using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Launches the isolated WinMemoryCleaner.Worker.exe process for native memory operations
    /// and enforces a hard timeout (12 seconds). When the worker does not finish in time it is
    /// killed and a timeout result is returned; the UI process itself never blocks on native calls.
    /// </summary>
    public class WorkerProcessRunner : IWorkerProcessRunner, IDisposable
    {
        #region Fields

        /// <summary>
        /// Hard timeout for a single native operation (10-15 s allowed by the specification)
        /// </summary>
        public const int TimeoutMs = 12000;

        private const string WorkerExecutableName = "WinMemoryCleaner.Worker.exe";

        private readonly object _sync = new object();
        private Process _currentProcess;
        private bool _disposed;

        #endregion

        #region Events

        /// <summary>
        /// Occurs when a worker run finishes (success, failure or timeout)
        /// </summary>
        public event Action<string, WorkerResult> OnCompleted;

        #endregion

        #region Methods

        /// <summary>
        /// Runs a single worker operation. Blocking; must be called from a background thread only.
        /// </summary>
        /// <param name="operation">Operation id, see <see cref="WorkerOperation"/></param>
        public WorkerResult Run(string operation)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, WorkerExecutableName);

                if (!File.Exists(path))
                    return Complete(operation, WorkerResult.FromFailure("Worker executable is missing"));

                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    // Test hook: "hang" maps to the worker's hidden --hang flag to exercise the timeout path
                    Arguments = operation == "hang" ? "--hang" : "--operation " + operation,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                Process process;

                try
                {
                    process = Process.Start(startInfo);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to start worker process");
                    return Complete(operation, WorkerResult.FromFailure("Failed to start worker: " + e.Message));
                }

                if (process == null)
                    return Complete(operation, WorkerResult.FromFailure("Failed to start worker"));

                lock (_sync)
                {
                    if (_disposed)
                    {
                        TryKill(process);
                        process.Dispose();
                        return Complete(operation, WorkerResult.FromFailure("Runner disposed"));
                    }

                    _currentProcess = process;
                }

                try
                {
                    // The worker produces at most one short line, so reading after the wait
                    // cannot deadlock on a full pipe.
                    string output;

                    if (process.WaitForExit(TimeoutMs))
                    {
                        output = process.StandardOutput.ReadToEnd().Trim();
                        stopwatch.Stop();

                        var result = WorkerResult.FromSuccess(process.ExitCode, process.Id, output, stopwatch.Elapsed);
                        Logger.Information(string.Format("Worker '{0}' (pid {1}) completed in {2} ms: {3}", operation, process.Id, stopwatch.ElapsedMilliseconds, output));

                        return Complete(operation, result);
                    }

                    stopwatch.Stop();
                    Logger.Warning(string.Format("Worker '{0}' (pid {1}) timed out after {2} ms; killing", operation, process.Id, TimeoutMs));
                    TryKill(process);

                    return Complete(operation, WorkerResult.FromTimeout(process.Id, stopwatch.Elapsed));
                }
                finally
                {
                    lock (_sync)
                    {
                        if (_currentProcess == process)
                            _currentProcess = null;
                    }

                    process.Dispose();
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Worker runner failed");
                return Complete(operation, WorkerResult.FromFailure("Worker runner failed: " + e.Message));
            }
        }

        private WorkerResult Complete(string operation, WorkerResult result)
        {
            var handler = OnCompleted;

            if (handler != null)
                handler(operation, result);

            return result;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();

                process.WaitForExit(3000);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to kill worker process");
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Kills a running worker so shutdown never leaves background processes behind.
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

            Process process;

            lock (_sync)
            {
                _disposed = true;
                process = _currentProcess;
                _currentProcess = null;
            }

            if (process != null)
            {
                TryKill(process);
                process.Dispose();
            }
        }

        #endregion
    }
}
