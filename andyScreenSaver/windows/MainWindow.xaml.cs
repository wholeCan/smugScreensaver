/**
 * Original work by Andrew Holkan
 * Date: 2/1/2013
 * Contact info: aholkan@gmail.com
 *
 * 5/2018:  Updating API
 *  Adding caption to image
 *  allowing screensaver to timeout after set time.
 *
 *  9/8/2019:  pretty stable, doing some code cleanup.
 *
 *  2/26/2022: major refactor of smEngine and everything else to upgrade to smugmug 2.0 api
 *
 *  2025: Refactored for better separation of concerns, readability, and maintainability
 * **/

using andyScreenSaver.windows.Helpers;
using andyScreenSaver.windows.Services;
using LibVLCSharp.Shared;
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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static SMEngine.CSMEngine;

#nullable enable

namespace andyScreenSaver
{
    public partial class Window1 : Window
    {
        #region Constants
        private const bool DoSmartStart = true;
        private const int DefaultMaxMouseMoves = 100;
        private const int DefaultMouseResetTimeMs = 500;
        private const int DefaultCursorHideSeconds = 3;
        #endregion

        #region Fields - Core Services
        private SMEngine.CSMEngine? _engine;
        private ImageUpdateService? _imageUpdateService;
        private ScreensaverStateManager _stateManager;
        private MouseActivityMonitor _mouseMonitor;
        #endregion

        #region Fields - Layout & Rendering
        private LayoutHelper? _layoutHelper;
        private TilePlacementService? _tilePlacement;
        private TileRenderer? _tileRenderer;
        private listManager? _listManager;
        #endregion

        #region Fields - Grid Configuration
        private int _gridWidth = 5;
        private int _gridHeight = 4;
        private int _borderWidth = 5;
        private int[] _imageCounterArray = Array.Empty<int>();
        #endregion

        #region Fields - Window Dimensions
        private int _windowHeight;
        private int _windowWidth;
        #endregion

        #region Fields - State & Timing
        private DateTime _lastUpdate = DateTime.Now;
        private Cursor? _savedCursor;
        private Task? _loginTask;
        private int _restartCounter;
        #endregion

        #region Properties
        public SMEngine.CSMEngine Engine
        {
            get => _engine ?? throw new InvalidOperationException("Engine not initialized");
            private set => _engine = value;
        }

        private int WindowHeight
        {
            get => _windowHeight;
            set => _windowHeight = value;
        }

        private int WindowWidth
        {
            get => _windowWidth;
            set => _windowWidth = value;
        }

        private int GridWidth
        {
            get => _gridWidth;
            set => _gridWidth = value;
        }

        private int GridHeight
        {
            get => _gridHeight;
            set => _gridHeight = value;
        }

        private int BorderWidth
        {
            get => _borderWidth;
            set => _borderWidth = value;
        }
        #endregion

        #region Initialization

        public Window1()
        {
            InitializeComponent();
            InitializeServices();
            InitializeLibVLC();
            LoadBorderWidthConfig();
            AppOpenCloseLogger.logOpened();
        }

        private void InitializeServices()
        {
            _stateManager = new ScreensaverStateManager();
            _mouseMonitor = new MouseActivityMonitor(DefaultMaxMouseMoves, DefaultMouseResetTimeMs);
        }

        private void InitializeLibVLC()
        {
            try
            {
                Core.Initialize();
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "LibVLCSharp initialization failed: " + ex.Message);
            }
        }

        private void LoadBorderWidthConfig()
        {
            if (int.TryParse(ConfigurationSettings.AppSettings["BorderWidth"], out int borderWidth))
            {
                _borderWidth = borderWidth;
            }
        }

        public void Init()
        {
            InitializeEngine();
            if (!_stateManager.ScreensaverModeDisabled) { 
               InitializeMouseCursorThread();
            }
            InitializeImageGrid();
            SetupWindowBehavior();
            ScheduleDailyTasks();
        }

