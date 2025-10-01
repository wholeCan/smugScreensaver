using Microsoft.Win32;  //registry.
using SmugMug.NET;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;


namespace SMEngine
{

    public partial class CSMEngine
    {
        private static DateTime timeStarted = DateTime.Now;
        private readonly static DateTime timeBooted = DateTime.Now;

        private readonly Dictionary<String, ImageSet> _imageDictionary;
        private static Int64 imageCounter = 0;

        private SmugMugAPI api = null;
        private User _user = null;
        private System.Data.DataTable galleryTable;
        private static List<Album> _allAlbums;
        private readonly Queue<ImageSet> _imageQueue;        

        private const int maximumQ = 20;   //window, only download if q is less than max and greater than min.
        private const int minQ = 2; //2, to allow some time to download albums before getting to 0.
        private volatile bool running = false;


#if (DEBUG) //max albums to pull
        private readonly int debug_limit = 50;
#else
        private int debug_limit = 5000000;
#endif

        private authEnvelope _envelope;
        private int exceptionsRaised = 0;

        #region NEW AUTH STUFF 2022

        const string CONSUMERTOKEN = "SmugMugOAuthConsumerToken";
        const string ACCESSTOKEN = "SmugmugOauthAccessToken";
        const string ACCESSTOKENSECRET = "SmugmugOauthAccessTokenSecret";

        private DateTime lastImageRequested = DateTime.Now;

        private int restartCounter = 0;
        private void rePullAlbums()
        {
            RestartCounter++;
            loadAllImages();
        }

        public void RePullAlbumsSafe()
        {
            rePullAlbums();
        }

        private async void setupJob()
        {
# if(DEBUG) // time of day to reset.
            var frequencyMinutes = 24.0 * 60.0;/// 24; //24 = 1 per day.  1 = 1 per hour
            var startHour = 2;// DateTime.Now.Hour;
            var startMinute = 15;// DateTime.Now.Minute + 1;
#else
            var frequencyMinutes = 24.0 * 60;// run once per day
            var startHour = 2;
            var startMinute = 15;
#endif

          /**
           TaskScheduler.Instance.ScheduleTask(startHour, startMinute, frequencyMinutes,  //run at 2:15a daily
               () =>
               {
                  // AppOpenCloseLogger.Log("scheduled task execution");
                   logMsg("reloading library!!!");
                   rePullAlbums();
                   //do the thing!
               });
          */

        }

        public string getTimeSinceLast()
        {
            var timeSince = DateTime.Now.Subtract(LastImageRequested);
            return timeSince.TotalSeconds.ToString("0.00");
        }

        public string getRuntimeStatsInfo(bool showMenu = true)
        {
            return StatsFormatter.Build(this, showMenu);
        }

        internal DateTime RetrieveLinkerTimestamp()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            AssemblyName assemblyName = assembly.GetName();

            const int PeHeaderOffset = 60;
            const int LinkerTimestampOffset = 8;

            byte[] buffer = new byte[2048];

            using (System.IO.FileStream fileStream = new System.IO.FileStream(assembly.Location, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                fileStream.Read(buffer, 0, 2048);
            }

            int offset = BitConverter.ToInt32(buffer, PeHeaderOffset);
            int secondsSince1970 = BitConverter.ToInt32(buffer, offset + LinkerTimestampOffset);
            DateTime epochTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime buildDate = epochTime.AddSeconds(secondsSince1970);

            return buildDate.ToLocalTime();
        }

        public authEnvelope getCode()
        {
            return AuthHelper.ReadTokensFromRegistry();
        }

        private static SmugMugAPI AuthenticateUsingAnonymous()
        {
            return AuthHelper.AuthenticateUsingAnonymous();
        }
        private static string fetchKey(string key)
        {
            string consumerSecret = null;
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var keySetting = config.AppSettings.Settings[key];
            if (keySetting != null)
            {
                consumerSecret = keySetting.Value;
            }
            if (String.IsNullOrEmpty(consumerSecret))
            {
                return null;
            }
            return consumerSecret;
        }

