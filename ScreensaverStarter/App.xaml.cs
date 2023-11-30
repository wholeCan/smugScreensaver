using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace ScreensaverStarter
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private HwndSource winWPFContent;


        private void Application_Startup(object sender, StartupEventArgs e)
        {

            try
            {
                if (e == null || e.Args == null)
                {
                    throw new Exception("Andy sucks");
                }
                // Preview mode--display in little window in Screen Saver dialog
                // (Not invoked with Preview button, which runs Screen Saver in
                // normal /s mode).
                if (e.Args.Length >= 1)
                {
              
                    if (e.Args[0].ToLower().StartsWith("/p") || e.Args[0].ToLower().StartsWith("/s") || e.Args[0].ToLower().StartsWith("/c"))
                    {
                        string programFilesX86Path = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                        Console.WriteLine("Program Files (x86) Path: " + programFilesX86Path);


                        string programPath = programFilesX86Path + @"\andyScrSaver";

                        // Create a new process start info
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            WorkingDirectory = programPath,
                            FileName = programPath + @"\andyScrSaver.exe",
                            UseShellExecute = false,
                            Arguments = e.Args[0]
                        };

                        try
                        {
                            // Start the process
                            Process.Start(psi);
                            Application.Current.Shutdown();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"An error occurred: {ex.Message}");
                        }

                    }

          

                }
                /**
                 * If not a 'sanctioned mode', ie no params included - go into slideshow mode with controls.
                 * **/
                else
                {
                    // do nothing.

                }
            }
            catch (Exception ex)
            {
               

            }
        }

        /// <summary>
        /// Event that triggers when parent window is disposed--used when doing
        /// screen saver preview, so that we know when to exit.  If we didn't 
        /// do this, Task Manager would get a new .scr instance every time
        /// we opened Screen Saver dialog or switched dropdown to this saver.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void winWPFContent_Disposed(object sender, EventArgs e)
        {
          //  winSaver.Close();
        }
    }

}
