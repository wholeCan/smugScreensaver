/** 
 * Original work by Andrew Holkan
 * Date: 2/1/2013
 * Contact info: aholkan@gmail.com
 * **/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
//using LibVLCSharp.Shared;
namespace andyScreenSaver
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Used to host WPF content in preview mode, attach HwndSource to parent Win32 window.
        private HwndSource winWPFContent;
        private Window1 winSaver;

        //DisableScreensaverClassSingleton disableScreensaverSingleton = DisableScreensaverClassSingleton.Instance;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
               // Core.Initialize(); // LibVLCSharp initialization
                if (ApplicationMutexSingleton.Instance.AlreadyRunning)
                {
                    Debug.WriteLine("Application already running!");
#if (DEBUG)  // mutex
                    MessageBox.Show("Debug: Shutting down - already running!");
#endif
                    Shutdown();
                    return;
                }
                if (e == null || e.Args == null)
                {
                    throw new Exception("Andy sucks");
                }
                // Preview mode--display in little window in Screen Saver dialog
                // (Not invoked with Preview button, which runs Screen Saver in
                // normal /s mode).
                if (e.Args.Length >= 1)
                {
                    if (e.Args[0].ToLower().StartsWith("/f"))
                    {//full screen mode, added by andy.
                        var win = new Window1();
                        win.Init();
                        win.setDimensions(333, 200);
                        win.disableActions();//ensures that moving mouse doesn't kill the app.
                        
                        win.WindowState = WindowState.Maximized;
                        win.WindowStyle = WindowStyle.None;
                        win.Show();
                    }
                    else if (e.Args[0].ToLower().StartsWith("/p"))
                    {
                        try
                        {
                            winSaver = new Window1();
                            winSaver.setDimensions(333, 200);
                            Int32 previewHandle = Convert.ToInt32(e.Args[1]);
                            var pPreviewHnd = new IntPtr(previewHandle);

                            var lpRect = new RECT();
                            var bGetRect = Win32API.GetClientRect(pPreviewHnd, ref lpRect);
                            var sourceParams = new HwndSourceParameters("sourceParams");

                            sourceParams.PositionX = 0;
                            sourceParams.PositionY = 0;
                            sourceParams.Height = lpRect.Bottom - lpRect.Top;

                            winSaver.setDimensions(sourceParams.Height, sourceParams.Width);
                            winSaver.Init();//just added this.
                            sourceParams.Width = lpRect.Right - lpRect.Left;
                            sourceParams.ParentWindow = pPreviewHnd;
                            sourceParams.WindowStyle = (int)(WindowStyles.WS_VISIBLE | WindowStyles.WS_CHILD | WindowStyles.WS_CLIPCHILDREN);

                            winWPFContent = new HwndSource(sourceParams);
                            winWPFContent.Disposed += new EventHandler(winWPFContent_Disposed);
                            winWPFContent.RootVisual = winSaver.grid1;
                        }
                        catch (Exception x)
                        {//likely an invalid handle, caused by shutting down the app before the connection is completely established.
                            System.Diagnostics.Debug.WriteLine(x.Message);
                            Shutdown();
                        }
                    }

                    // Normal screensaver mode.  Either screen saver kicked in normally,
                    // or was launched from Preview button
                    else if (e.Args[0].ToLower().StartsWith("/s"))
                    {
                        if (true)
                        {///original mechanism, to fill all screens.  this worked well until 7/2014 build.  after that, windows started
                         ///throwing exceptions when this code ran in screensaver mode.
                            bool doWriteLogs = false;
                            StreamWriter sw1 = null;
                            if (doWriteLogs)
                            {

                                var env = Path.GetTempPath();
                                sw1 = new StreamWriter(env + @"\smugmug.startup.log");
                            }
                            var wins = new List<Window1>();
                            var iter = 0;
                            foreach (var s in System.Windows.Forms.Screen.AllScreens)
                            {
                                var win1 = new Window1();
                                var workingArea = s.Bounds;
                                win1.WindowStyle = WindowStyle.None;
                                win1.setDimensions(workingArea.Height, workingArea.Width); //don't account for border height.
                                win1.Init();
                                wins.Add(win1);
                                wins.ElementAt(iter).Left = workingArea.X;
                                wins.ElementAt(iter).Width = workingArea.Width;
                                wins.ElementAt(iter).Top = workingArea.Top;
                                wins.ElementAt(iter).Height = workingArea.Height;
                                wins.ElementAt(iter).Show();
                                iter++;
                            }
                            if (doWriteLogs)
                            { sw1.Close(); }
                        }
                    }

                    // Config mode, launched from Settings button in screen saver dialog
                    else if (e.Args[0].ToLower().StartsWith("/c"))
                    {
                        var win = new SettingsWindow();
                        win.Show();
                    }

                }
                /**
                 * If not a 'sanctioned mode', ie no params included - go into slideshow mode with controls.
                 * **/
                else
                {//no parameters
                 //var win = new Window1();

                    //disableScreensaverSingleton.DisableScreenSaver();
                    var wins = new List<Window1>();
                    var iter = 0;
                    foreach (var s in System.Windows.Forms.Screen.AllScreens)
                    {
                        var win1 = new Window1();
                        var workingArea = s.Bounds;
                        win1.WindowStyle = WindowStyle.None;
                        win1.setDimensions(workingArea.Height, workingArea.Width); //don't account for border height.
                        win1.Init();
                        win1.disableActions();
                        wins.Add(win1);
                        wins.ElementAt(iter).Left = workingArea.X;
                        wins.ElementAt(iter).Width = workingArea.Width;
                        wins.ElementAt(iter).Top = workingArea.Top;
                        wins.ElementAt(iter).Height = workingArea.Height;
                        wins.ElementAt(iter).Show();

                        iter++;
                    }

                }
            }
            catch (Exception ex)
            {
                //var ln=ex.StackTrace.GetFrame(0).GetFileLineNumber();
                var loc = Path.GetTempPath();
                var exceptionLog = loc + @"\errlog.startup.smug." + DateTime.Now.ToShortDateString().Replace('/', '-') + ".txt";
                if (File.Exists(exceptionLog))
                    File.Delete(exceptionLog);
                using (var sw = new StreamWriter(exceptionLog))
                {

                    // Get stack trace for the exception with source file information
                    var st = new System.Diagnostics.StackTrace(ex, true);
                    // Get the top stack frame
                    var frame = st.GetFrame(0);
                    // Get the line number from the stack frame
                    var line = frame.GetFileLineNumber();

                    sw.WriteLine($"EX: {ex.ToString()}");
                    sw.WriteLine($"e: {e.ToString()}");
                    sw.WriteLine($"len: {e.Args.Length}");
                    foreach (var v in e.Args)
                        sw.WriteLine($"var: {v}");
                    sw.WriteLine($"Args: {e.Args.ToString()}");
                    sw.WriteLine($"Line: {line}");
                    sw.Close();
                }

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
            winSaver.Close();
        }
    }
}
