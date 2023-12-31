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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using static SMEngine.CSMEngine;


#nullable enable

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
        ThreadStart? ts = null;
        Thread? threadImageUpdate = null;
        static bool running = true;
        bool screensaverModeDisabled = false;
        
        int gridWidth = 5;  //these are replaced by setting menu.
        int gridHeight = 4;
        int borderWidth = 5;//see config file for setting.
        public void disableActions()
        {
            ScreensaverModeDisabled = true;
            Engine.IsScreensaver(ScreensaverModeDisabled);

            Topmost = false;
            Cursor = Cursors.Arrow;
        }

        private BitmapImage? Bitmap2BitmapImage(System.Drawing.Bitmap bitmap)
        {
            BitmapImage? bitmapImage = null;
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
                LogError(ex, ex.Message);
            }
            return bitmapImage;
        }

        private void HideSetup()
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

        static Color GetAverageColor(Bitmap bitmap, Rectangle region)
        {

            int totalRed = 0;
            int totalGreen = 0;
            int totalBlue = 0;

            int totalPixels = 0;

            for (int y = region.Top; y < region.Bottom; y++)
            {
                for (int x = region.Left; x < region.Right; x++)
                {
                    Color pixelColor = bitmap.GetPixel(x, y);

                    totalRed += pixelColor.R;
                    totalGreen += pixelColor.G;
                    totalBlue += pixelColor.B;

                    totalPixels++;
                }
            }
            if (totalPixels <= 0)
            {// escape div-0
                return Color.Black;
            }
            // Calculate average color components
            int averageRed = totalRed / totalPixels;
            int averageGreen = totalGreen / totalPixels;
            int averageBlue = totalBlue / totalPixels;

            return Color.FromArgb(averageRed, averageGreen, averageBlue);
        }

        private Color GetAverageColor(Bitmap bitmapImage)
        {
            var numberOfRows = 12;  //upper 12th
            var numberColumns = 3;  //left 3rd
            Rectangle topLeftQuadrant = new Rectangle(0, 0, bitmapImage.Width / numberColumns, bitmapImage.Height / numberOfRows);
            Color averageColor = GetAverageColor(bitmapImage, topLeftQuadrant);
            if (IsColorDark(averageColor))
            {
                return Color.White;
            }
            else
            {
                return Color.Black;
            }

        }

        static bool IsColorDark(Color color)
        {
            //Debug.WriteLine("Color {0} {1} {2}", color.R.ToString("X2"), color.G.ToString("X2"), color.B.ToString("X2"));
            // Calculate perceived brightness using Y component in YUV color space
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;

            // You can adjust this threshold as needed
           double brightnessThreshold = 0.7;  //ANDY, higher number means more white.

            // Check if the brightness is below the threshold to determine if it's dark
            return brightness < brightnessThreshold;
        }
        private Color GetAverageColorOld(Bitmap tmp)
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
                LogError(ex, ex.Message);
                return Color.Black;
            }
        }

        private Bitmap? GetupperLeftCornerImage(Bitmap original)
        {
            try
            {
                Bitmap bmpImage = new Bitmap(original);
                var cropArea = new Rectangle(0, 0, (int)original.Height / 3, (int)original.Width / 3);
                return bmpImage.Clone(cropArea, bmpImage.PixelFormat);
            }
            catch (Exception)
            {   //known out of memory exception, just pass over it.
                return null;
            }

        }

        private int CalculateFontSize(int imageHeight, double xPercentage)
        {
            // Formula: FontSize = ImageHeight * (X / 100)
            double fontSize = imageHeight * (xPercentage / 100);

            // Round to the nearest integer
            return (int)Math.Round(fontSize);
        }
        private int GetFontSize(Bitmap image, int configSize)
        {
            var minimumFontSize = configSize+7;
            var maxFontSize = configSize+19;
            var percentOfHeight = 3;
            var imageHeight = calculateImageHeight(); //image.Height;
            var calculatedFont = CalculateFontSize(Convert.ToInt32(imageHeight), percentOfHeight);
            var baseValue = Math.Max(minimumFontSize, calculatedFont);
            var midValue = Math.Min(baseValue, maxFontSize);
            Debug.WriteLine("*** font size: " + midValue);
            return midValue;
        }
        private int GetFontSizeOld(Bitmap image, int configSize)
        {
            int fontSize = configSize;
            if (image.HorizontalResolution < 150)
            {
                fontSize += 7;
            }
            if (image.HorizontalResolution < 80)
            {
                fontSize += 19;
            }
            return fontSize;

        }

        static Bitmap ScaleImage(Bitmap originalImage, int desiredHeight)
        {
            // Calculate the scaling factor
            float scaleFactor = (float)desiredHeight / originalImage.Height;

            // Calculate the new width based on the scaling factor
            int newWidth = (int)(originalImage.Width * scaleFactor);

            // Create a new Bitmap with the desired height and width
            Bitmap scaledImage = new Bitmap(newWidth, desiredHeight);

            using (Graphics g = Graphics.FromImage(scaledImage))
            {
                // Set the interpolation mode for better quality
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                // Draw the original image onto the new bitmap with the calculated size
                g.DrawImage(originalImage, 0, 0, newWidth, desiredHeight);
            }
            

            return scaledImage;
        }

        [Obsolete]
        private void ImageAddCaption(string text, ref Bitmap referenceImage)
        {
            //let's see if we can a caption on the bitmap.
            var firstLocation = new PointF(10f, 10f);
            try
            {
               // referenceImage = (Bitmap)ScaleImage(referenceImage, Convert.ToInt32(calculateImageHeight())).Clone();
                //exception when tmp.rawData and tmp.userData is null.
                using (var graphics = Graphics.FromImage(referenceImage))
                {
                    
                    var configPenSize = 8;
                    int.TryParse(ConfigurationSettings.AppSettings["captionPenSize"], out configPenSize);

                //    var fontSize = configPenSize;
                   // var croppedImage = getupperLeftCornerImage(tmp);//can probably skip this.
                   // if (croppedImage == null)
                   /// {
                   //     //throw new Exception("could not find corner");
                   //     return;
                   // }

                    var penColor = new SolidBrush(GetAverageColor(referenceImage));// System.Drawing.Brushes.White;



                    var fontSize = GetFontSize(referenceImage, configPenSize);
                    using (var arialFont = new Font("Arial", fontSize))
                    {
                        graphics.DrawString(text, arialFont, penColor, firstLocation);
                    }
                   
                }
            }
            catch (Exception ex)
            {
                //ignore known issue
                if (ex.Message != "A Graphics object cannot be created from an image that has an indexed pixel format.")
                {
                    LogError(ex, ex.Message);
                }
                
            }
        }

        private void shuffleImages()
        {//12/30/23 - tbh, I don't know what this is really doing.
            var tmp1 = hStack1.Children[0];
            hStack1.Children.RemoveAt(0);
            hStack1.Children.Add(tmp1);
        }

        bool statsEnabled = false;

        private void LogMsg(string msg)
        {
            Debug.WriteLine("Window: " + DateTime.Now.ToLongTimeString() + ": " + msg);
        }

        private void LogError(Exception ex, string msg)
        {
            // Console.WriteLine(msg);
            LogMsg($"{DateTime.Now}: {msg}");
            lock (this)
            {
                try
                {
                    var dir = Path.GetTempPath();
                    using (var sw = new StreamWriter(
                        $"{dir}\\errlog.smug." + DateTime.Now.ToShortDateString().Replace('/', '-') + ".txt",
                        true)
                        )
                    {
                        sw.WriteLine($"{DateTime.Now}: exception: {msg}");
                        if (ex.StackTrace != null)
                        {
                            sw.WriteLine($"{DateTime.Now}: stack trace: {ex.StackTrace}");
                        }
                        
                        sw.Close();
                    }
                }
                catch (Exception ex2)
                {
                    Debug.WriteLine("Uh Oh! Error writing error file!");
                }
            }
        }

        private Double calculateImageHeight()
        {
            return MyHeight / GridHeight - (100 / Math.Pow(2, GridHeight));

        }


        private void UpdateImage()
        {
            //todo: add try-catch block here?
            try
            {
                hStack1.Dispatcher.BeginInvoke(new Action(delegate ()
                    {
                        //resuze each of the images to fit screen.
                        foreach (var v in hStack1.Children)
                        {
                            foreach (Border borderImage in (v as StackPanel).Children)
                            {
                                var image = borderImage.Child as indexableImage;
                                image.Height = calculateImageHeight() ; //161; lculateImageHeight
                            }
                        }
                        if (Engine.screensaverExpired())
                        {
                            ShowMsg(
                                DateTime.Now.ToShortTimeString() +
                                ": Slide show is stopped until " +
                                (Engine.settings.startTime / 100).ToString() +
                                ":" +
                                (Engine.settings.startTime % 100).ToString("00") +
                                " - press <left> or <right> arrow to wake up.",
                                false
                                );
                        }
                    }));
                var run = false;
                ImageSet? image = null;

                var counter = 0;
                var blackImagePlaced = false;

                while (image == null && counter < 1)
                {//if image isn't ready, wait for it.
                    try
                    {
                        image = Engine.getImage();
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("Too many requests"))
                        {
                            // consider sleeping a bit, 429 received from server.
                            Thread.Sleep(1000);
                        }
                        throw ex;
                    }
                    counter++;//allow it to die.
                    if (image == null)
                    {
                        if (counter % 100 == 0)
                        {
                            HideSetup();
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
                                    image.Bitmap = Engine.getBlackImagePixel();
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
                            ShowStats();
                        }
                    }
                    hStack1.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate ()
                    {
                        SetImage(ref run, ref image);
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
            catch (Exception ex)
            {
                LogError(ex, ex.Message);
            }
        }

        private void ShowStats()
        {
           
                        {
                            //todo: add switch here controlled by hotkey
                            if (StatsEnabled)
                            {
                                ShowMsg(Engine.getRuntimeStatsInfo(), true);
                            }
                            else
                            {
                                ShowMsg(null, false);
                            }
                        }
        }

        private void SetImage(ref bool run, ref ImageSet s)
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
                    if (s != null && s.Bitmap != null)
                    {
                        //do this so we can try to guarantee the size prior to writing captions.
                        s.Bitmap = (Bitmap)ScaleImage(s.Bitmap, Convert.ToInt32(calculateImageHeight()));
                        bmyImage2 = s.Bitmap;
                        
                        if (Engine.settings.showInfo)
                        {
                            if (bmyImage2.Height == 0)
                            {
                                LogError(new Exception("empty bmp"), "empty bmp");
                            }
                            
                            setImageCaption(ref s, ref bmyImage2, randWidth, randHeight);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogError(e,e.Message);
            }
        }
        private void setImageCaption(ref SMEngine.CSMEngine.ImageSet s, ref Bitmap targetBitmapImage, int randWidth, int randHeight)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(s.Category + ": " + s.AlbumTitle);
                if (!string.IsNullOrEmpty(s.Caption) && !s.Caption.Contains("OLYMPUS"))
                {
                    sb.Append(": " + s.Caption);
                }
                ImageAddCaption(sb.ToString(), ref targetBitmapImage);
            }
            catch (Exception ex)
            {
                LogError(ex, $"Problem setting caption {ex.Message}");
            }
            indexableImage image = ((hStack1.Children[randWidth] as StackPanel).Children[randHeight] as Border).Child as indexableImage;

            //whether you set MaxHeight, or MaxWidth will determine how the images end up centered on screen.
            image.MaxHeight = /*this.Height*/ MyHeight / GridHeight - (BorderWidth / GridHeight); 
            image.Width = MyWidth / GridWidth - (BorderWidth / GridWidth);
            image.Source = Bitmap2BitmapImage(s.Bitmap);

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
                    LogError(ex, $"Problem setting image {ex.Message}");
                }
                ImageCounterArray[imageIndex]++;
            }
        }

        private void runImageUpdateThread()
        {
            Running = true;
            while (Running)
            {
                myManualResetEvent.WaitOne();
                try
                {
                    UpdateImage();
                }
                catch (Exception ex)
                {
                    LogError(ex, "updateImage failed: " + ex.Message);
                }
                finally
                {
                    var millisecondsSinceLastRun = DateTime.Now.Subtract(LastUpdate).TotalMilliseconds;
                    var timeToSleep = Engine.settings.speed_s * 1000 - millisecondsSinceLastRun;
                    if (timeToSleep > 0)
                    {
                        LogMsg("sleeping for " + timeToSleep + " milliseconds");
                        Thread.Sleep((Int32)timeToSleep);
                    }
                    LastUpdate = DateTime.Now;
                }
            }
        }

        Cursor myCursor;

        private void LoginSmugmug()
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
            //todo: why is this outside the try block?  is there a reason to start the thread?
            //would it hurt to not do so?  Would need to test.
            ThreadStartImageUpdate = new ThreadStart(runImageUpdateThread);
            ThreadImageUpdate = new Thread(ThreadStartImageUpdate)
            {
                IsBackground = true
            };
            ThreadImageUpdate.Start();

        }
        Task? task = null;

        private void ToggleScreen()
        {
            if (WindowStyle == WindowStyle.SingleBorderWindow)
            {
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResizeWithGrip;
            }
        }
        bool isPaused = false;
        private void TogglePauseSlideshow()
        {
            //            Engine.Pause();
            if (isPaused)
                myManualResetEvent.Set();// allow to run
            else
                myManualResetEvent.Reset();//.Suspend();
            isPaused = !isPaused;
            ShowStats();
        }

        private readonly static ManualResetEvent myManualResetEvent = new(true);

       private void InitEngine(bool? forceStart = false)
        {
            if (Engine != null)
            {
                AppOpenCloseLogger.uptimeCheckpoint(Engine.getUptime(), Engine.getRuntimeStatsInfo(false));
            }
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

                    Engine.fireException += ShowException;
                    try
                    {
                        Task = new Task(() => { LoginSmugmug(); });
                        Task.Start();
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, $"Invalid connection {ex.Message}");
                    }

                    //todo how to shut down if failed?
                }
            }
        }

      

        //Application myParent;
        [Obsolete]
        public Window1()
        {
            InitializeComponent();
            var borderWidth = 0;
            int.TryParse(ConfigurationSettings.AppSettings["BorderWidth"], out borderWidth);
            AppOpenCloseLogger.logOpened();
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
                            var secondsToHideCursor = 3;
                            DateTime laterTime = (state as Window1).getLastMouseMove().AddSeconds(secondsToHideCursor); 
                            if (DateTime.Now > laterTime)
                            {
                                Mouse.SetCursor(Cursors.None);
                            }
                        }));
                    }catch (Exception)
                    {
                        Debug.WriteLine("thread err 239d");
                    }
                    Thread.Sleep(100);// wait a bit between runs.
                }
            }
        }


        public void Init()

        {

            InitEngine();
            Engine.IsScreensaver(false);

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
                LogError(ex, ex.Message);
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
                            LogError(ex, ex.Message);
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

            var temporaryCursor = this.Cursor;
            this.Cursor = Cursors.Arrow;


            ScreensaverModeDisabled = false;
            MyCursor = Cursor;

            //initEngine();

            

            LastMouseMove = DateTime.Now;
            TotalMouseMoves = 0;

            /*
            // On each WheelMouse change, we zoom in/out a particular % of the original distance
            const double ZoomPctEachWheelChange = 0.02;
            ZoomDelta = Vector3D.Multiply(ZoomPctEachWheelChange, camMain.LookDirection);
            */
            this.Cursor = temporaryCursor;

            TaskScheduler.Instance.ScheduleTask(2, 15, 24.0 * 60.0,  //run at 2:15a daily
               () =>
               {
                   LogMsg("Scheduled task execution");
                   repullAlbums();
               });

        }

        int restartCounter = 0;
        private void repullAlbums()
        {
            LogMsg("reloading library!!!");
            InitEngine(true);
            Engine.RestartCounter = ++restartCounter;
        }
        public DateTime getLastMouseMove()
        {
            return lastMouseMove;
        }