        public static void writeAuthTokens(authEnvelope envelope)
        {
            AuthHelper.WriteTokensToRegistry(envelope);
        }
        public static SmugMugAPI AuthenticateUsingOAuth(authEnvelope envelope)
        {
            return AuthHelper.AuthenticateUsingOAuth(envelope);
        }

        public authEnvelope AuthenticateUsingOauthNewConnection_part1(authEnvelope envelope)
        {
            var tokens = AuthHelper.GenerateOAuthAccessToken_UI_PART1(envelope.consumerToken, envelope.consumerSecret);
            tokens.consumerToken = envelope.consumerToken;
            tokens.consumerSecret = envelope.consumerSecret;

            return tokens;
        }
        public bool AuthenticateUsingOauthNewConnection_part2(authEnvelope tokens, string six)
        {
            var token = AuthHelper.GenerateOAuthAccessToken_UI_PART2(tokens, six);

            tokens.token = token.AccessToken;
            tokens.tokenSecret = token.AccessTokenSecret;
            writeAuthTokens(tokens);
            SmugMugAPI apiOAuth = new SmugMugAPI(LoginType.OAuth, token);
            Api = apiOAuth;
            Loggedin = true;
            return true;
        }

        public void logout()
        {
            var a = new authEnvelope("", "", "", "");
            writeAuthTokens(a);
        }

        //fire off the web request to authenticate
        // moved to AuthHelper

        //assuming part 1 fired, input the sixDigitCode - and complete authentication
        // moved to AuthHelper
#endregion

        private loginInfo _login;
        static Random r = new Random();
        public CSMEngine(bool doStart)
        {


            
            _imageQueue = new Queue<ImageSet>();
            GalleryTable = new System.Data.DataTable();
            _imageDictionary = new Dictionary<string, ImageSet>();  //image id, and url
            PlayedImages = new Dictionary<string, ImageSet>();
            GalleryTable.Columns.Add(new System.Data.DataColumn("Category", typeof(string)));
            GalleryTable.Columns.Add(new System.Data.DataColumn("Album", typeof(string)));
            AllAlbums = new List<Album>();
            Settings = new CSettings();
            Login = new loginInfo();

            loadConfiguration();
            if (doStart)
            {
                start();
            }
            setupJob();

           
        }
        public CSMEngine() : this(true)
        {

        }

    

        CSettings _settings;
        public void setSettings(CSettings set)
        {
            Settings = set;
        }

        public bool checkCategoryForAlbums(string category)
        {
            //we are checking to see if there are any albums in this category
            var albums = AllAlbums.FirstOrDefault(x => getFolder(x).Equals(category));
            if (albums == null)
            {
                logMsg("albums was returned as false for some reason");
                return false;
            }
            return true;
        }

        public void saveConfiguration()
        {
            saveGalleries();
            saveSettings();
        }
        
        public static bool WriteRegistryValue(string KeyName, object Value)
        {
            if (Value == null)
                return false;
            try
            {
                return RegistryHelper.WriteString(KeyName, Value.ToString());
            }
            catch (Exception e)
            {
                logMsg(e.Message);
                //doException(e.Message);
                return false;
            }
        }
        private static string ReadRegistryValue(string KeyName, string defValue)
        {
            return RegistryHelper.ReadString(KeyName, defValue);
        }

        private static int salt = 0xbad7eed;


