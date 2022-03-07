/** 
 * Original work by Andrew Holkan
 * Date: 2/1/2013
 * Contact info: aholkan@gmail.com
 * 
 * 5/2018:  Updating API
 *  Adding caption to image
 *  allowing screensaver to timeout after set time.
 *  
 *  9/8/2019:  pretty stable, doing so me code cleanup.
 *  
 *  2/26/2022: major refactor of smEngine and everything else to upgrade to smugmug 2.0 api
 * **/

//using Quartz;
//using Quartz.Impl;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using static SMEngine.CSMEngine;

namespace andyScreenSaver
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    /// 
    public class TaskScheduler
    {
        private static TaskScheduler _instance;
        private List<Timer> timers = new List<Timer>();

        private TaskScheduler() { }

        public static TaskScheduler Instance => _instance ?? (_instance = new TaskScheduler());

        public void ScheduleTask(int hour, int min, double intervalInHour, Action task)
        {
            DateTime now = DateTime.Now;
            DateTime firstRun = new DateTime(now.Year, now.Month, now.Day, hour, min, 0, 0);
            if (now > firstRun)
            {
                firstRun = firstRun.AddDays(1);
            }

            TimeSpan timeToGo = firstRun - now;
            if (timeToGo <= TimeSpan.Zero)
            {
                timeToGo = TimeSpan.Zero;
            }

            var timer = new Timer(x =>
            {
                task.Invoke();
            }, null, timeToGo, TimeSpan.FromHours(intervalInHour));

            timers.Add(timer);
        }
    }
    public partial class Window1 : Window
    {
        private Vector3D zoomDelta;
        private int myHeight = 0;
        private int myWidth = 0;
        const bool doSmartStart = true;
        public void setDimensions(int _myHeight, int _myWidth)
        {
            myHeight = _myHeight;
            myWidth = _myWidth;
        }
        private SMEngine.CSMEngine _engine = null;
        ThreadStart ts = null;
        Thread t = null;
        static bool running = true;
        bool actionsDisabled = false;
        int gridWidth = 5;  //these are replaced by setting menu.
        int gridHeight = 4;
        int borderWidth = 5;//see config file for setting.
        public void disableActions()
        {
            actionsDisabled = true;
            Topmost = false;
            Cursor = Cursors.Arrow;
        }

        private BitmapImage Bitmap2BitmapImage(System.Drawing.Bitmap bitmap)
        {
            BitmapImage bitmapImage = null;
            try
            {
                var memory = new MemoryStream();
                var b = new Bitmap(bitmap);
                b.Save(memory, ImageFormat.Png);
                memory.Position = 0;
                memory.Seek(0, SeekOrigin.Begin);
                bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                memory.Close();
            }
            catch (Exception ex)
            {
                LogError(ex.Message);
            }
            return bitmapImage;
        }

        private void hideSetup()
        {
            SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    SetupRequired.Visibility = System.Windows.Visibility.Hidden;
                }));
        }
        private DateTime lastUpdate = DateTime.Now;

        private listManager lm;
        Color InvertMeAColour(Color ColourToInvert)
        {
            const int RGBMAX = 255;
            return Color.FromArgb(RGBMAX - ColourToInvert.R,
              RGBMAX - ColourToInvert.G, RGBMAX - ColourToInvert.B);
        }

        private Color getAverageColor(Bitmap tmp)
        {
            try
            {
                var bm = new Bitmap(tmp);
                var srcData = bm.LockBits(
                 new Rectangle(0, 0, bm.Width, bm.Height),
                 ImageLockMode.ReadOnly,
                 PixelFormat.Format32bppArgb);

                var stride = srcData.Stride;

                var Scan0 = srcData.Scan0;

                var totals = new long[] { 0, 0, 0 };

                var width = bm.Width;
                var height = bm.Height;

                unsafe
                {
                    byte* p = (byte*)(void*)Scan0;

                    //this logic is probably not really relevant since  I switchted to cropping the image.  leaving it in because it works ok.


                    for (int y = 0; y < height / 3; y++)  //note, dividing by two, because i want the top left region.
                    {
                        for (int x = 0; x < width / 2; x++)  //note, dividing by two, because i want the top left region.
                        {
                            for (int color = 0; color < 3; color++)
                            {
                                int idx = (y * stride) + x * 4 + color;

                                totals[color] += p[idx];
                            }
                        }
                    }
                }


                var avgB = (int)totals[0] / (width * height);
                var avgG = (int)totals[1] / (width * height);
                var avgR = (int)totals[2] / (width * height);

                var myColor = Color.FromArgb(avgR, avgB, avgG);
                var measuredColor = ((float)avgR * 0.299 + (float)avgG * 0.587 + (float)avgB * 0.114);
                if (measuredColor > 35.0)  //35 works pretty ok.
                    return Color.Black;
                else
                    return Color.White;
            }
            catch (Exception ex)
            {
                LogError(ex.Message);
                return Color.Black;
            }
        }

        private Bitmap getupperLeftCornerImage(Bitmap original)
        {
            Bitmap bmpImage = new Bitmap(original);
            var cropArea = new Rectangle(0, 0, (int)original.Height / 3, (int)original.Width / 3);
            return bmpImage.Clone(cropArea, bmpImage.PixelFormat);
            //tmp = croppedImage;//remove this!

        }
        private void imageAddCaption(string text, ref Bitmap tmp)
        {
            //let's see if we can a caption on the bitmap.
            var firstLocation = new PointF(10f, 10f);
            try
            {
                using (var graphics = Graphics.FromImage(tmp))
                {
                    var configPenSize = 8;
                    int.TryParse(ConfigurationSettings.AppSettings["captionPenSize"], out configPenSize);

                    var fontSize = configPenSize;
                    var croppedImage = getupperLeftCornerImage(tmp);


                    var penColor = new SolidBrush(getAverageColor(croppedImage));// System.Drawing.Brushes.White;
                    if (tmp.HorizontalResolution < 150)
                    {
                        fontSize = fontSize + 7;
                    }
                    if (tmp.HorizontalResolution < 80)
                    {
                        fontSize = fontSize + 19;
                    }
                    using (var arialFont = new Font("Arial", fontSize))
                    {
                        graphics.DrawString(text, arialFont, penColor, firstLocation);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex.Message);
            }
        }

        private void shuffleImages()
        {
            var tmp1 = hStack1.Children[0];
            hStack1.Children.RemoveAt(0);
            hStack1.Children.Add(tmp1);
        }

        bool statsEnabled = false;

        private void logMsg(string msg)
        {
            Debug.WriteLine("Window: " + DateTime.Now.ToLongTimeString() + ": " + msg);
        }

        private void LogError(String msg)
        {
            // Console.WriteLine(msg);
            logMsg($"{DateTime.Now}: {msg}");
            lock (this)
            {
                try
                {
                    var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    using (var sw = new StreamWriter(
                        $"{dir}\\errlog.smug." + DateTime.Now.ToShortDateString().Replace('/', '-') + ".txt",
                        true)
                        )
                    {
                        sw.WriteLine($"{DateTime.Now}: exception: {msg}");
                        sw.Close();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Uh Oh! Error writing error file!");
                }
            }
        }

      
        private void updateImage()
        {
            
            hStack1.Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    //resuze each of the images to fit screen.
                    foreach (var v in hStack1.Children)
                    {
                        foreach (Border borderImage in (v as StackPanel).Children)
                        {
                            var image = borderImage.Child as rotatableImage;
                            image.Height = myHeight / gridHeight - (100 / Math.Pow(2, gridHeight)); //161; 
                        }
                    }
                    if (_engine.screensaverExpired())
                    {
                        showMsg("service is shut down - press left or right arrow to wake up.");
                    }
                }));
            var run = false;
            ImageSet image =null;
            
            var counter = 0;
            var blackImagePlaced = false;

            while (image == null && counter < 1)
            {//if image isn't ready, wait for it.
                image = _engine.getImage();
                counter++;//allow it to die.
                if (image == null)
                {
                    if (counter % 100 == 0)
                    {
                        hideSetup();
                    }
                }
                else {
                    
                    //Thread.Sleep(100);
                }
            }
            if (_engine.getLogin().login == "")
            {
                SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    SetupRequired.Visibility = System.Windows.Visibility.Visible;
                    SetupRequired.Content = "Setup required!  Go to the screensaver menu.";
                }));
            }
            else
            {

                if (image == null)//image has failed for some reason.
                {
                    SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
                    {
                        SetupRequired.Visibility = System.Windows.Visibility.Visible;
                        SetupRequired.Content = "No data presently available, trying again...";
                        shuffleImages();

                        if (_engine.warm())
                        {
                            if (image != null)
                            {
                                image.b = _engine.getBlackImagePixel();
                                blackImagePlaced = true;
                            }
                        }

                    }));
                }
                else
                {//putting this in the else, because the blackImagePlaced is set in another thread and creates a race condition.
                    //  the red text disappears after resetting network connection, when really i want it to show up.
                    if (!blackImagePlaced && !_engine.screensaverExpired())
                    {
                        //todo: add switch here controlled by hotkey
                        if (statsEnabled)
                        { showMsg(_engine.getRuntimeStatsInfo()); }
                        else
                        { showMsg(""); }
                    }
                }
                hStack1.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate ()
                {
                    setImage(ref run, ref image);
                }));
            }
            if (run && !_engine.settings.showInfo && !blackImagePlaced)
            {
                SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    SetupRequired.Visibility = System.Windows.Visibility.Collapsed;
                }));
            }
        }

        private void setImage(ref bool run, ref ImageSet s)
        {
            try
            {
                if (s != null)
                {
                    run = true;
                    var maxTotalCells = Convert.ToInt32(gridWidth) * Convert.ToInt32(gridHeight);
                    var beginIndex = 0;
                    var rIndex = new Random().Next(beginIndex, maxTotalCells);
                    var randWidth = rIndex % gridWidth;
                    var randHeight = rIndex / gridWidth;
                    while (lm.isInList(new Tuple<int, int>(randWidth, randHeight)))
                    {
                        rIndex = new Random().Next(beginIndex, maxTotalCells);
                        randWidth = rIndex % gridWidth;
                        randHeight = rIndex / gridWidth;

                    }
                    Bitmap bmyImage2;
                    if (s != null && s.b != null)
                    {
                        bmyImage2 = s.b;
                        if (_engine.settings.showInfo)
                        {
                            setImageCaption(ref s, ref bmyImage2, randWidth, randHeight);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
            }
        }
        private void setImageCaption(ref SMEngine.CSMEngine.ImageSet s, ref Bitmap bmyImage2, int randWidth, int randHeight)
        {
            try
            {
                var d = DateTime.Now;
                var caption = //s.MyDate.ToShortDateString() 
                    s.CAtegory 
                    + ": "+ s.albumTitle;
                if (s.caption != "" && !s.caption.Contains("OLYMPUS"))
                {
                    caption += ": " + s.caption;
                }
                imageAddCaption(caption, ref bmyImage2); 
            }
            catch (Exception ex)
            {
                LogError($"Problem setting caption {ex.Message}");
            }
            var image = ((hStack1.Children[randWidth] as StackPanel).Children[randHeight] as Border).Child as rotatableImage;

            image.Height = /*this.Height*/ myHeight / gridHeight - (borderWidth / gridHeight); //161; 

            (image as System.Windows.Controls.Image).Source = Bitmap2BitmapImage(s.b);

            lm.addToList(new Tuple<int, int>(randWidth, randHeight));
            var imageIndex = (int)(randWidth + (randHeight * (gridWidth)));
            if (imageCounterArray[imageIndex] == 0)
            {
                try
                {
                    var tmp = new Bitmap(bmyImage2);
                    var fileName = getImageStorageLoc() + @"\" + imageIndex + @".jpg";
                    if (File.Exists(fileName) && doSmartStart)
                    {
                        File.Delete(fileName);
                    }
                    if (doSmartStart)
                    { tmp.Save(fileName, ImageFormat.Jpeg); }
                }
                catch (Exception ex)
                {
                    LogError($"Problem setting image {ex.Message}");
                }
                imageCounterArray[imageIndex]++;
            }
        }

        private void run()
        {
            running = true;
            while (running)
            {
                var runDelta = DateTime.Now - lastUpdate;
//                if (Convert.ToInt32(runDelta.TotalSeconds) >= _engine.settings.speed_s)
                {
                    updateImage();
                    

                }
                var millisecondsSinceLastRun = DateTime.Now.Subtract(lastUpdate).TotalMilliseconds;
                var timeToSleep = _engine.settings.speed_s * 1000 - millisecondsSinceLastRun;
                if (timeToSleep > 0)
                {
                    logMsg("sleeping for " + timeToSleep + " milliseconds");
                    Thread.Sleep((Int32)timeToSleep);
                }
                lastUpdate = DateTime.Now;

            }
        }

        Cursor myCursor;

        private void loginSmugmug()
        {
            _engine.login(_engine.getCode());
            ts = new ThreadStart(run);
            t = new Thread(ts);
            t.IsBackground = true;
            t.Start();

        }
        Task task = null;
        private void initEngine(bool? forceStart = false)
        {
            //get dimensions
            var w = System.Windows.SystemParameters.WorkArea.Width;
            var h = System.Windows.SystemParameters.WorkArea.Height;
            
            if (_engine == null || forceStart == true)
            {
                
                _engine = new SMEngine.CSMEngine();
                _engine.setScreenDimensions(w, h);
                {
                    gridHeight = _engine.settings.gridHeight;
                    gridWidth = _engine.settings.gridWidth;

                    _engine.fireException += showException;
                    try
                    {
                        task = new Task(() => { loginSmugmug(); });
                        task.Start();
                    }
                    catch (Exception ex)
                    {
                        LogError($"Invalid connection {ex.Message}");
                    }
                }
            }
        }
        //Application myParent;
        public Window1()
        {
           // myParent = parent;
            InitializeComponent();
            int borderWidth = 0;
            int.TryParse(ConfigurationSettings.AppSettings["BorderWidth"], out borderWidth);
        }
        int[] imageCounterArray;
        private string getImageStorageLoc()
        {
            var storageDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures) + @"\SmugAndy\";
            return storageDirectory;
        }



        private async void setupJob()
        {
# if(DEBUG)
            var frequencyHours = 24; //24 = 1 per day.  
            var startHour = DateTime.Now.Hour;
            var startMinute = DateTime.Now.Minute + 1;
#else
            var frequencyHours = 24;// run once per day
            var startHour = 11;
            var startMinute = 15;
#endif

            TaskScheduler.Instance.ScheduleTask(startHour, startMinute, frequencyHours,  //run at 11:15a daily
               () =>
               {
                   logMsg("reloading library!!!");
                   initEngine(true);
                });
        }
        public void init()

        {
           // setupJob(); //todo: this is broken, reloading causes multiple images to show.

            //   LogError($"Starting up: {DateTime.Now}");
            var tmp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var file = tmp + @"\andyScr.trace.log";

            initEngine();

            lm = new listManager(gridWidth * gridHeight);
            imageCounterArray = new int[gridHeight * gridWidth];

            for (int i = 0; i < gridHeight * gridWidth - 1; i++)
                imageCounterArray[i] = 0;

            for (int idx = 0; idx < gridWidth; idx++)
            {
                var sp = new StackPanel();
                sp.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                sp.Orientation = Orientation.Vertical;


                hStack1.Children.Add(sp);//add vertical stack panel.
            }
            var imageIndex = 0;  //to be used for saving/loading default image.
            var storageDirectory = getImageStorageLoc();

            try
            {
                if (!Directory.Exists(storageDirectory) && doSmartStart)
                    Directory.CreateDirectory(storageDirectory);
            }
            catch (Exception ex)
            {
                LogError(ex.Message);
            }
            foreach (StackPanel stackPanel in hStack1.Children)
            {
                for (var idx = 0; idx < gridHeight; idx++)
                {

                    var myBorder = new Border();
                    myBorder.BorderThickness = new Thickness(_engine.settings.borderThickness);

                    rotatableImage i = new rotatableImage(_engine);
                    myBorder.Child = i;

                    var bi3 = new BitmapImage();
                    if (!File.Exists(storageDirectory + @"\" + imageIndex + @".jpg") || !doSmartStart)
                    {
                        bi3.BeginInit();
                        bi3.UriSource = new Uri("/andyScrSaver;component/2011072016-03-00IMG7066-L.jpg", UriKind.Relative);
                        bi3.EndInit();
                    }
                    else
                    {
                        var fileName = storageDirectory + @"\" + imageIndex + @".jpg";
                        bi3.BeginInit();
                        try
                        {
                            bi3.UriSource = new Uri(fileName, UriKind.RelativeOrAbsolute);
                            bi3.CacheOption = BitmapCacheOption.OnLoad;
                        }
                        catch (Exception ex)
                        {
                            LogError(ex.Message);
                            bi3.UriSource = new Uri("/andyScrSaver;component/2011072016-03-00IMG7066-L.jpg", UriKind.Relative);
                        }
                        finally
                        {
                            bi3.CacheOption = BitmapCacheOption.OnLoad;
                        }
                        bi3.EndInit();
                    }
                    i.Source = bi3;
                    i.ImageIndex = imageIndex++;
                    i.Height = /*this.Height*/ myHeight / gridHeight - (borderWidth / gridHeight); //161; 
                    i.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    stackPanel.Children.Add(myBorder);
                }
            }

            if (!System.Diagnostics.Debugger.IsAttached)
            {
                Topmost = true;
            }
            Cursor = Cursors.None;

            var tc = this.Cursor;
            this.Cursor = Cursors.Wait;


            actionsDisabled = false;
            myCursor = Cursor;

            //initEngine();

            lastMouseMove = DateTime.Now;
            totalMouseMoves = 0;


            // On each WheelMouse change, we zoom in/out a particular % of the original distance
            const double ZoomPctEachWheelChange = 0.02;
            zoomDelta = Vector3D.Multiply(ZoomPctEachWheelChange, camMain.LookDirection);
            this.Cursor = tc;
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                // Zoom in
                camMain.Position = Point3D.Add(camMain.Position, zoomDelta);
            else
                // Zoom out
                camMain.Position = Point3D.Subtract(camMain.Position, zoomDelta);
        }
        private void doshutdown()
        {
            if (!actionsDisabled)
            {
                myCursor = Cursor;
                Application.Current.Shutdown();
            }
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            doshutdown();
        }
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                //todo: What happens when service is stopped, can I safely resrtart?     
                _engine.resetExpiredImageCollection();
                updateImage();
            }
            else if (e.Key == Key.S)
            {
                statsEnabled = !statsEnabled;
                if (statsEnabled)
                {
                    showMsg(_engine.getRuntimeStatsInfo());
                }
                else showMsg("");
            }
            else if (e.Key == Key.Escape || e.Key== Key.Q)
            {
                Application.Current.Shutdown();
              //  Close();
            }
            else if (e.Key == Key.W)
            {
                 WindowStyle = WindowStyle.SingleBorderWindow;
                 ResizeMode = ResizeMode.CanResizeWithGrip;
            }
            else if (e.Key == Key.B)
            {
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else if (e.Key == Key.R)
            {
                initEngine(true);
            }
            else
            {
                doshutdown();
            }
        }

        private void showMsg(String msg)
        {

            SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
            {
                if (!actionsDisabled)
                {
                    Random r = new Random();
                    VerticalAlignment vAlign = System.Windows.VerticalAlignment.Bottom;
                    HorizontalAlignment hAlign = System.Windows.HorizontalAlignment.Center;
                    do
                    {
                        vAlign = (System.Windows.VerticalAlignment)r.Next(3);
                        hAlign = (System.Windows.HorizontalAlignment)r.Next(3);
                    } while (vAlign != System.Windows.VerticalAlignment.Center &&
                        hAlign != System.Windows.HorizontalAlignment.Center);
                    SetupRequired.VerticalAlignment = vAlign;
                    SetupRequired.HorizontalAlignment = hAlign;
                }

                SetupRequired.Visibility = System.Windows.Visibility.Visible;
                SetupRequired.Content = msg;
            }));

        }
        public void showException(String msg)
        {
            //log exceptions fired from smEngine
            LogError(msg);
        }

        private DateTime lastMouseMove;
        private long totalMouseMoves;
        private long maxMouseMoves = 2;
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {//author: ASH
            var ts = DateTime.Now - lastMouseMove;
            if (ts.TotalMilliseconds < 100)
            {
                totalMouseMoves++;
                if (totalMouseMoves > maxMouseMoves)//a little bit of slack before closing.
                {
                    doshutdown();
                }

            }
            else
            {
                totalMouseMoves = 0;
            }
            lastMouseMove = DateTime.Now;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            logMsg("Closing");
        }

        private void image1_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (actionsDisabled)
            {
                updateImage();// automatic way totalMouseMoves 
            }
        }
        private void Window_SizeChanged_1(object sender, SizeChangedEventArgs e)
        {
            myWidth = Convert.ToInt32(e.NewSize.Width);
            myHeight = Convert.ToInt32(e.NewSize.Height);
            updateImage();
        }
        private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
        }
    }
}
