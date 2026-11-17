using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Trace listener that writes diagnostic events to a rotating file under %ProgramData%\WinMemoryCleaner.
    /// Used for cleanup/monitoring diagnostics; per-second performance metrics are never logged.
    /// All failures are silent so logging can never break the application.
    /// </summary>
    public sealed class FileLogger : TraceListener
    {
        #region Fields

        private const long MaxLogSizeBytes = 4L * 1024 * 1024; // 4 MB before rotation
        private readonly object _sync = new object();
        private readonly string _path;
        private StreamWriter _writer;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="FileLogger"/> class.
        /// </summary>
        public FileLogger()
        {
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.App.Name);

                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                _path = Path.Combine(directory, Constants.App.Name + ".log");
            }
            catch
            {
                _path = null;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Writes a message with a timestamp prefix
        /// </summary>
        public override void Write(string message)
        {
            WriteEntry(message, false);
        }

        /// <summary>
        /// Writes a message with a timestamp prefix and a line break
        /// </summary>
        public override void WriteLine(string message)
        {
            WriteEntry(message, true);
        }

        private void WriteEntry(string message, bool lineBreak)
        {
            if (_path == null || string.IsNullOrEmpty(message))
                return;

            try
            {
                lock (_sync)
                {
                    EnsureWriter();

                    if (_writer == null)
                        return;

                    _writer.Write(string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss} ", DateTime.Now));
                    _writer.Write(message);

                    if (lineBreak)
                        _writer.WriteLine();

                    _writer.Flush();
                }
            }
            catch
            {
                // Logging must never throw
            }
        }

        private void EnsureWriter()
        {
            if (_writer != null)
                return;

            try
            {
                var info = new FileInfo(_path);

                if (info.Exists && info.Length > MaxLogSizeBytes)
                {
                    try
                    {
                        File.Delete(_path + ".old");
                        File.Move(_path, _path + ".old");
                    }
                    catch
                    {
                        // If rotation fails, append anyway
                    }
                }

                _writer = new StreamWriter(_path, true) { AutoFlush = false };
            }
            catch
            {
                _writer = null;
            }
        }

        /// <summary>
        /// Releases the underlying writer
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_sync)
                {
                    if (_writer != null)
                    {
                        try
                        {
                            _writer.Flush();
                            _writer.Close();
                        }
                        catch
                        {
                            // ignored
                        }

                        _writer = null;
                    }
                }
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
