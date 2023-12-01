using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace andyScreenSaver
{
    internal class DisableScreensaverClassSingleton
    {
        const int SPI_SETSCREENSAVEACTIVE = 0x0011;
        bool screensaverDisabled = false;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SystemParametersInfo(int uiAction, int uiParam, ref int pvParam, int fWinIni);

        private static DisableScreensaverClassSingleton instance;
        private DisableScreensaverClassSingleton()
        {
            screensaverDisabled = false;//start up in normal mode, and require activation.       
        }
        public static DisableScreensaverClassSingleton Instance
        {
            get
            {
                // If the instance is null, create a new instance
                if (instance == null)
                {
                    instance = new DisableScreensaverClassSingleton();
                }
                return instance;
            }
        }

            public void DisableScreenSaver()
            {
                int nullVar = 0;
            
                SystemParametersInfo(SPI_SETSCREENSAVEACTIVE, 0, ref nullVar, 0);
                screensaverDisabled = true;
        }

            public void EnableScreenSaver()
            {
                if (instance != null)
                {
                    int restoreValue = 1; // You can set it to 1 to enable the screen saver back
                    SystemParametersInfo(SPI_SETSCREENSAVEACTIVE, 0, ref restoreValue, 0);
                }
            }
        ~DisableScreensaverClassSingleton ()
        {
            EnableScreenSaver();
        }

     
    }
}
