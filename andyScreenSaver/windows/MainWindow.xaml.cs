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

using andyScreenSaver.windows.Helpers;
using LibVLCSharp.Shared;
using SMEngine;
using System;
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
using System.Windows.Media;


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

        private LayoutHelper _layoutHelper;
        private TilePlacementService _tilePlacement;
        private TileRenderer _tileRenderer;

        // Async loop state (Option A)
        private CancellationTokenSource? _imageLoopCts;
        private Task? _imageLoopTask;
        private AsyncManualResetEvent _pauseGate = new AsyncManualResetEvent(initialState: true);

        public void disableActions()
        {
            ScreensaverModeDisabled = true;
            Engine.IsScreensaver(ScreensaverModeDisabled);

            Topmost = false;
            Cursor = Cursors.Arrow;
        }

        private BitmapImage? Bitmap2BitmapImage(System.Drawing.Bitmap bitmap)
        {
            try { return ImageUtils.BitmapToBitmapImage(bitmap); } catch (Exception ex) { LogError(ex, ex.Message); return null; }
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
        static Bitmap ScaleImage(Bitmap originalImage, int desiredHeight)
        {
            return ImageUtils.ScaleImage(originalImage, desiredHeight);
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
            AppLogger.Log(msg);
        }

        private void LogError(Exception ex, string msg)
        {
            AppLogger.LogError(ex, msg);
        }

        private Double calculateImageHeight()
        {
            return _layoutHelper?.CalculateImageHeight() ?? 0d;
        }

        private Double calculatedImageWidth()
        {
            var width = _layoutHelper?.CalculateImageWidth() ?? 0d;
            Debug.WriteLine($"calculated width: {width}");
            return width;
        }

        public Window1()
        {
            InitializeComponent();
            try
            {
                Core.Initialize(); // LibVLCSharp initialization 
                                   //only needed for show, not config.
            }
            catch (Exception ex)
            {
                LogError(ex, ex.Message);
            }
            var borderWidth = 0;
            int.TryParse(ConfigurationSettings.AppSettings["BorderWidth"], out borderWidth);
            AppOpenCloseLogger.logOpened();

            // Initialize helpers if null
            _layoutHelper ??= new LayoutHelper(
                () => MyWidth,
                () => MyHeight,
                () => GridWidth,
                () => GridHeight,
                () => BorderWidth
            );
            _tileRenderer ??= new TileRenderer(
                () => calculatedImageWidth(),
                () => calculateImageHeight(),
                (m) => LogMsg(m)
            );
        }

        private void UpdateLayoutAndExpiredBanner()
        {
            hStack1.Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    TileGridBuilder.SetImageHeights(hStack1, calculateImageHeight());
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
        }

        private ImageSet? TryGetNextImage()
        {
            try
            {
                return Engine.getImage();
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Too many requests"))
                {
                    Thread.Sleep(1000);
                }
                throw;
            }
        }

        private void ShowSetupRequiredBanner()
        {
            SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
            {
                SetupRequired.Visibility = System.Windows.Visibility.Visible;
                SetupRequired.Content = "Setup required!  Go to the screensaver menu.";
            }));
        }

        private void HandleNoImageAvailable()
        {
            SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
            {
                SetupRequired.Visibility = System.Windows.Visibility.Visible;
                SetupRequired.Content = "No data presently available, trying again...";
                shuffleImages();
            }));
        }

        private void HandleImageAvailable(bool blackImagePlaced)
        {
            if (!blackImagePlaced && !Engine.screensaverExpired())
            {
                ShowStats();
            }
        }

        private (bool run, ImageSet? image) ProcessPlacement(bool run, ImageSet? image)
        {
            bool localRun = run;
            ImageSet? localImage = image;
            hStack1.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate ()
            {
                SetImage(ref localRun, ref localImage);
            }));
            return (localRun, localImage);
        }

        private void CollapseSetupBannerIfAppropriate(bool run, bool blackImagePlaced)
        {
            if (run && !Engine.settings.showInfo && !blackImagePlaced)
            {
                SetupRequired.Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    SetupRequired.Visibility = System.Windows.Visibility.Collapsed;
                }));
            }
        }

        private bool IsLoggedIn()
        {
            return Engine.getLogin().login != string.Empty;
        }

        private void UpdateImage()
        {
            try
            {
                UpdateLayoutAndExpiredBanner();

                bool run = false;
                bool blackImagePlaced = false;
                ImageSet? image = TryGetNextImage();

                if (!IsLoggedIn())
                {
                    ShowSetupRequiredBanner();
                }
                else
                {
                    if (image == null)
                    {
                        HandleNoImageAvailable();
                    }
                    else
                    {
                        HandleImageAvailable(blackImagePlaced);
                    }

                    (run, image) = ProcessPlacement(run, image);
                }

                CollapseSetupBannerIfAppropriate(run, blackImagePlaced);
            }
            catch (Exception ex)
            {
                LogError(ex, ex.Message);
            }
        }

        private void ShowStats()
        {
            if (StatsEnabled)
            {
                ShowMsg(Engine.getRuntimeStatsInfo(), true);
            }
            else
            {
                ShowMsg(null, false);
            }
        }

        private void SetImage(ref bool run, ref ImageSet s)
        {
            try
            {
                if (s != null)
                {
                    run = true;
                    var cell = _tilePlacement.PickNextCell();
                    var randWidth = cell.Item1;
                    var randHeight = cell.Item2;

                    Bitmap bmyImage2;
                    if (s != null && s.Bitmap != null)
                    {
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
                string captionText = CaptionBuilder.Build(s);
                if (! s.IsVideo)
                {
                    // Only bake caption into photos when showInfo is enabled
                    if (Engine.settings.showInfo)
                    {
                        ImageUtils.AddCaption(captionText, ref targetBitmapImage);
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(ex, $"Problem setting caption {ex.Message}");
            }

            var border = GetGridBorder(randWidth, randHeight);
            var image = border.Child as indexableImage;

            // Render based on media type
            if (s.IsVideo)
            {
                // Only show overlay when showInfo is enabled
                var text = Engine.settings.showInfo ? CaptionBuilder.Build(s) : string.Empty;
                _tileRenderer.RenderSync(border, image, s, text, Engine.isDefaultMute());
            }
            else
            {
                _ = _tileRenderer.RenderAsync(border, image, s, Engine.isDefaultMute());
            }

            _tilePlacement.MarkPlaced(randWidth, randHeight);
            CacheImageIfFirstTime(targetBitmapImage, randWidth, randHeight);
        }

        private string BuildCaptionText(SMEngine.CSMEngine.ImageSet s)
        {
            return CaptionBuilder.Build(s);
        }

        private Border GetGridBorder(int randWidth, int randHeight)
        {
            return TileGridBuilder.GetBorderAt(hStack1, randWidth, randHeight);
        }

        int videoCounter = 0;
        int photoCounter = 0;




        private void CacheImageIfFirstTime(Bitmap targetBitmapImage, int randWidth, int randHeight)
        {
            int imageIndex = randWidth + (randHeight * GridWidth);
            if (ImageCounterArray[imageIndex] == 0)
            {
                try
                {
                    var tmp = new Bitmap(targetBitmapImage);
                    var fileName = getImageStorageLoc() + @"\" + imageIndex + @".jpg";
                    if (File.Exists(fileName) && DoSmartStart)
                        File.Delete(fileName);

                    if (DoSmartStart)
                        tmp.Save(fileName, ImageFormat.Jpeg);
                }
                catch (Exception ex)
                {
                    LogError(ex, $"Problem setting image {ex.Message}");
                }
                ImageCounterArray[imageIndex]++;
            }
        }

        private void SafeUpdateImage()
        {
            try
            {
                UpdateImage();
            }
            catch (Exception ex)
            {
                LogError(ex, "updateImage failed: " + ex.Message);
            }
        }

        private int ComputeSleepMilliseconds()
        {
            var elapsedMs = (int)DateTime.Now.Subtract(LastUpdate).TotalMilliseconds;
            var targetMs = (int)(Engine.settings.speed_s * 1000);
            var remaining = targetMs - elapsedMs;
            return remaining > 0 ? remaining : 0;
        }

        private async Task ImageUpdateLoopAsync(CancellationToken token)
        {
            Running = true;
            while (!token.IsCancellationRequested && Running)
            {
                try
                {
                    await _pauseGate.WaitAsync(token).ConfigureAwait(false);
                    await Task.Run(() => UpdateImage(), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError(ex, "updateImage failed: " + ex.Message);
                }

                // pacing outside finally
                var remaining = ComputeSleepMilliseconds();
                if (remaining > 0)
                {
                    try { await Task.Delay(remaining, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                }
                LastUpdate = DateTime.Now;
            }
        }

        private void StartImageLoop()
        {
            StopImageLoop();
            _imageLoopCts = new CancellationTokenSource();
            _imageLoopTask = ImageUpdateLoopAsync(_imageLoopCts.Token);
        }

        private async void StopImageLoop()
        {
            try
            {
                _imageLoopCts?.Cancel();
                if (_imageLoopTask != null)
                {
                    try { await _imageLoopTask.ConfigureAwait(false); } catch { /* ignore */ }
                }
            }
            finally
            {
                _imageLoopTask = null;
                _imageLoopCts?.Dispose();
                _imageLoopCts = null;
            }
        }

        Cursor myCursor;

        private void LoginSmugmug()
        {
            try
            {
                Engine.login(Engine.getCode());
            }
            catch (Exception)
            {
                MessageBox.Show("Invalid login, shutting down!");
                return;
            }

            StartImageLoop();
        }

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
            if (isPaused)
            {
                _pauseGate.Set();
            }
            else
            {
                _pauseGate.Reset();
            }
            isPaused = !isPaused;
            ShowStats();
        }

        private void Doshutdown()
        {
            MyCursor = Cursor;
            StopImageLoop();
            Application.Current.Shutdown();
        }

        private void InitEngine(bool? forceStart = false)
        {
            if (Engine != null)
            {
                AppOpenCloseLogger.uptimeCheckpoint(Engine.getUptime(), Engine.getRuntimeStatsInfo(false));
            }
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
                }
            }
        }


        int[] imageCounterArray;
        private string getImageStorageLoc()
        {
            var storageDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures) + @"\SmugAndy\";
            return storageDirectory;
        }

        private BitmapImage GetInitialTileImage(int imageIndex, string storageDirectory)
        {
            const string fallback = "/andyScrSaver;component/2011072016-03-00IMG7066-L.jpg";
            return InitialImageProvider.Build(imageIndex, storageDirectory, DoSmartStart, fallback);
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
                    Thread.Sleep(100);
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
            _tilePlacement = new TilePlacementService(Lm, () => GridWidth, () => GridHeight);
            ImageCounterArray = new int[GridHeight * GridWidth];

            for (var i = 0; i < GridHeight * GridWidth - 1; i++)
            {
                ImageCounterArray[i] = 0;
            }

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

            TileGridBuilder.BuildGrid(
                hStack1,
                GridWidth,
                GridHeight,
                Engine.settings.borderThickness,
                imageIndex => GetInitialTileImage(imageIndex, storageDirectory),
                MyHeight / GridHeight - (BorderWidth / GridHeight)
            );

            if (!System.Diagnostics.Debugger.IsAttached)
            {
                Topmost = true;
            }
            Cursor = Cursors.None;

            var temporaryCursor = this.Cursor;
            this.Cursor = Cursors.Arrow;


            ScreensaverModeDisabled = false;
            MyCursor = Cursor;

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

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!screensaverModeDisabled)
            {
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
                case Key.M:
                    Engine.toggleDefaultMute();
                    // apply global mute/unmute to all videos immediately
                    _tileRenderer?.ApplyGlobalMute(this, Engine.isDefaultMute());
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
            Engine.resetExpiredImageCollection();
            var totalImages = gridHeight * gridWidth;
            for (int i = 0; i < totalImages*1.5; i++)
                UpdateImage();
        }

        private void DoUpgrade()
        {
            var upgradeManager = UpgradeManager.Instance;
            
            if (!upgradeManager.ReadyForUpgrade)
            {
                Debug.WriteLine("already upgraded");
            }
            else
            {
                Debug.WriteLine("upgrade query to user");
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
            UiMessageHelper.ShowMessage(SetupRequired, isPaused, msg, showStatsIsSet);
        }
        public void ShowException(String msg)
        {
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
        private Task? task = null;

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            var resetTime = LastMouseMove.AddMilliseconds(500);
            if (!screensaverModeDisabled)
            {
                if (DateTime.Now < resetTime)
                {
                    TotalMouseMoves++;
                    if (TotalMouseMoves > MaxMouseMoves)
                    {
                        Doshutdown();
                    }
                }
                else
                {
                    TotalMouseMoves = 0;
                }
            }
            else
            {
                this.Cursor = MyCursor;
            }
            LastMouseMove = DateTime.Now;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            LogMsg("Closing");
            
            AppOpenCloseLogger.logClosed(Engine.getUptime(), Engine.getRuntimeStatsInfo(false));
        }

        private void Image1_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // Ignore clicks that originate from a video tile; those toggle mute inside TileRenderer
            if (ClickOriginatedFromVideo(e))
            {
                e.Handled = true;
                return;
            }
            UpdateImage();
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

        private bool ClickOriginatedFromVideo(MouseEventArgs e)
        {
            try
            {
                var d = e.OriginalSource as DependencyObject;
                while (d != null)
                {
                    if (d is LibVLCSharp.WPF.VideoView)
                        return true;
                    d = VisualTreeHelper.GetParent(d);
                }
            }
            catch { }
            return false;
        }
    }
}