        public void saveSettings()
        {
            WriteRegistryValue("quality", Settings.quality.ToString());
            WriteRegistryValue("Speed_S", Settings.speed_s.ToString());
            WriteRegistryValue("LoadAll", Settings.load_all ? 1.ToString() : 0.ToString());
            WriteRegistryValue("ShowInfo", Settings.showInfo ? 1.ToString() : 0.ToString());

            WriteRegistryValue("gridH", Settings.gridHeight.ToString());
            WriteRegistryValue("gridW", Settings.gridWidth.ToString());
            WriteRegistryValue("borderT", Settings.borderThickness.ToString());

        }
        public void loadSettings()
        {
            try
            {
                Settings.quality = Int32.Parse(ReadRegistryValue("quality", "2"));
#if (DEBUG) //use custom speed setting
                Settings.speed_s = 6;
#else
                Settings.speed_s = Int32.Parse(ReadRegistryValue("Speed_S", "5"));
#endif
                var loadAll = Int32.Parse(ReadRegistryValue("LoadAll", "1"));
                var showInfo = Int32.Parse(ReadRegistryValue("ShowInfo", "1"));
                Settings.load_all = loadAll == 1 ? true : false;
                Settings.showInfo = showInfo == 1 ? true : false;

                Settings.gridHeight = Int32.Parse(ReadRegistryValue("gridH", "3"));
                Settings.gridWidth = Int32.Parse(ReadRegistryValue("gridW", "4"));
                Settings.borderThickness = Int32.Parse(ReadRegistryValue("borderT", "1"));
            }
            catch (Exception ex)
            {
                doException(ex.Message);
                logMsg(ex.Message);
                Settings = new CSettings();
            }
        }
        public CSettings settings
        {
            get
            {
                return Settings;
            }
        }
        
        public loginInfo getLogin()
        {
            return Login;
        }
        private void loadGalleries()
        {
            int galleries_present = 0;
            try
            {
                galleries_present = Int32.Parse(ReadRegistryValue("GalleryCount", "12"));
            }
            catch (Exception ex)
            {
                logMsg(ex.Message);
                doException(ex.Message);
                galleries_present = 0;

            }
            for (int i = 0; i < galleries_present; i++)
            {
                var g = new GalleryEntry();
                g.category = ReadRegistryValue("Cat_" + i.ToString(), "def");
                g.gallery = ReadRegistryValue("Gal_" + i.ToString(), "def");
                if (g.category != "def" && g.category != null)
                {
                    addGallery(g.category, g.gallery);
                }
                else
                {
                    logMsg("Skipping default category!");
                }
            }


        }
        private void saveGalleries()
        {
            int galleries_present = GalleryTable.Rows.Count;
            WriteRegistryValue("GalleryCount", galleries_present.ToString());
            for (int i = 0; i < galleries_present; i++)
            {
                var g = new GalleryEntry();
                g.category = GalleryTable.Rows[i].ItemArray[0].ToString();
                g.gallery = GalleryTable.Rows[i].ItemArray[1].ToString();
                WriteRegistryValue("Cat_" + i.ToString(), g.category);
                WriteRegistryValue("Gal_" + i.ToString(), g.gallery);

            }
        }

        private void loadConfiguration()
        {
            int.TryParse(fetchKey("startTime"), out Settings.startTime);
            int.TryParse(fetchKey("stopTime"), out Settings.stopTime);
            loadGalleries();
            loadSettings();
        }
        public void addGallery(string cat, string gal)
        {
            GalleryTable.Rows.Add(cat, gal);
        }

        public string[] getCategoriesAsync()
        {
            // Return unique category names based on currently loaded albums
            return getCategories();
        }

        bool gettingCategories = false;

        public string[] getCategories()
        {
            var categories = new List<string>();
            try
            {
                GettingCategories = true;
                lock (AllAlbums)
                {
                    foreach (var album in AllAlbums)
                    {
                        var folder = getFolder(album);
                        if (!string.IsNullOrEmpty(folder) && !categories.Contains(folder))
                        {
                            categories.Add(folder);
                        }
                    }
                }
            }
            finally
            {
                GettingCategories = false;
            }
            return categories.ToArray();
        }


        public string getFolder(Album album)
        {
            if (album.Uris == null || album.Uris.Folder == null)
            {
                logMsg("album is null!");
                return "";
            }
            var fullPath = album.Uris.Folder.Uri.ToString();
            var category = fullPath.Substring(fullPath.LastIndexOf('/') + 1);
            return category;
        }

