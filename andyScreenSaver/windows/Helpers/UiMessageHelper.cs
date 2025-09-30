using System;
using System.Windows;
using System.Windows.Controls;

namespace andyScreenSaver.windows.Helpers
{
    internal static class UiMessageHelper
    {
        public static void ShowMessage(ContentControl setupRequired, bool isPaused, string? msg, bool showStatsIsSet)
        {
            if (setupRequired == null) return;

            setupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
            {
                // Pause banner handling
                if (isPaused)
                {
                    if (string.IsNullOrEmpty(msg))
                    {
                        msg = "Paused";
                    }
                    else
                    {
                        msg = "Paused" + "\n" + msg;
                    }
                }

                // If nothing to show, hide
                if (string.IsNullOrEmpty(msg))
                {
                    setupRequired.Visibility = Visibility.Hidden;
                    return;
                }

                // Stats banner formatting (kept consistent with original logic)
                if (showStatsIsSet)
                {
                    if (!isPaused)
                    {
                        msg = "Paused: " + isPaused.ToString() + "\n" + msg;
                    }
                }

                setupRequired.Visibility = Visibility.Visible;
                setupRequired.Content = msg;
            }));
        }
    }
}
