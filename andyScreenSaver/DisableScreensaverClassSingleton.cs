using System;
using System.Runtime.InteropServices;

namespace andyScreenSaver
{
    /// <summary>
    /// Manages screensaver prevention using SetThreadExecutionState.
    /// This is cleaner than SystemParametersInfo because it:
    /// - Doesn't modify user's system settings permanently
    /// - Automatically resets when the app exits
    /// - Is the Windows-recommended approach for keeping display active
    /// </summary>
    internal class DisableScreensaverClassSingleton
    {
        [Flags]
        public enum EXECUTION_STATE : uint
        {
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        private static DisableScreensaverClassSingleton instance;
        private bool isActive = false;

        private DisableScreensaverClassSingleton()
        {
            isActive = false;
        }

        public static DisableScreensaverClassSingleton Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new DisableScreensaverClassSingleton();
                }
                return instance;
            }
        }

        /// <summary>
        /// Prevents the screensaver and display from turning off while the app runs.
        /// This is non-intrusive and automatically resets when the app exits.
        /// </summary>
        public void DisableScreenSaver()
        {
            if (!isActive)
            {
                SetThreadExecutionState(
                    EXECUTION_STATE.ES_CONTINUOUS |
                    EXECUTION_STATE.ES_DISPLAY_REQUIRED |
                    EXECUTION_STATE.ES_SYSTEM_REQUIRED);
                isActive = true;
            }
        }

        /// <summary>
        /// Re-enables normal screensaver behavior.
        /// </summary>
        public void EnableScreenSaver()
        {
            if (isActive)
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                isActive = false;
            }
        }

        ~DisableScreensaverClassSingleton()
        {
            EnableScreenSaver();
        }
    }
}