        public string[] getAlbums(string category)
        {
            var catAlbums = new List<string>();
            foreach (Album album in AllAlbums)
            {
                if (getFolder(album).Equals(category))
                {
                    catAlbums.Add(album.Name);
                }
            }
            return catAlbums.ToArray();
        }
        public void addAllAlbums()
        {
            GalleryTable.Clear();
            foreach (Album a in AllAlbums)
            {
                addGallery(getFolder(a), a.Name);
            }
        }

        public void addAllAlbums(string byCategoryName)
        {
            var albums = AllAlbums.Where(x => getFolder(x) == byCategoryName);
            foreach (var a in albums)
            {
                addGallery(getFolder(a), a.Name);
            }
        }

        private bool _loggedin = false;
        public bool checkLogin(authEnvelope e)
        {
            return Loggedin;
        }

        public bool login(authEnvelope envelope)
        {
            Envelope = envelope;

            try
            {
                Loggedin = false;
                if (Api != null) return true;
                Api = AuthenticateUsingOAuth(envelope);
               
                if (Api == null)
                    return false;
                Loggedin = true; // used for thread control.
                //note: the old login would also load albums and load the user, it's not clear we want to do this yet.
                return true;
            }
            catch (Exception ex)
            {
                doException("login: " + ex.Message);
                return false;
            }
        }

        public void shutdown()
        {
            tracker.shutdown();
        }
        private Tracker tracker = new Tracker();

        private async void loadAlbums(string userNickName = null)
        {
            try
            {
                if (userNickName != null)
                {
                    User = await Api.GetUser(userNickName);
                }
                else
                {
                    User = await Api.GetAuthenticatedUser();
                    
                }

                tracker.phoneHome(new TrackerDetails { AppName = "andyScreenSaver", Host = Dns.GetHostName(), Username = User.NickName });

                var albums = await Api.GetAlbums(User, Debug_limit);
                logMsg("returned albums: " + albums.Count());
                lock (AllAlbums)
                {
                    AllAlbums.AddRange(albums.Take(Debug_limit));
                }


            }
            catch (Exception ex)
            {
                doException(ex.Message);
            }
        }


        public int qSize
        {
            get
            {
                return ImageQueue.Count;
            }
        }

        //public static DateTime TimeStarted { get => timeStarted; set => timeStarted = value; }

        public static DateTime TimeBooted => timeBooted;

        public Dictionary<string, ImageSet> ImageDictionary => _imageDictionary;

        public static long ImageCounter { get => imageCounter; set => imageCounter = value; }
        public SmugMugAPI Api { get => api; set => api = value; }
        public User User { get => _user; set => _user = value; }
        public DataTable GalleryTable { get => galleryTable; set => galleryTable = value; }
        public static List<Album> AllAlbums { get => _allAlbums; set => _allAlbums = value; }

        public Queue<ImageSet> ImageQueue => _imageQueue;

        public static int MaximumQ => maximumQ;

        public static int MinQ => minQ;

        public bool Running { get => running; set => running = value; }

        public int Debug_limit => debug_limit;

        public authEnvelope Envelope { get => _envelope; set => _envelope = value; }
        public int ExceptionsRaised { get => exceptionsRaised; set => exceptionsRaised = value; }

        public static string CONSUMERTOKEN1 => CONSUMERTOKEN;

        public static string ACCESSTOKEN1 => ACCESSTOKEN;

        public static string ACCESSTOKENSECRET1 => ACCESSTOKENSECRET;

        public DateTime LastImageRequested { get => lastImageRequested; set => lastImageRequested = value; }
        public int RestartCounter { get => restartCounter; set => restartCounter = value; }
        public loginInfo Login { get => _login; set => _login = value; }
        public static Random R { get => r; set => r = value; }
        public CSettings Settings { get => _settings; set => _settings = value; }
        public static int Salt { get => salt; set => salt = value; }
        public bool GettingCategories { get => gettingCategories; set => gettingCategories = value; }
        public bool Loggedin { get => _loggedin; set => _loggedin = value; }
        public ThreadStart Ts { get => ts; set => ts = value; }
        public Thread imageCollectionThread { get => t; set => t = value; }
        public ThreadStart TsAlbumLoad { get => tsAlbumLoad; set => tsAlbumLoad = value; }
        public Thread TAlbumLoad { get => tAlbumLoad; set => tAlbumLoad = value; }
        public DateTime? TimeWentBlack { get => timeWentBlack; set => timeWentBlack = value; }
        public bool Expired { get => expired; set => expired = value; }
        public double W { get => w; set => w = value; }
        public double H { get => h; set => h = value; }
        public List<ImageSet> AllImages { get => _allImages; set => _allImages = value; }
        public bool IsLoadingAlbums1 { get => isLoadingAlbums; set => isLoadingAlbums = value; }
        public Dictionary<string, ImageSet> PlayedImages { get => playedImages; set => playedImages = value; }

