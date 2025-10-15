using System;

namespace andyScreenSaver.windows.Services
{
    /// <summary>
    /// Manages screensaver state including pause, stats display, and screensaver mode
    /// </summary>
    public class ScreensaverStateManager
    {
        private bool _isPaused;
        private bool _statsEnabled;
        private bool _screensaverModeDisabled;

        public bool IsPaused
        {
            get => _isPaused;
            set => _isPaused = value;
        }

        public bool StatsEnabled
        {
            get => _statsEnabled;
            set => _statsEnabled = value;
        }

        public bool ScreensaverModeDisabled
        {
            get => _screensaverModeDisabled;
            set => _screensaverModeDisabled = value;
        }

        public void TogglePause()
        {
            _isPaused = !_isPaused;
        }

        public void ToggleStats()
        {
            _statsEnabled = !_statsEnabled;
        }
    }
}
