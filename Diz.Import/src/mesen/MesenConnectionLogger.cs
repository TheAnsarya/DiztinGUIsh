using System;
using System.IO;
using System.Linq;

namespace Diz.Import.Mesen
{
    /// <summary>
    /// Dedicated file logger for Mesen2 live trace connection diagnostics.
    /// Automatically manages log file rotation and cleanup.
    /// </summary>
    public static class MesenConnectionLogger
    {
        private static readonly object _lockObj = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiztinGUIsh",
            "Logs"
        );
        
        private static string CurrentLogFile => Path.Combine(
            LogDirectory, 
            $"mesen_connection_{DateTime.Now:yyyy-MM-dd}.log"
        );

        static MesenConnectionLogger()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                CleanupOldLogs();
                
                // Write startup banner
                WriteLog("=============================================================");
                WriteLog($"DiztinGUIsh Mesen2 Connection Logger Initialized");
                WriteLog($"Session started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                WriteLog($"Log directory: {LogDirectory}");
                WriteLog("=============================================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGGER ERROR] Failed to initialize file logging: {ex.Message}");
            }
        }

        /// <summary>
        /// Log with specific category prefix (CLIENT, CONTROLLER, etc.)
        /// </summary>
        public static void Log(string category, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logLine = $"[{timestamp}] [{category}] {message}";
            
            Console.WriteLine(logLine);
            WriteLog(logLine);
        }

        private static void WriteLog(string line)
        {
            try
            {
                lock (_lockObj)
                {
                    File.AppendAllText(CurrentLogFile, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGGER ERROR] Failed to write to log file: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete log files older than 30 days.
        /// </summary>
        private static void CleanupOldLogs()
        {
            try
            {
                if (!Directory.Exists(LogDirectory))
                    return;

                var cutoffDate = DateTime.Now.AddDays(-30);
                var oldLogs = Directory.GetFiles(LogDirectory, "mesen_connection_*.log")
                    .Where(f => File.GetCreationTime(f) < cutoffDate)
                    .ToArray();

                foreach (var oldLog in oldLogs)
                {
                    try
                    {
                        File.Delete(oldLog);
                        Console.WriteLine($"[LOGGER] Deleted old log file: {Path.GetFileName(oldLog)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LOGGER WARNING] Failed to delete {oldLog}: {ex.Message}");
                    }
                }
                
                if (oldLogs.Length > 0)
                {
                    Console.WriteLine($"[LOGGER] Cleanup complete: removed {oldLogs.Length} log file(s) older than 30 days");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGGER ERROR] Log cleanup failed: {ex.Message}");
            }
        }
    }
}