        private static void logMsg(string msg)
        {
            Debug.WriteLine(DateTime.Now.ToLongTimeString() + ": " + msg);
        }
      

        // Backing field for PlayedImages
        private Dictionary<string, ImageSet> playedImages = new Dictionary<string, ImageSet>();

        private void runImageCollection()
        {
            if (Running == false)
            {
                Running = true;
                bool startIt = true;
                while (Running)
                

                    /*if (qSize < MinQ)
                    {
                        startIt = true;
                    }*/
                    if (qSize<MinQ && qSize < MaximumQ)
                    {
                        try
                        {
                            if (!screensaverExpired())  //new test, ensuring not pulling image while asleep
                            {
                                var imageSet = getRandomImage();
                                if (imageSet != null)
                                {
                                    lock (ImageQueue)
                                    {
                                        ImageQueue.Enqueue(imageSet);
                                        logMsg($"Image queue depth: {qSize}");
                                    }
                                }
                            }
                            else
                            {//wait for thread to wake up!
                                logMsg("Sleeping while waiting to wake up");
                                Thread.Sleep(5000);
                            }
                        }
                        catch (Exception ex)
                        {
                            doException(ex.Message);
                            logMsg(ex.Message);
                            Running = false;
                            logMsg("Invalid login");
                        }
                    }
                    else
                    {
                        startIt = false;
                    }

                    System.Threading.Thread.Sleep(50);//don't overrun processor.
                Running = false;//reset if stopped for any reason.
            }
        }
        ThreadStart ts = null;
        Thread t = null;

        ThreadStart tsAlbumLoad = null;
        Thread tAlbumLoad = null;

        private int userNameListSize = 0;
        private string[] fetchUsersToLoad()
        {
            var usernameList = fetchKey("UserNameList");
            Debug.Assert(!string.IsNullOrEmpty(usernameList));
            if (usernameList == null)
            {
                usernameList = @"MY_NAME";
            }
            var list = usernameList.Split(',').Select(u => u.Trim()).ToArray();
            userNameListSize = list.Count();
            return list;
        }
        private void start()
        {
            try
            {
                Envelope = getCode();
            }
            catch (Exception ex)
            {
                return;
            }
            if (TAlbumLoad == null)
            {
                TsAlbumLoad = new ThreadStart(loadAllImages);
                TAlbumLoad = new Thread(TsAlbumLoad)
                {
                    IsBackground = true
                };
                TAlbumLoad.Start();
            }
            //System.Threading.Thread.Sleep(50);//experimental.
            if (imageCollectionThread == null)
            {
                Ts = new ThreadStart(runImageCollection);
                imageCollectionThread = new Thread(Ts)
                {
                    IsBackground = true
                };
                imageCollectionThread.Start();
            }

        }

        private System.Drawing.Bitmap BitmapImage2Bitmap(BitmapImage bitmapImage)
        {
            System.Drawing.Bitmap bitmap = null;
            if (bitmapImage != null)
            {
                try
                {
                    using var outStream = new MemoryStream();
                    var enc = new BmpBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                    enc.Save(outStream);
                    bitmap = new System.Drawing.Bitmap(outStream);
                    outStream.Close();
                }
                catch (Exception ex)
                {   //do nothing.
                    //doException("bm2m: " + ex.Message);
                    logMsg(ex.Message);
                }
            }
            return bitmap;

        }

