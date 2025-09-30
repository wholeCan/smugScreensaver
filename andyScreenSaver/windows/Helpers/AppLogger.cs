using System;
using System.Diagnostics;
using System.IO;

namespace andyScreenSaver.windows.Helpers
{
    internal static class AppLogger
    {
        public static void Log(string msg)
        {
            Debug.WriteLine($"Window: {DateTime.Now.ToLongTimeString()}: {msg}");
        }

        public static void LogError(Exception ex, string msg)
        {
            Log($"{DateTime.Now}: {msg}");
            try
            {
                var dir = Path.GetTempPath();
                using (var sw = new StreamWriter(
                    $"{dir}\\errlog.smug." + DateTime.Now.ToShortDateString().Replace('/', '-') + ".txt",
                    true))
                {
                    sw.WriteLine($"{DateTime.Now}: exception: {msg}");
                    if (ex?.StackTrace != null)
                    {
                        sw.WriteLine($"{DateTime.Now}: stack trace: {ex.StackTrace}");
                    }
                }
            }
            catch
            {
                Debug.WriteLine("Uh Oh! Error writing error file!");
            }
        }
    }
}
