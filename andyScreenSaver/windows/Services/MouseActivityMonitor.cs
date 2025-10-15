using System;

namespace andyScreenSaver.windows.Services
{
    /// <summary>
    /// Monitors mouse activity to determine when to shutdown screensaver
    /// </summary>
    public class MouseActivityMonitor
    {
        private DateTime _lastMouseMove;
        private long _totalMouseMoves;
        private readonly long _maxMouseMoves;
        private readonly int _resetTimeMilliseconds;

        public DateTime LastMouseMove => _lastMouseMove;
        public long TotalMouseMoves => _totalMouseMoves;

        public MouseActivityMonitor(long maxMouseMoves = 100, int resetTimeMilliseconds = 500)
        {
            _maxMouseMoves = maxMouseMoves;
            _resetTimeMilliseconds = resetTimeMilliseconds;
            _lastMouseMove = DateTime.Now;
            _totalMouseMoves = 0;
        }

        /// <summary>
        /// Records a mouse move and returns true if shutdown threshold is exceeded
        /// </summary>
        public bool RecordMouseMove()
        {
            var resetTime = _lastMouseMove.AddMilliseconds(_resetTimeMilliseconds);

            if (DateTime.Now < resetTime)
            {
                _totalMouseMoves++;
                if (_totalMouseMoves > _maxMouseMoves)
                {
                    return true; // Threshold exceeded, should shutdown
                }
            }
            else
            {
                _totalMouseMoves = 0;
            }

            _lastMouseMove = DateTime.Now;
            return false;
        }

        /// <summary>
        /// Resets the mouse move counter
        /// </summary>
        public void Reset()
        {
            _lastMouseMove = DateTime.Now;
            _totalMouseMoves = 0;
        }

        /// <summary>
        /// Checks if enough time has passed to hide the cursor
        /// </summary>
        public bool ShouldHideCursor(int secondsToHide = 3)
        {
            DateTime laterTime = _lastMouseMove.AddSeconds(secondsToHide);
            return DateTime.Now > laterTime;
        }
    }
}