        DateTime? timeWentBlack = null;

        public bool warm()
        {
            var warmupTime = 60; //seconds, don't black out images until alive for a minute.
            return DateTime.Now.Subtract(timeStarted).TotalSeconds > warmupTime;
        }

        //how is this used vs isExpired?
        public bool screensaverExpired()
        {

#if (DEBUG) //minutes to run when timing out
            var minutesToRun = 1;//put back to 30
#else
            var minutesToRun = 30;
#endif
            var maxRuntimeSeconds = minutesToRun * 60;  //only run for an hour to conserve smugmugs api.

            var timeOfDay = DateTime.Now.Hour * 100 + DateTime.Now.Minute;

            var totalRuntimeSeconds = DateTime.Now.Subtract(manualInteruptExpiration).TotalSeconds;
            //logMsg("runtime is:" + totalRuntimeSeconds.ToString("0.00"));
            //we want to allow to run for a couple hours if manually woken up.
#if (DEBUG)
            var wakeupTime = 400;  // 8am,  (800 = 8am
            var goToBedTime = 2300;  //10PM,  2200=10pm)  

#else
            var wakeupTime = Settings.startTime;  // 8am,  (800 = 8am
            var goToBedTime = Settings.stopTime;  //10PM,  2200=10pm)  
#endif
            //debug test values, manually set these as a really dumb unit test.
            //timeOfDay = 2400;
            //totalRuntimeSeconds = 70;

            Expired = (totalRuntimeSeconds > maxRuntimeSeconds) &&
                !(timeOfDay >= wakeupTime && timeOfDay < goToBedTime);  //for testing, let it run a couple hours. then see if it wakes back up at 2p.

            return Expired;
        }


        DateTime manualInteruptExpiration = DateTime.Now;
        //todo: delete this
        private bool defaultMute = true;
        public void toggleDefaultMute()
        {
            defaultMute = !defaultMute;
        }

        public bool isDefaultMute()
        {
            return defaultMute;
        }
        public void resetExpiredImageCollection()
        {
            if (Expired)
            {// we only want to set this if expired, to avoid always resetting the time set.
                //timeStarted = DateTime.Now;
                manualInteruptExpiration = DateTime.Now;
                TimeWentBlack = null;
                Expired = false;
            }
        }
        private bool expired = false;
        public ImageSet getImage()
        {
            var b = new ImageSet();
            logMsg("fetching image...");
            lock (ImageQueue)
            {
                if (qSize > 0)
                {
                    b = ImageQueue.Dequeue();
                    logMsg($"Dequeue, Image queue depth: {qSize}");
                    ImageCounter++;
                }
            }
            if (!screensaverExpired())
            {
                b.Bitmap = BitmapImage2Bitmap(b.BitmapImage);
            }
            else
            {
                b.Bitmap = getBlackImagePixel();
            }
            return b;
        }

        public System.Drawing.Bitmap getBlackImagePixel()
        {
            //just generate a black image

            if (TimeWentBlack == null)
            {
                TimeWentBlack = DateTime.Now;
            }
            var bb = new System.Drawing.Bitmap(1, 1);
            bb.SetPixel(0, 0, System.Drawing.Color.Black);
            logMsg("Setting a black pixel!");

            return bb;
        }


        private double w = 0, h = 0;
        public void setScreenDimensions(double _w, double _h)
        {
            W = _w;
            H = _h;
        }
        private System.Drawing.Bitmap cropAtRect(System.Drawing.Bitmap sourceBitmap, System.Drawing.Rectangle r)
        {
            var newBitmap = new System.Drawing.Bitmap(r.Width, r.Height);
            using (var g = System.Drawing.Graphics.FromImage(newBitmap))
            {
                g.DrawImage(sourceBitmap, -r.X, -r.Y); //todo, experiment - does it help to mamke this (sourceBitmap, 0, 0) Unclear it's even used.
            }
            return newBitmap;
        }



        private BitmapImage showImage(string URL)
        {
            return ImageLoader.DownloadImage(this, URL);
        }

