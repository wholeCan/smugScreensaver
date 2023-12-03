using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace andyScreenSaver
{
    internal class AppOpenCloseLogger
    {
        public static void logOpened()
        {
            var env = Path.GetTempPath();
            using (var sw1 = new StreamWriter(env + @"\smugmug.uptime.log", true))
            {
                sw1.WriteLine("Opened: " + DateTime.Now.ToShortDateString() + ", " + DateTime.Now.ToLongTimeString());
            }
        }
        public static void logClosed(string uptime)
        {
            var env = Path.GetTempPath(); 
            using (var sw1 = new StreamWriter(env + @"\smugmug.uptime.log", true))
            {
                sw1.WriteLine("Closed: " + DateTime.Now.ToShortDateString()+", " + DateTime.Now.ToLongTimeString() + ": uptime hours: "+ uptime);
            }
        }
    }
}
