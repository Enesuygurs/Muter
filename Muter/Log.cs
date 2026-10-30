using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Muter
{
    // Small thread-safe file logger (%LOCALAPPDATA%\Muter\muter.log). It never throws, rotates at 1 MB and
    // suppresses identical warnings/errors repeated within a minute so polling failures cannot flood the disk.
    internal static class Log
    {
        private const long MaxFileSize = 1024 * 1024;
        private static readonly TimeSpan RepeatWindow = TimeSpan.FromMinutes(1);
        private static readonly object sync = new object();
        private static readonly Dictionary<string, DateTime> lastWritten = new Dictionary<string, DateTime>();
        private static string directory;

        public static string Directory
        {
            get
            {
                lock (sync)
                {
                    if (directory == null)
                    {
                        string overrideDir = Environment.GetEnvironmentVariable("MUTER_LOG_DIR");
                        directory = !string.IsNullOrEmpty(overrideDir)
                            ? overrideDir
                            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Muter");
                    }
                    return directory;
                }
            }
            set
            {
                lock (sync)
                {
                    directory = value;
                    lastWritten.Clear();
                }
            }
        }

        public static string FilePath => Path.Combine(Directory, "muter.log");

        public static void Info(string message) => Write("INFO", message, null);

        public static void Warn(string message, Exception exception = null) => Write("WARN", message, exception);

        public static void Error(string message, Exception exception = null) => Write("ERROR", message, exception);

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                string detail = exception == null ? string.Empty : " | " + exception.GetType().Name + ": " + exception.Message;
                string key = level + message + detail;
                DateTime now = DateTime.Now;

                lock (sync)
                {
                    // Only repeated problems are suppressed; every INFO line (state changes, toggles) is kept.
                    if (level != "INFO")
                    {
                        if (lastWritten.TryGetValue(key, out DateTime last) && now - last < RepeatWindow)
                            return;
                        lastWritten[key] = now;
                        if (lastWritten.Count > 500)
                            lastWritten.Clear();
                    }

                    var line = new StringBuilder();
                    line.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(level).Append("] ").Append(message).Append(detail);
                    if (exception != null && level == "ERROR")
                        line.AppendLine().Append(exception);
                    line.AppendLine();

                    string dir = Directory;
                    System.IO.Directory.CreateDirectory(dir);
                    string path = FilePath;
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxFileSize)
                    {
                        string old = Path.Combine(dir, "muter.old.log");
                        File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, line.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never take the application down.
            }
        }
    }
}