        private string fetchImageUrl(ImageSizes imageSize)
        {
            return ImageLoader.GetBestImageUrl(this, imageSize);
        }

        private async void loadImages(Album? a, bool singleAlbumMode, int size = 2)
        {
            if (a == null || a.Uris.AlbumImages == null) return;
            await ImageLoader.LoadImagesForAlbum(this, a, singleAlbumMode, size);
        }

        public delegate void fireExceptionDel(string msg);
        public event fireExceptionDel fireException;

        List<ImageSet> _allImages = new List<ImageSet>();

        private bool isLoadingAlbums = false; //to prevent multiple simultaneous loads.
        public bool IsLoadingAlbums()
        {
            return IsLoadingAlbums1;
        }
        private bool isConfigurationMode = false;

        public bool IsConfigurationMode
        {
            set
            {
                isConfigurationMode = value;
            }
            get
            {
                return isConfigurationMode;
            }
        }
        private void loadAllImages()
        {
            if (IsLoadingAlbums1)
            {
                Debug.WriteLine($"Already loading albums!");
                return;
            }
            IsLoadingAlbums1 = true;
            AllAlbums = new List<Album>();
            PlayedImages = new Dictionary<string, ImageSet>();
            try
            {
                while (Loggedin == false)
                {// I don't think I really want to do this. what if running app, we would probably want to start up config.
                    Thread.Sleep(100); //waiting for login to complete.
                }
                if (checkLogin(Envelope))
                {

                    foreach (var username in fetchUsersToLoad())
                    {
                        if (username == @"MY_NAME")
                        {
                            loadAlbums();
                        }
                        else
                        {
                            loadAlbums(username);
                        }
                    }


                    if (Settings.load_all)
                    {
                        var ary = getCategoriesAsync();
                        var rnd = new Random();
                        var shuffledCats = ary.OrderBy(x => rnd.Next()).ToList();

                        var shuffledAlbums = AllAlbums.OrderBy(x => rnd.Next()).ToList();

                        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        Parallel.ForEach(
                            shuffledAlbums,
                               new ParallelOptions { MaxDegreeOfParallelism = 4 },
                            a =>
                          {

                              if (getFolder(a).Contains("Surveilance"))
                              {
                                  logMsg("Throwing out surveilance");
                              }
                              else
                              {
                                 // if (!screensaverExpired())
                                  {
                                      try
                                      {
                                          if (!IsConfigurationMode)
                                          {
                                              loadImages(a, false);
                                          }
                                      }
                                      catch (Exception ex)
                                      {
                                          doException(ex.Message);
                                      }
                                  }
                                  /*else
                                  {
                                      logMsg("Skipping loadImages - expired");
                                  }
                                  */
                              }
                          });

                        logMsg($"Time to load all albums: {sw.ElapsedMilliseconds} milliseconds");
                    }
                    else
                    {
                        //we are loading only selected albums, based on configuration!
                        Album a = null;
                        if (GalleryTable.Rows.Count > 0)
                        {
                            for (int i = 0; i < GalleryTable.Rows.Count; i++)
                            {
                                var cat = GalleryTable.Rows[i].ItemArray[0].ToString();
                                var gal = GalleryTable.Rows[i].ItemArray[1].ToString();
                                a = AllAlbums.FirstOrDefault(x => getFolder(x) == cat && x.Name == gal);
                                if (a != null)
                                {
                                    loadImages(a, false);//load single album from gallery.
                                }
                            }
                        }


                    }
                }
            }
            catch (Exception ex)
            {
                doException(ex.Message);
                logMsg(ex.Message);
                //invalid login.  Stopping.
                logMsg("invalid login.  Stopping.");
            }
            finally
            {
                IsLoadingAlbums1 = false;
            }

        }

        private ImageSet getRandomImage()
        {
            return ImageSelectionHelper.TryGetRandomImage(this);
        }
        public void doException(string msg)
        {
            ExceptionsRaised++;
            if (fireException != null)
            {
                fireException(msg);
            }
        }
    }

}
