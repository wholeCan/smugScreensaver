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
using SMEngine;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using static SMEngine.CSMEngine;

namespace andyScreenSaver
{

    public partial class Window1 : Window
    {
        private Vector3D zoomDelta;
        private int myHeight = 0;
        private int myWidth = 0;
        const bool doSmartStart = true;
        public void setDimensions(int _myHeight, int _myWidth)
        {
            MyHeight = _myHeight;
            MyWidth = _myWidth;
        }
        private SMEngine.CSMEngine _engine;
        ThreadStart ts = null;
        Thread t = null;
        static bool running = true;
        bool actionsDisabled = false;
        int gridWidth = 5;  //these are replaced by setting menu.
        int gridHeight = 4;
        int borderWidth = 5;//see config file for setting.
        public void disableActions()
        {
            ActionsDisabled = true;
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
                if (bm.Width == 0 || bm.Height == 0)
                {
                    //frequent error seen at run time.
                    return Color.Black;
                }
                var srcData = bm.LockBits(
                        new Rectangle(0, 0, bm.Width, bm.Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb
                    );

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
            try
            {
                Bitmap bmpImage = new Bitmap(original);
                var cropArea = new Rectangle(0, 0, (int)original.Height / 3, (int)original.Width / 3);
                return bmpImage.Clone(cropArea, bmpImage.PixelFormat);
            }
            catch (Exception ex)
            {
                return null;
            }
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
                    if (croppedImage == null)
                    {
                        //throw new Exception("could not find corner");
                        return;
                    }

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

        private void LogError(string msg)
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
                            var image = borderImage.Child as indexableImage;
                            image.Height = MyHeight / GridHeight - (100 / Math.Pow(2, GridHeight)); //161; 
                        }
                    }
                    if (Engine.screensaverExpired())
                    {
                        showMsg(
                            DateTime.Now.ToShortTimeString() +
                            ": Slide show is stopped until " +
                            (Engine.settings.startTime / 100).ToString() +
                            ":" +
                            (Engine.settings.startTime % 100).ToString("00") +
                            " - press <left> or <right> arrow to wake up."
                            );
                    }
                }));
            var run = false;
            ImageSet image = null;

            var counter = 0;
            var blackImagePlaced = false;

            while (image == null && counter < 1)
            {//if image isn't ready, wait for it.
                image = Engine.getImage();
                counter++;//allow it to die.
                if (image == null)
                {
                    if (counter % 100 == 0)
                    {
                        hideSetup();
                    }
                }
                else
                {

                    //Thread.Sleep(100);
                }
            }
            if (Engine.getLogin().login == "")
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

                        if (Engine.warm())
                        {
                            if (image != null)
                            {
                                image.B = Engine.getBlackImagePixel();
                                blackImagePlaced = true;
                            }
                        }

                    }));
                }
                else
                {//putting this in the else, because the blackImagePlaced is set in another thread and creates a race condition.
                    //  the red text disappears after resetting network connection, when really i want it to show up.
                    if (!blackImagePlaced && !Engine.screensaverExpired())
                    {
                        //todo: add switch here controlled by hotkey
                        if (StatsEnabled)
                        { showMsg(Engine.getRuntimeStatsInfo()); }
                        else
                        { showMsg(""); }
                    }
                }
                hStack1.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate ()
                {
                    setImage(ref run, ref image);
                }));
            }
            if (run && !Engine.settings.showInfo && !blackImagePlaced)
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
                    var maxTotalCells = Convert.ToInt32(GridWidth) * Convert.ToInt32(GridHeight);
                    var beginIndex = 0;
                    var rIndex = new Random().Next(beginIndex, maxTotalCells);
                    var randWidth = rIndex % GridWidth;
                    var randHeight = rIndex / GridWidth;
                    while (Lm.isInList(new Tuple<int, int>(randWidth, randHeight)))
                    {
                        rIndex = new Random().Next(beginIndex, maxTotalCells);
                        randWidth = rIndex % GridWidth;
                        randHeight = rIndex / GridWidth;

                    }
                    Bitmap bmyImage2;
                    if (s != null && s.B != null)
                    {
                        bmyImage2 = s.B;
                        if (Engine.settings.showInfo)
                        {
                            if (bmyImage2.Height == 0)
                            {
                                LogError("empty bmp");
                            }
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
        private void setImageCaption(ref SMEngine.CSMEngine.ImageSet s, ref Bitmap targetBitmapImage, int randWidth, int randHeight)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(s.CAtegory + ": " + s.AlbumTitle);
                if (!string.IsNullOrEmpty(s.Caption) && !s.Caption.Contains("OLYMPUS"))
                {
                    sb.Append(": " + s.Caption);
                }
                imageAddCaption(sb.ToString(), ref targetBitmapImage);
            }
            catch (Exception ex)
            {
                LogError($"Problem setting caption {ex.Message}");
            }
            indexableImage image = ((hStack1.Children[randWidth] as StackPanel).Children[randHeight] as Border).Child as indexableImage;

            //whether you set MaxHeight, or MaxWidth will determine how the images end up centered on screen.
            image.MaxHeight = /*this.Height*/ MyHeight / GridHeight - (BorderWidth / GridHeight); 
            image.Width = MyWidth / GridWidth - (BorderWidth / GridWidth);
            image.Source = Bitmap2BitmapImage(s.B);

            Lm.addToList(new Tuple<int, int>(randWidth, randHeight));
            var imageIndex = (int)(randWidth + (randHeight * (GridWidth)));
            if (ImageCounterArray[imageIndex] == 0)
            {
                try
                {
                    var tmp = new Bitmap(targetBitmapImage);
                    var fileName = getImageStorageLoc() + @"\" + imageIndex + @".jpg";
                    if (File.Exists(fileName) && DoSmartStart)
                    {
                        File.Delete(fileName);
                    }
                    if (DoSmartStart)
                    { 
                        tmp.Save(fileName, ImageFormat.Jpeg); 
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Problem setting image {ex.Message}");
                }
                ImageCounterArray[imageIndex]++;
            }
        }

        private void run()
        {
            Running = true;
            while (Running)
            {
                var runDelta = DateTime.Now - LastUpdate;
                try
                {
                    updateImage();
                }
                catch (Exception ex)
                {
                    LogError("updateImage failed: " + ex.Message);
                }
                finally
                {
                    var millisecondsSinceLastRun = DateTime.Now.Subtract(LastUpdate).TotalMilliseconds;
                    var timeToSleep = Engine.settings.speed_s * 1000 - millisecondsSinceLastRun;
                    if (timeToSleep > 0)
                    {
                        logMsg("sleeping for " + timeToSleep + " milliseconds");
                        Thread.Sleep((Int32)timeToSleep);
                    }
                    LastUpdate = DateTime.Now;
                }
            }
        }

        Cursor myCursor;

        private void loginSmugmug()
        {
            try
            {
                Engine.login(Engine.getCode());
            }
            catch (Exception e)
            {
                MessageBox.Show("Invalid login, shutting down!");

                return;
            }
            Ts = new ThreadStart(run);
            T = new Thread(Ts)
            {
                IsBackground = true
            };
            T.Start();

        }
        Task task = null;
        private void initEngine(bool? forceStart = false)
        {
            //get dimensions
            var w = System.Windows.SystemParameters.WorkArea.Width;
            var h = System.Windows.SystemParameters.WorkArea.Height;

            if (Engine == null || forceStart == true)
            {

                Engine = new SMEngine.CSMEngine();
                Engine.setScreenDimensions(w, h);
                {
                    GridHeight = Engine.settings.gridHeight;
                    GridWidth = Engine.settings.gridWidth;

                    Engine.fireException += showException;
                    try
                    {
                        Task = new Task(() => { loginSmugmug(); });
                        Task.Start();
                    }
                    catch (Exception ex)
                    {
                        LogError($"Invalid connection {ex.Message}");
                    }

                    //todo how to shut down if failed?
                }
            }
        }

        //Application myParent;
        [Obsolete]
        public Window1()
        {
            // myParent = parent;
            InitializeComponent();
            var borderWidth = 0;
            int.TryParse(ConfigurationSettings.AppSettings["BorderWidth"], out borderWidth);
        }
        int[] imageCounterArray;
        private string getImageStorageLoc()
        {
            var storageDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures) + @"\SmugAndy\";
            return storageDirectory;
        }



        static void MouseCursorResetMethod(object state)
        {
            Debug.WriteLine($"Thread execution to hide mouse run at: {DateTime.Now}");
            {

                for (; ; )
                {
                    try
                    {
                        (state as Window1).Dispatcher.BeginInvoke(new Action(delegate ()
                        {
                            DateTime laterTime = (state as Window1).getLastMouseMove().AddSeconds(5); //wait 5 seconds before turning off cursor.
                            if (DateTime.Now > laterTime)
                            {

                                Mouse.SetCursor(Cursors.None);
                            }
                        }));
                    }catch (Exception ex)
                    {
                        Debug.WriteLine("thread err 239d");
                    }
                    Thread.Sleep(1000);// wait a bit between runs.
                }
            }
        }


        public void init()

        {
            var tmp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            initEngine();

            Thread mouseThreadWithParameters = new Thread(new ParameterizedThreadStart(MouseCursorResetMethod));
            mouseThreadWithParameters.IsBackground = true;
            mouseThreadWithParameters.Start(this);

            Lm = new listManager(GridWidth * GridHeight);
            ImageCounterArray = new int[GridHeight * GridWidth];

            for (var i = 0; i < GridHeight * GridWidth - 1; i++)
            {
                ImageCounterArray[i] = 0;
            }

            for (var idx = 0; idx < GridWidth; idx++)
            {
                var sp = new StackPanel
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Orientation = Orientation.Vertical
                };


                hStack1.Children.Add(sp);//add vertical stack panel.
            }
            var imageIndex = 0;  //to be used for saving/loading default image.
            var storageDirectory = getImageStorageLoc();

            try
            {
                if (!Directory.Exists(storageDirectory) && DoSmartStart)
                {
                    Directory.CreateDirectory(storageDirectory);
                }
            }
            catch (Exception ex)
            {
                LogError(ex.Message);
            }
            foreach (StackPanel stackPanel in hStack1.Children)
            {
                for (var idx = 0; idx < GridHeight; idx++)
                {

                    var myBorder = new Border();
                    myBorder.BorderThickness = new Thickness(Engine.settings.borderThickness);

                    var i = new indexableImage();
                    myBorder.Child = i;

                    var bi3 = new BitmapImage();
                    if (!File.Exists(storageDirectory + @"\" + imageIndex + @".jpg") || !DoSmartStart)
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
                    i.Height = /*this.Height*/ MyHeight / GridHeight - (BorderWidth / GridHeight); //161; 
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


            ActionsDisabled = false;
            MyCursor = Cursor;

            //initEngine();

            LastMouseMove = DateTime.Now;
            TotalMouseMoves = 0;


            // On each WheelMouse change, we zoom in/out a particular % of the original distance
            const double ZoomPctEachWheelChange = 0.02;
            ZoomDelta = Vector3D.Multiply(ZoomPctEachWheelChange, camMain.LookDirection);
            this.Cursor = tc;

           

        }
        public DateTime getLastMouseMove()
        {
            return lastMouseMove;
        }
        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                // Zoom in
                camMain.Position = Point3D.Add(camMain.Position, ZoomDelta);
            else
                // Zoom out
                camMain.Position = Point3D.Subtract(camMain.Position, ZoomDelta);
        }
        private void doshutdown()
        {
            if (!ActionsDisabled)
            {
                MyCursor = Cursor;
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
                Engine.resetExpiredImageCollection();
                updateImage();
            }
            else if (e.Key == Key.S)
            {
                StatsEnabled = !StatsEnabled;
                if (StatsEnabled)
                {
                    showMsg(Engine.getRuntimeStatsInfo());
                }
                else showMsg("");
            }
            else if (e.Key == Key.Escape || e.Key == Key.Q)
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

        private void showMsg(string msg)
        {

            SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
            {
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

        public Vector3D ZoomDelta { get => zoomDelta; set => zoomDelta = value; }
        public int MyHeight { get => myHeight; set => myHeight = value; }
        public int MyWidth { get => myWidth; set => myWidth = value; }

        public static bool DoSmartStart => doSmartStart;

        public CSMEngine Engine { get => _engine; set => _engine = value; }
        public ThreadStart Ts { get => ts; set => ts = value; }
        public Thread T { get => t; set => t = value; }
        public static bool Running { get => running; set => running = value; }
        public bool ActionsDisabled { get => actionsDisabled; set => actionsDisabled = value; }
        public int GridWidth { get => gridWidth; set => gridWidth = value; }
        public int GridHeight { get => gridHeight; set => gridHeight = value; }
        public int BorderWidth { get => borderWidth; set => borderWidth = value; }
        public DateTime LastUpdate { get => lastUpdate; set => lastUpdate = value; }
        public listManager Lm { get => lm; set => lm = value; }
        public bool StatsEnabled { get => statsEnabled; set => statsEnabled = value; }
        public Cursor MyCursor { get => myCursor; set => myCursor = value; }
        public Task Task { get => task; set => task = value; }
        public int[] ImageCounterArray { get => imageCounterArray; set => imageCounterArray = value; }
        public DateTime LastMouseMove { get => lastMouseMove; set => lastMouseMove = value; }
        public long TotalMouseMoves { get => totalMouseMoves; set => totalMouseMoves = value; }
        public long MaxMouseMoves { get => maxMouseMoves; set => maxMouseMoves = value; }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {//author: ASH
            var ts = DateTime.Now - LastMouseMove;
            if (actionsDisabled)
            {
                if (ts.TotalMilliseconds < 100)
                {
                    TotalMouseMoves++;
                    if (TotalMouseMoves > MaxMouseMoves)//a little bit of slack before closing.
                    {
                        doshutdown();
                    }

                }
                else
                {
                    TotalMouseMoves = 0;
                }
            }
            else
            {
                //todo: show mouse cursor
                this.Cursor = MyCursor;
//                Mouse.SetCursor(MyCursor);
            }
            LastMouseMove = DateTime.Now;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            logMsg("Closing");
        }

        private void image1_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (ActionsDisabled)
            {
                updateImage();// automatic way totalMouseMoves 
            }
        }
        private void Window_SizeChanged_1(object sender, SizeChangedEventArgs e)
        {
            MyWidth = Convert.ToInt32(e.NewSize.Width);
            MyHeight = Convert.ToInt32(e.NewSize.Height);
            updateImage();
        }
        private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
        }
    }
}