        private void InitializeEngine()
        {
            if (_engine != null)
            {
                AppOpenCloseLogger.uptimeCheckpoint(_engine.getUptime(), _engine.getRuntimeStatsInfo(false));
            }

            var workArea = SystemParameters.WorkArea;
            _engine = new SMEngine.CSMEngine(true, "andyScreenSaver");
            _engine.setScreenDimensions(workArea.Width, workArea.Height);
            _engine.IsScreensaver(false);
            _engine.fireException += ShowException;

            GridHeight = _engine.settings.gridHeight;
            GridWidth = _engine.settings.gridWidth;

            StartLoginTask();
        }

        private void StartLoginTask()
        {
            try
            {
                _loginTask = Task.Run(() => LoginSmugmug());
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, $"Failed to start login task: {ex.Message}");
            }
        }

        private void InitializeMouseCursorThread()
        {
            var mouseThread = new Thread(MouseCursorResetLoop)
            {
                IsBackground = true
            };
            //do i want this?

            if (!_stateManager.ScreensaverModeDisabled)
            {
                mouseThread.Start(this);
            }
        }

        private void InitializeImageGrid()
        {
            _listManager = new listManager(GridWidth * GridHeight);
            _tilePlacement = new TilePlacementService(_listManager, () => GridWidth, () => GridHeight);
            _imageCounterArray = new int[GridHeight * GridWidth];

            InitializeLayoutHelpers();
            LoadInitialImages();
        }

        private void InitializeLayoutHelpers()
        {
            _layoutHelper = new LayoutHelper(
                () => WindowWidth,
                () => WindowHeight,
                () => GridWidth,
                () => GridHeight,
                () => BorderWidth
            );

            _tileRenderer = new TileRenderer(
                () => _layoutHelper?.CalculateImageWidth() ?? 0d,
                () => _layoutHelper?.CalculateImageHeight() ?? 0d,
                (m) => AppLogger.Log(m),
                Engine
            );
        }

        private void LoadInitialImages()
        {
            var storageDirectory = GetImageStorageLocation();
            EnsureStorageDirectoryExists(storageDirectory);

            TileGridBuilder.BuildGrid(
                imageGrid,
                GridWidth,
                GridHeight,
                _engine?.settings.borderThickness ?? 0,
                imageIndex => GetInitialTileImage(imageIndex, storageDirectory)
            );
        }

        private void SetupWindowBehavior()
        {
            if (!Debugger.IsAttached)
            {
                Topmost = true;
            }
            //do i want this?
            if (!_stateManager.ScreensaverModeDisabled)
            {
                Cursor = Cursors.None;
                _savedCursor = Cursor;
                _mouseMonitor.Reset();
            }
        }

        private void ScheduleDailyTasks()
        {
            TaskScheduler.Instance.ScheduleTask(
                hour: 2,
                min: 15,
                intervalInMinutes: 24.0 * 60.0,
                task: () =>
                {
                    AppLogger.Log("Scheduled task execution");
                    RepullAlbums();
                });
        }

        #endregion

        #region Window Lifecycle

        public void SetDimensions(int height, int width)
        {
            WindowHeight = height;
            WindowWidth = width;
        }

        public void DisableScreensaverMode()
        {
            _stateManager.ScreensaverModeDisabled = true;
            _engine?.IsScreensaver(_stateManager.ScreensaverModeDisabled);
            Topmost = false;
            Cursor = Cursors.Arrow;
        }

        private void Shutdown()
        {
            _imageUpdateService?.Stop();
            Application.Current.Shutdown();
        }

        #endregion

        #region Image Update Logic

        private void LoginSmugmug()
        {
            try
            {
                if (_engine == null) return;
                _engine.login(_engine.getCode());
                StartImageUpdateService();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Invalid login, shutting down!");
                AppLogger.LogError(ex, "Login failed: " + ex.Message);
            }
        }

        private void StartImageUpdateService()
        {
            _imageUpdateService?.Dispose();
            _imageUpdateService = new ImageUpdateService(
                updateAction: () => Task.Run(() => UpdateImage()),
                calculateDelayMs: ComputeSleepMilliseconds,
                logError: AppLogger.LogError
            );
            _imageUpdateService.Start();
        }

        private void UpdateImage()
        {
            try
            {
                UpdateLayoutAndExpiredBanner();

                if (!IsLoggedIn())
                {
                    ShowSetupRequiredBanner();
                    return;
                }

                var image = TryGetNextImage();
                if (image == null)
                {
                    HandleNoImageAvailable();
                    return;
                }

                PlaceImageOnGrid(image);
                ShowStatsIfEnabled();
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "UpdateImage failed: " + ex.Message);
            }

            _lastUpdate = DateTime.Now;
        }

        private void PlaceImageOnGrid(ImageSet imageSet)
        {
            imageGrid.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
            {
                SetImage(imageSet);
            }));
        }

        private void SetImage(ImageSet imageSet)
        {
            try
            {
                if (imageSet?.Bitmap == null || _tilePlacement == null) return;

                var (randWidth, randHeight) = _tilePlacement.PickNextCell();
                var scaledBitmap = ScaleImageToFit(imageSet.Bitmap);

                if (_engine?.settings.showImageCaptions == true)
                {
                    ValidateBitmap(scaledBitmap);
                    RenderImageWithCaption(imageSet, scaledBitmap, randWidth, randHeight);
                }
                else
                {
                    RenderImageWithoutCaption(imageSet, scaledBitmap, randWidth, randHeight);
                }

                _tilePlacement.MarkPlaced(randWidth, randHeight);
                CacheImageIfFirstTime(scaledBitmap, randWidth, randHeight);
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "SetImage failed: " + ex.Message);
            }
        }

        private Bitmap ScaleImageToFit(Bitmap bitmap)
        {
            var targetHeight = Convert.ToInt32(_layoutHelper?.CalculateImageHeight() ?? 0);
            return ImageUtils.ScaleImage(bitmap, targetHeight);
        }

        private void ValidateBitmap(Bitmap bitmap)
        {
            if (bitmap.Height == 0)
            {
                AppLogger.LogError(new Exception("Empty bitmap"), "Bitmap has zero height");
            }
        }

        private void RenderImageWithCaption(ImageSet imageSet, Bitmap bitmap, int gridX, int gridY)
        {
            var captionText = CaptionBuilder.Build(imageSet);

            if (!imageSet.IsVideo && _engine?.settings.showImageCaptions == true)
            {
                ImageUtils.AddCaption(captionText, ref bitmap);
                //replace the imageset with the captioned image.
                imageSet.Bitmap = bitmap;
                
            }

            var border = GetGridBorder(gridX, gridY);
            var image = border.Child as indexableImage;

            if (imageSet.IsVideo)
            {
                var overlayText = _engine?.settings.showImageCaptions == true ? captionText : string.Empty;
                _tileRenderer?.RenderSync(border, image, imageSet, overlayText, _engine?.isDefaultMute() ?? false, _engine?.settings.allowVideoToFinish ?? true);
            }
            else
            {
                _ = _tileRenderer?.RenderAsync(border, image, imageSet, _engine?.isDefaultMute() ?? false, _engine?.settings.allowVideoToFinish ?? true);
               //_tileRenderer?.RenderSync(border, image, imageSet, captionText, true);
            }
        }

        private void RenderImageWithoutCaption(ImageSet imageSet, Bitmap bitmap, int gridX, int gridY)
        {
            var border = GetGridBorder(gridX, gridY);
            var image = border.Child as indexableImage;

            if (imageSet.IsVideo)
            {
                _tileRenderer?.RenderSync(border, image, imageSet, string.Empty, _engine?.isDefaultMute() ?? false, _engine?.settings.allowVideoToFinish ?? true);
            }
            else
            {
                _ = _tileRenderer?.RenderAsync(border, image, imageSet, _engine?.isDefaultMute() ?? false, _engine?.settings.allowVideoToFinish ?? true);
            }
        }

        private ImageSet? TryGetNextImage()
        {
            try
            {
                return _engine?.getImage();
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

        private int ComputeSleepMilliseconds()
        {
            var elapsedMs = (int)DateTime.Now.Subtract(_lastUpdate).TotalMilliseconds;
            var targetMs = (int)((_engine?.settings.speed_s ?? 5) * 1000);
            var remaining = targetMs - elapsedMs;
            return remaining > 0 ? remaining : 0;
        }

        #endregion

        #region UI Updates

        private void UpdateLayoutAndExpiredBanner()
        {
            imageGrid.Dispatcher.BeginInvoke(new Action(() =>
            {
                TileGridBuilder.SetImageHeights(imageGrid, _layoutHelper?.CalculateImageHeight() ?? 0);

                if (_engine?.screensaverExpired() == true)
                {
                    ShowExpirationMessage();
                }
            }));
        }

        private void ShowExpirationMessage()
        {
            var startHour = (_engine?.settings.startTime ?? 0) / 100;
            var startMinute = (_engine?.settings.startTime ?? 0) % 100;
            var message = $"{DateTime.Now.ToShortTimeString()}: Slide show is stopped until {startHour}:{startMinute:00} - press <left> or <right> arrow to wake up.";
            ShowMessage(message, false);
        }

        private void ShowSetupRequiredBanner()
        {
            SetupRequired.Dispatcher.BeginInvoke(new Action(() =>
            {
                SetupRequired.Visibility = Visibility.Visible;
                SetupRequired.Content = "Setup required! Go to the screensaver menu.";
            }));
        }

        private void HideSetupBanner()
        {
            SetupRequired.Dispatcher.BeginInvoke(new Action(() =>
            {
                SetupRequired.Visibility = Visibility.Hidden;
            }));
        }

        private void HandleNoImageAvailable()
        {
            SetupRequired.Dispatcher.BeginInvoke(new Action(() =>
            {
                SetupRequired.Visibility = Visibility.Visible;
                SetupRequired.Content = "No data presently available, trying again...";
            }));
        }

        private void ShowStatsIfEnabled()
        {
            if (_stateManager.StatsEnabled && _engine != null && !(_engine.screensaverExpired()))
            {
                ShowMessage(_engine.getRuntimeStatsInfo(), true);
            }
            else 
            {
                ShowMessage(null, false);
            }
        }

        private void ShowMessage(string? message, bool showStats)
        {
            UiMessageHelper.ShowMessage(SetupRequired, _stateManager.IsPaused, message, showStats);
        }

        public void ShowException(string msg)
        {
            AppLogger.LogError(new Exception("Engine exception raised"), msg);
        }

        #endregion

        #region User Input Handlers

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.Right:
                    _engine?.resetExpiredImageCollection();
                    UpdateImage();
                    break;

                case Key.M:
                    _engine?.toggleDefaultMute();
                    _tileRenderer?.ApplyGlobalMute(this, _engine?.isDefaultMute() ?? false);
                    break;

                case Key.S:
                    _stateManager.ToggleStats();
                    ShowStatsIfEnabled();
                    break;

                case Key.P:
                case Key.Space:
                    TogglePauseSlideshow();
                    break;

                case Key.Enter:
                    ReloadScreen();
                    break;

                case Key.R:
                    RepullAlbums();
                    break;

                case Key.W:
                    ToggleWindowMode();
                    break;

                case Key.U:
                    if (Keyboard.IsKeyDown(Key.LeftCtrl))
                    {
                        DoUpgrade();
                    }
                    break;

                case Key.Escape:
                case Key.Q:
                    Shutdown();
                    break;

                default:
                    if (!_stateManager.ScreensaverModeDisabled)
                    {
                        Shutdown();
                    }
                    break;
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Reserved for future use
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_stateManager.ScreensaverModeDisabled)
            {
                if (_mouseMonitor.RecordMouseMove())
                {
                    Shutdown();
                }
            }
            else
            {
                Cursor = _savedCursor ?? Cursors.Arrow;
            }
        }

        private void Image1_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (IsClickFromVideo(e))
            {
                e.Handled = true;
                return;
            }

            UpdateImage();
        }

        private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Reserved for future use
        }

        private void Window_SizeChanged_1(object sender, SizeChangedEventArgs e)
        {
            WindowWidth = Convert.ToInt32(e.NewSize.Width);
            WindowHeight = Convert.ToInt32(e.NewSize.Height);
            UpdateLayoutAndExpiredBanner();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            AppLogger.Log("Closing application");
            try
            {
                _engine?.shutdown();
                _imageUpdateService?.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, "Error during shutdown: " + ex.Message);
            }

            if (_engine != null)
            {
                AppOpenCloseLogger.logClosed(_engine.getUptime(), _engine.getRuntimeStatsInfo(false));
            }
        }

        #endregion

        #region UI Actions

        private void TogglePauseSlideshow()
        {
            if (_stateManager.IsPaused)
            {
                _imageUpdateService?.Resume();
            }
            else
            {
                _imageUpdateService?.Pause();
            }

            _stateManager.TogglePause();
            ShowStatsIfEnabled();
        }

        private void ToggleWindowMode()
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

        private void ReloadScreen()
        {
            _engine?.resetExpiredImageCollection();
            var totalImages = _gridHeight * _gridWidth;

            for (int i = 0; i < totalImages * 1.5; i++)
            {
                UpdateImage();
            }
        }

        private void RepullAlbums()
        {
            AppLogger.Log("Reloading library");
            InitializeEngine();
            if (_engine != null)
            {
                _engine.RestartCounter = ++_restartCounter;
            }
        }

        private void DoUpgrade()
        {
            var upgradeManager = UpgradeManager.Instance;

            if (!upgradeManager.ReadyForUpgrade)
            {
                Debug.WriteLine("Already upgraded");
                return;
            }

            var result = MessageBox.Show(
                "Do you want to continue with installing a new version?",
                "Upgrade confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                MessageBox.Show("After installation, please restart the application");
                Debug.WriteLine("Starting upgrade");
                upgradeManager.PerformUpgrade();
                Application.Current.Shutdown();
            }
            else
            {
                Debug.WriteLine("Deleting tmp installer");
                upgradeManager.deleteCurrentInstaller();
            }
        }

        #endregion

        #region Helper Methods

        private bool IsLoggedIn()
        {
            return _engine?.getLogin().login != string.Empty;
        }

        private Border GetGridBorder(int gridX, int gridY)
        {
            return TileGridBuilder.GetBorderAt(imageGrid, gridX, gridY);
        }

        private void CacheImageIfFirstTime(Bitmap bitmap, int gridX, int gridY)
        {
            int imageIndex = gridX + (gridY * GridWidth);

            if (_imageCounterArray[imageIndex] == 0)
            {
                try
                {
                    var fileName = Path.Combine(GetImageStorageLocation(), $"{imageIndex}.jpg");

                    if (File.Exists(fileName) && DoSmartStart)
                    {
                        File.Delete(fileName);
                    }

                    if (DoSmartStart)
                    {
                        using var temp = new Bitmap(bitmap);
                        temp.Save(fileName, ImageFormat.Jpeg);
                    }

                    _imageCounterArray[imageIndex]++;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError(ex, $"Failed to cache image: {ex.Message}");
                }
            }
        }

        private string GetImageStorageLocation()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures), "SmugAndy");
        }

        private void EnsureStorageDirectoryExists(string directory)
        {
            try
            {
                if (!Directory.Exists(directory) && DoSmartStart)
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError(ex, $"Failed to create storage directory: {ex.Message}");
            }
        }

        private BitmapImage GetInitialTileImage(int imageIndex, string storageDirectory)
        {
            const string fallback = "/andyScrSaver;component/2011072016-03-00IMG7066-L.jpg";
            return InitialImageProvider.Build(imageIndex, storageDirectory, DoSmartStart, fallback);
        }

        private bool IsClickFromVideo(MouseEventArgs e)
        {
            try
            {
                var dependencyObject = e.OriginalSource as DependencyObject;
                while (dependencyObject != null)
                {
                    if (dependencyObject is LibVLCSharp.WPF.VideoView)
                    {
                        return true;
                    }
                    dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
                }
            }
            catch
            {
                // Ignore errors during tree traversal
            }

            return false;
        }

        public DateTime GetLastMouseMove()
        {
            return _mouseMonitor.LastMouseMove;
        }

        #endregion

        #region Background Threads

        private static void MouseCursorResetLoop(object? state)
        {
            Debug.WriteLine($"Mouse cursor hide thread started at: {DateTime.Now}");

            var window = state as Window1;
            if (window == null) return;

            while (true)
            {
                try
                {
                    window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (window._mouseMonitor.ShouldHideCursor(DefaultCursorHideSeconds))
                        {
                            Mouse.SetCursor(Cursors.None);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Mouse cursor thread error: {ex.Message}");
                }

                Thread.Sleep(100);
            }
        }

        #endregion
    }
}
