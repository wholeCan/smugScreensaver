using System;
using System.Reflection;
using System.Text;
using System.Linq;

namespace SMEngine
{
    internal static class StatsFormatter
    {
        public static string Build(CSMEngine engine, bool showMenu)
        {
            var msg = new StringBuilder();
            var debugOn =
#if DEBUG
                true;
#else
                false;
#endif
            msg.AppendLine("Time: " + DateTime.Now.ToShortDateString() + ", " + DateTime.Now.ToLongTimeString());
            msg.AppendLine("Running since " +
                CSMEngine.TimeBooted.ToShortDateString() +
                " : " +
                CSMEngine.TimeBooted.ToShortTimeString());

            msg.AppendLine("Uptime: " + DateTime.Now.Subtract(CSMEngine.TimeBooted).ToString());

            lock (engine.ImageDictionary)
            {
                msg.AppendLine("Images: " + engine.ImageDictionary.Values.Count(v => v != null && !v.IsVideo));
                msg.AppendLine("Videos: " + engine.ImageDictionary.Values.Count(v => v != null && v.IsVideo));
            }
            lock (CSMEngine.AllAlbums)
            {
                msg.AppendLine("Albums: " + CSMEngine.AllAlbums.Count);
            }
            msg.AppendLine("Images shown: " + CSMEngine.ImageCounter);
            msg.AppendLine("Video muted: " + engine.isDefaultMute().ToString());
            msg.AppendLine("Images deduped: " + engine.PlayedImages.Count);
            msg.AppendLine("Queue depth: " + engine.qSize);
            msg.AppendLine("Image size: " + engine.Settings.quality);
            msg.AppendLine("Time between images: " + engine.getTimeSinceLast());
            msg.AppendLine("Exceptions raised: " + engine.ExceptionsRaised);
            msg.AppendLine("Reloaded albums: " + engine.RestartCounter + " times.");
            msg.AppendLine("Memory: " + System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024));
            msg.AppendLine("Peak memory: " + System.Diagnostics.Process.GetCurrentProcess().PeakPagedMemorySize64 / (1024 * 1024));
            msg.AppendLine("Peak virtual memory: " + System.Diagnostics.Process.GetCurrentProcess().PeakVirtualMemorySize64 / (1024 * 1024));
            msg.AppendLine("Schedule: " + engine.Settings.startTime.ToString() + " - " + engine.Settings.stopTime.ToString());
            msg.AppendLine("Version: " + (Assembly.GetEntryAssembly()?.GetName().Version).ToString());
            msg.AppendLine("Built: " + engine.RetrieveLinkerTimestamp());
            if (showMenu)
            {
                msg.AppendLine("Menu:");
                msg.AppendLine("\ts: show or hide stats");
                msg.AppendLine("\tw: toggle window controls");
                msg.AppendLine("\tr: reload library");
                msg.AppendLine("\tCtrl+U: upgrade app");
                msg.AppendLine("\tEnter: refresh all images");
                msg.AppendLine("\tp: pause slideshow");
                msg.AppendLine("\t<- or ->: show next photo");
                msg.AppendLine("\tESC or Q: exit program");
            }
            engine.LastImageRequested = DateTime.Now;
            return msg.ToString().TrimEnd();
        }
    }
}