/*        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                // Zoom in
                camMain.Position = Point3D.Add(camMain.Position, ZoomDelta);
            else
                // Zoom out
                camMain.Position = Point3D.Subtract(camMain.Position, ZoomDelta);
        }
*/
        private void Doshutdown()
        {
            MyCursor = Cursor;
           // AppOpenCloseLogger.logClosed("Screensaver: "+ Engine.getUptime(), Engine.getRuntimeStatsInfo(false));
            Application.Current.Shutdown();
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!screensaverModeDisabled)
            {
            //    doshutdown();
            }
        }
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.LeftCtrl:
                    break;
                case Key.Left:
                case Key.Right:
                    Engine.resetExpiredImageCollection();
                    UpdateImage();
                    break;
                case Key.S:
                    StatsEnabled = !StatsEnabled;
                    if (StatsEnabled)
                    {
                        ShowMsg(Engine.getRuntimeStatsInfo(), true);
                    }
                    else ShowMsg(null, false);
                    break;
                case Key.Escape:
                case Key.Q:
                    Doshutdown();
                    break;
                case Key.W:
                    ToggleScreen();
                    break;
                case Key.U:
                    if (Keyboard.IsKeyDown(Key.LeftCtrl)) { DoUpgrade(); }
                    break;
                case Key.R:
                    repullAlbums();
                    break;
                case Key.Enter:
                    ReloadScreen();
                    break;
                case Key.P:
                case Key.Space:
                    TogglePauseSlideshow();
                    break;
                default:
                    if (!screensaverModeDisabled)
                    {
                        Doshutdown();
                    }
                    break;
            }
        }

        private void ReloadScreen()
        {
            //todo: w's idea to reload all images on screen.
            var totalImages = gridHeight * gridWidth;
            for (int i = 0; i < totalImages*1.5; i++)
                UpdateImage();
        }

        private void DoUpgrade()
        {
            var upgradeManager = UpgradeManager.Instance;
            
            if (!upgradeManager.ReadyForUpgrade)
            {
                //do nothing.
                Debug.WriteLine("already upgraded");
             //   MessageBox.Show("Latest is already installed.");
            }
            else
            {
                Debug.WriteLine("upgrade query to user");
                //MessageBox.Show("yes or no?");
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    "Do you want to continue with installing a new version?",
                    "Upgrade confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                    );
                if (result == MessageBoxResult.Yes)
                {
                    System.Windows.MessageBox.Show("After installation, please restart the application");
                    Debug.WriteLine("starting upgrade");
                    upgradeManager.PerformUpgrade();
                    
                    Application.Current.Shutdown();

                } 
                else
                {
                    Debug.WriteLine("Deleting tmp installer");
                    upgradeManager.deleteCurrentInstaller();
                }
            }
        }

        private void ShowMsg(string? msg, bool showStatsIsSet)
        {

            SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
            {
             //   if (showStatsIsSet)
             if (isPaused)
                {//hack: there are a couple cases where we have text, but don't want to show paused - like when sleeping.
                    if (string.IsNullOrEmpty(msg))
                        {
                        msg = "Paused";// + isPaused.ToString();
                    }
                    else { msg = "Paused" +"\n" + msg;
                    }
                }
                if (string.IsNullOrEmpty(msg))
                {
                    SetupRequired.Visibility = Visibility.Hidden;
                    return;
                }
                if (showStatsIsSet)
                    {//hack: there are a couple cases where we have text, but don't want to show paused - like when sleeping.
                    if (!isPaused)
                    {
                        msg = "Paused: " + isPaused.ToString() + "\n" + msg;
                    }
                    }
                SetupRequired.Visibility = Visibility.Visible;
                
                SetupRequired.Content = msg;
                
            }));

        }
        public void ShowException(String msg)
        {
            //log exceptions fired from smEngine
            LogError(new Exception("showException raised"), msg);
        }

        

        public Vector3D ZoomDelta { get => zoomDelta; set => zoomDelta = value; }
        public int MyHeight { get => myHeight; set => myHeight = value; }
        public int MyWidth { get => myWidth; set => myWidth = value; }

        public static bool DoSmartStart => doSmartStart;

        public CSMEngine Engine { get => _engine; set => _engine = value; }
        public ThreadStart ThreadStartImageUpdate { get => ts; set => ts = value; }
        public Thread ThreadImageUpdate { get => threadImageUpdate; set => threadImageUpdate = value; }
        public static bool Running { get => running; set => running = value; }
        public bool ScreensaverModeDisabled { get => screensaverModeDisabled; set => screensaverModeDisabled = value; }
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


        private DateTime lastMouseMove;
        private long totalMouseMoves;
        private long maxMouseMoves = 100;

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            //author: ASH
            //modified 2023, logic all wrong
            var resetTime = LastMouseMove.AddMilliseconds(500);
            if (!screensaverModeDisabled)
            {
                if (DateTime.Now < resetTime)
                {
                    TotalMouseMoves++;
                    if (TotalMouseMoves > MaxMouseMoves)//a little bit of slack before closing.
                    {
                        Doshutdown(); //shut down screensaver
                    }
                }
                else
                {
                    TotalMouseMoves = 0; // reset wiggle counter if it's been a little while.
                }
            }
            else
            {
                this.Cursor = MyCursor;
            }
            LastMouseMove = DateTime.Now;
            //Thread.Sleep(0);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            LogMsg("Closing");
            
            AppOpenCloseLogger.logClosed(Engine.getUptime(), Engine.getRuntimeStatsInfo(false));
        }

        private void Image1_MouseUp(object sender, MouseButtonEventArgs e)
        {
         //   if (ScreensaverModeDisabled)
            {
                UpdateImage();// automatic way totalMouseMoves 
            }
        }
        private void Window_SizeChanged_1(object sender, SizeChangedEventArgs e)
        {
            MyWidth = Convert.ToInt32(e.NewSize.Width);
            MyHeight = Convert.ToInt32(e.NewSize.Height);
            UpdateImage();
        }
        private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
        }
    }
}
