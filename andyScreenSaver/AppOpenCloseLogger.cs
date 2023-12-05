using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;

namespace andyScreenSaver
{
    internal class AppOpenCloseLogger
    {

        private static void cleanupFile()
        {
            try
            {
                if (File.Exists(LogFilename))
                {
                    FileInfo fileInfo = new FileInfo(LogFilename);
                    var fileSize = fileInfo.Length;
                    var maxFileSizeMB = 2;
                    if (fileSize > (1024 * 1024 * maxFileSizeMB))
                    {
                        File.Delete(LogFilename);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("cleanupFile: " + ex.Message);
            }
        }

        private static string LogFilename
        {
            get
            {
                var env = Path.GetTempPath();
                return env + @"\smugmug.uptime.log";
            }
        }
        public static void logOpened()
        {
            cleanupFile();
            using (var sw1 = new StreamWriter(LogFilename, true))
            {
                sw1.WriteLine("Opened: " + DateTime.Now.ToShortDateString() + ", " + DateTime.Now.ToLongTimeString());
            }
        }
        public static void logClosed(string uptime, string stats)
        {
            using (var sw1 = new StreamWriter(LogFilename, true))
            {
                sw1.WriteLine("Closing Stats: " + stats);
                sw1.WriteLine("Closed: " + DateTime.Now.ToShortDateString()+", " + DateTime.Now.ToLongTimeString() + ": uptime hours: "+ uptime);
                
            }
        }

        internal static void uptimeCheckpoint(string uptime, string stats)
        {
            using (var sw1 = new StreamWriter(LogFilename, true))
            {
                sw1.WriteLine("Uptime stats: " + stats);
                sw1.WriteLine("Uptime: " + DateTime.Now.ToShortDateString() + ", " + DateTime.Now.ToLongTimeString() + ": uptime hours: " + uptime);
            }
        }
    }
}
