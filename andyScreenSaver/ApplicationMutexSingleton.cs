using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace andyScreenSaver
{
  
    internal class ApplicationMutexSingleton
    {
        string mutexName = "AndysScreensaverApplication";
        private Mutex mutex = null;
        bool alreadyRunning = false;

        private static ApplicationMutexSingleton instance;
        private ApplicationMutexSingleton()
        {
            mutex = new Mutex(true, mutexName, out bool createdNew);
            {
                if (createdNew)
                {
                    alreadyRunning = false;
                    Debug.WriteLine("this is the new instance!");
                }
                else
                {
                    Debug.WriteLine("Another instance of the application is already running.");
#if (DEBUG)
                    MessageBox.Show("Debug: Shutting down - already running!");
#endif
                    alreadyRunning = true;
                }
            }
        }

        public bool AlreadyRunning
        {
            get
            {
                return alreadyRunning;
            }
        }
        ~ApplicationMutexSingleton()
        {
            mutex = null;
        }

        public static ApplicationMutexSingleton Instance
        {
            get
            {
                // If the instance is null, create a new instance
                if (instance == null)
                {
                    instance = new ApplicationMutexSingleton();
                }
                return instance;
            }
        }
    }
}
