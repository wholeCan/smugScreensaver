﻿using Microsoft.Win32;  //registry.
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
using System.Text;
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
        private const int minQ = 2;
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
#if (DEBUG) //debug on, used only for showing at runtime in case I forget which version is running.
    var debugOn = true;
#else
            var debugOn = false;
#endif
            var msg = new StringBuilder();
            msg.AppendLine("Time: " + DateTime.Now.ToShortDateString() + ", " + DateTime.Now.ToLongTimeString());
            msg.AppendLine("Running since " +
                TimeBooted.ToShortDateString()
                + " : "
                + TimeBooted.ToShortTimeString()
                );

            msg.AppendLine("Uptime: " + DateTime.Now.Subtract(TimeBooted).ToString());

            lock (ImageDictionary)
            {
                msg.AppendLine("Images: " + ImageDictionary.Count);
            }
            lock (AllAlbums)
            {
                msg.AppendLine("Albums: " + AllAlbums.Count);
            }
            msg.AppendLine("UserNameList: " + userNameListSize + ", " + string.Join(",", fetchUsersToLoad().ToList()));
            msg.AppendLine("Images shown: " + ImageCounter);
            msg.AppendLine("Images deduped: " + PlayedImages.Count);
            msg.AppendLine("Queue depth: " + ImageQueue.Count);
            msg.AppendLine("Image size: " + Settings.quality + " / " + fetchImageUrlSize());
            msg.AppendLine("Screensaver mode: " + isScreensaver.ToString());
            msg.AppendLine("Debug mode: " +  debugOn.ToString());
            msg.AppendLine("Time between images: " + getTimeSinceLast());
            msg.AppendLine("Exceptions raised: " + ExceptionsRaised);
            msg.AppendLine("Reloaded albums: " + RestartCounter + " times.");
            msg.AppendLine("Memory: " + Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024));
            msg.AppendLine("Peak memory: " + Process.GetCurrentProcess().PeakPagedMemorySize64 / (1024 * 1024));
            msg.AppendLine("Peak virtual memory: " + Process.GetCurrentProcess().PeakVirtualMemorySize64 / (1024 * 1024));
            msg.AppendLine("Schedule: " + Settings.startTime.ToString() + " - " + Settings.stopTime.ToString());
            msg.AppendLine("Version: " + (Assembly.GetEntryAssembly()?.GetName().Version).ToString());
            msg.AppendLine("Built: " + RetrieveLinkerTimestamp());
            if (showMenu)
            {//menu
                msg.AppendLine("Menu:");
                msg.AppendLine("\ts: show or hide stats");
                msg.AppendLine("\tw: toggle window controls");
                msg.AppendLine("\tr: reload library");
                msg.AppendLine("\tCtrl+U: upgrade app");
                msg.AppendLine("\tEnter: refresh all images");
                msg.AppendLine("\tp: pause slideshow");
                msg.AppendLine("\t<- or ->: show next photo");
                msg.AppendLine("\tESC or Q: exit program");
            }
            LastImageRequested = DateTime.Now;
            return msg.ToString().TrimEnd();
        }

        private static DateTime RetrieveLinkerTimestamp()
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

            var envelope = new authEnvelope();

            //todo: can I get build time variables from environment?
            // todo: look into https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-6.0&tabs=windows

            //salty tokens
            //var CONSUMERSECRET_SALTED_KEY = "SmugMugOAuthConsumerSecret";
            //var consumerTokenKey = "SmugMugOAuthConsumerToken";
            try
            {
                //can't encrypt this, as the current authentication is per machine.
                envelope.consumerToken = Constants.apiToken; //fetchKey(consumerTokenKey);
                envelope.consumerSecret = Constants.apiSecret;//fetchKey(CONSUMERSECRET_SALTED_KEY);
            }

            catch (Exception ex)
            {
                throw new ApplicationException("invalid token, sorry you lose.");
            }
            try
            {

                envelope.token =
                    Authenticator.Decrypt(
                    ReadRegistryValue(ACCESSTOKEN1, ""), Salt
                    );
                envelope.tokenSecret =
                    Authenticator.Decrypt(
                    ReadRegistryValue(ACCESSTOKENSECRET1, ""), Salt
                    );
            }
            catch (Exception ex)
            {
                //token is either wrong, or missing - so return empty string.
                envelope.token = "";
                envelope.tokenSecret = "";
            }
            return envelope;
        }

        private static SmugMugAPI AuthenticateUsingAnonymous()
        {
            //Access OAuth keys from App.config
            string consumerKey = null;
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var keySetting = config.AppSettings.Settings[CONSUMERTOKEN1];
            if (keySetting != null)
            {
                consumerKey = keySetting.Value;
            }

            if (String.IsNullOrEmpty(consumerKey))
            {
                throw new ConfigurationErrorsException("The OAuth consumer token must be specified in App.config");
            }

            //Connect to SmugMug using Anonymous access
            SmugMugAPI apiAnonymous = new(LoginType.Anonymous, new OAuthCredentials(consumerKey));
            return apiAnonymous;
        }
        private static string fetchKey(string key)
        {
            //todo: would be better if could fetch from registry
            string consumerSecret = null;
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var keySetting = config.AppSettings.Settings[key];
            if (keySetting != null)
            {
                consumerSecret = keySetting.Value;
            }
            if (String.IsNullOrEmpty(consumerSecret))
            {
                //   throw new ConfigurationErrorsException("The OAuth consumer token secret must be specified in App.config");
                return null;
            }
            return consumerSecret;
        }

        public static void writeAuthTokens(authEnvelope envelope)
        {
            WriteRegistryValue(ACCESSTOKEN1, Authenticator.Encrypt(envelope.token, Salt));
            WriteRegistryValue(ACCESSTOKENSECRET1, Authenticator.Encrypt(envelope.tokenSecret, Salt));
        }
        public static SmugMugAPI AuthenticateUsingOAuth(authEnvelope envelope)
        {


            OAuthCredentials oAuthCredentials = null;
            if (envelope.token != null && envelope.token != "")
            {
                oAuthCredentials = new OAuthCredentials(envelope.consumerToken, envelope.consumerSecret,
                    envelope.token, envelope.tokenSecret);
            }
            else
            {
                return null;
            }

            //Connect to SmugMug using oAuth
            SmugMugAPI apiOAuth = new SmugMugAPI(LoginType.OAuth, oAuthCredentials);
            return apiOAuth;
        }

        public authEnvelope AuthenticateUsingOauthNewConnection_part1(authEnvelope envelope)
        {
            var tokens = GenerateOAuthAccessToken_UI_PART1(envelope.consumerToken, envelope.consumerSecret);
            tokens.consumerToken = envelope.consumerToken;
            tokens.consumerSecret = envelope.consumerSecret;

            return tokens;
        }
        public bool AuthenticateUsingOauthNewConnection_part2(authEnvelope tokens, string six)
        {
            //attach debugger here?

            var token = GenerateOAuthAccessToken_UI_PART2(tokens, six);

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
        private static authEnvelope GenerateOAuthAccessToken_UI_PART1(string consumerKey, string secret)
        {
            string baseUrl = "http://api.smugmug.com";
            string requestUrl = "/services/oauth/1.0a/getRequestToken";
            string authorizeUrl = "/services/oauth/1.0a/authorize";
            string requestToken = null;
            string requestTokenSecret = null;

            #region Request Token
            var gRequest = OAuth.OAuthRequest.ForRequestToken(consumerKey, secret, "oob");

            gRequest.RequestUrl = baseUrl + requestUrl;
            string auth = gRequest.GetAuthorizationHeader();

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(gRequest.RequestUrl);
            request.Headers.Add("Authorization", auth);
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            Stream responseStream = response.GetResponseStream();
            StreamReader readStream = new StreamReader(responseStream, System.Text.Encoding.UTF8);
            string result = readStream.ReadToEnd();
            foreach (string token in result.Split('&'))
            {
                string[] splitToken = token.Split('=');

                switch (splitToken[0])
                {
                    case "oauth_token":
                        requestToken = splitToken[1];
                        break;
                    case "oauth_token_secret":
                        requestTokenSecret = splitToken[1];
                        break;
                    default:
                        break;
                }
            }
            response.Close();
            #endregion

            #region Authorization
            string authorizationUrl = String.Format("{0}{1}?mode=auth_req_token&oauth_token={2}&Access=Full&Permissions=Modify", baseUrl, authorizeUrl, requestToken);
            var ps = new ProcessStartInfo(authorizationUrl)
            {
                UseShellExecute = true,
                Verb = "open"
            };
            try
            {
                System.Diagnostics.Process.Start(ps);
            }
            catch (System.ComponentModel.Win32Exception noBrowser)
            {
                if (noBrowser.ErrorCode == -2147467259)
                {
                    throw new Exception("no browser");
                    //   MessageBox.Show(noBrowser.Message);
                }
            }
            catch (Exception e)
            {

                logMsg("exception: " + e.Message);
                throw e;
            }
            #endregion


            return new authEnvelope("", "", requestToken, requestTokenSecret);
        }

        //assuming part 1 fired, input the sixDigitCode - and complete authentication
        private static OAuthCredentials GenerateOAuthAccessToken_UI_PART2(authEnvelope envelope, string sixDigitCode)
        {
            string baseUrl = "http://api.smugmug.com";
            string accessUrl = "/services/oauth/1.0a/getAccessToken";


            string verifier = sixDigitCode;


            #region Access Token
            var gRequest = OAuth.OAuthRequest.ForAccessToken(
                envelope.consumerToken,
                envelope.consumerSecret,
                envelope.token,
                envelope.tokenSecret,
                verifier);
            gRequest.RequestUrl = baseUrl + accessUrl;
            var auth = gRequest.GetAuthorizationHeader();
            var request = (HttpWebRequest)WebRequest.Create(gRequest.RequestUrl);

            //todo: add exception here if auth fails.
            request.Headers.Add("Authorization", auth);
            var response = (HttpWebResponse)request.GetResponse();
            var responseStream = response.GetResponseStream();
            var readStream = new StreamReader(responseStream, System.Text.Encoding.UTF8);
            var result = readStream.ReadToEnd();
            foreach (string token in result.Split('&'))
            {
                string[] splitToken = token.Split('=');

                switch (splitToken[0])
                {
                    case "oauth_token":
                        envelope.token = splitToken[1];
                        break;
                    case "oauth_token_secret":
                        envelope.tokenSecret = splitToken[1];
                        break;
                    default:
                        break;
                }
            }
            response.Close();
            #endregion

            return new OAuthCredentials(
                envelope.consumerToken,
                envelope.consumerSecret,
                envelope.token,
                envelope.tokenSecret
                );
        }
#endregion

        private loginInfo _login;
        static Random r = new Random();
        public CSMEngine(bool doStart)
        {
            Settings = new CSettings();
            _imageQueue = new Queue<ImageSet>();
            GalleryTable = new System.Data.DataTable();
            _imageDictionary = new Dictionary<string, ImageSet>();  //image id, and url
            PlayedImages = new Dictionary<string, ImageSet>();
            GalleryTable.Columns.Add(new System.Data.DataColumn("Category", typeof(string)));
            GalleryTable.Columns.Add(new System.Data.DataColumn("Album", typeof(string)));
            AllAlbums = new List<Album>();
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
                // Setting
                string subKey = "SOFTWARE\\andysScreensaver\\login";
                using (RegistryKey rk = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
                {
                    using (var sk1 = rk.CreateSubKey(subKey))
                    {
                        // I have to use CreateSubKey 

                        // (create or open it if already exits), 
                        // 'cause OpenSubKey open a subKey as read-only

                        // Save the value
                        sk1.SetValue(KeyName.ToUpper(), Value);

                        sk1.Close();
                        rk.Close();
                        return true;
                    }
                }
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
            // Opening the registry key
            var subKey = "SOFTWARE\\andysScreensaver\\login";
            using (var rk = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default))
            {
                // Open a subKey as read-only

                using (var sk1 = rk.OpenSubKey(subKey))
                {
                    // If the RegistrySubKey doesn't exist -> (null)
                    if (sk1 == null)
                    {
                        return defValue;
                    }
                    else
                    {
                        try
                        {
                            // If the RegistryKey exists I get its value
                            // or null is returned.
                            return (string)sk1.GetValue(KeyName.ToUpper());
                        }
                        catch (Exception e)
                        {
                            logMsg(e.Message);
                            return null;
                        }
                        finally
                        {
                            sk1.Close();
                            rk.Close();
                        }
                    }
                }
            }
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
                Settings.speed_s = 3;
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
            var categories = new List<String>();

            GettingCategories = true;
            foreach (var album in AllAlbums)
            {
                if (!categories.Contains(getFolder(album)))
                {
                    categories.Add(getFolder(album));
                }
            }
            GettingCategories = false;
            return categories.ToArray();
            if (User != null)
            {
                var myCats = getCategories();
                foreach (var c in myCats)
                {
                    categories.Add(c);
                }
            }
            GettingCategories = false;
            return categories.ToArray();
        }

        bool gettingCategories = false;

        public string[] getCategories()
        {
            var categories = new List<string>();
            lock (this)
            {
                GettingCategories = true;
                throw new NotImplementedException();
            }
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
       

        private void runImageCollection()
        {
            if (Running == false)
            {
                Running = true;
                bool startIt = true;
                while (Running)
                {
                

                    if (ImageQueue.Count < MinQ)
                    {
                        startIt = true;
                    }
                    if (ImageQueue.Count < MaximumQ && startIt)
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
                }
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
            var list = usernameList.Split(',');
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
            var wakeupTime = 800;  // 8am,  (800 = 8am
            var goToBedTime = 1300;  //10PM,  2200=10pm)  

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
            var BytesToRead = 2500;
            var image = new BitmapImage();

            try
            {
                if (URL != null)
                {
                    var request = WebRequest.Create(new Uri(URL, UriKind.Absolute));
                    request.Timeout = -1;
                    try
                    {
                        var response = (HttpWebResponse)request.GetResponse();

                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            throw new Exception("image not returned: " + URL);
                        }
                        var responseStream = response.GetResponseStream();
                        var reader = new BinaryReader(responseStream);
                        var memoryStream = new MemoryStream();

                        var bytebuffer = new byte[BytesToRead];
                        var bytesRead = reader.Read(bytebuffer, 0, BytesToRead);
                        var sw = new Stopwatch();
                        sw.Start();
                        while (bytesRead > 0)
                        {
                            memoryStream.Write(bytebuffer, 0, bytesRead);
                            bytesRead = reader.Read(bytebuffer, 0, BytesToRead);
                        }

                        sw.Stop();
                        logMsg($"Get Image {URL} took: {sw.ElapsedMilliseconds}ms.");
                        image.BeginInit();
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        image.StreamSource = memoryStream;
                        image.EndInit();
                    }
                    catch (Exception ex)
                    {//possible http 404 response known, just skip and move on.
                        return null;
                    }
                }
            }


            catch (System.Net.WebException ex)
            {//no connection, wait longer.
                doException(ex.Message);
                logMsg(ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                doException(ex.Message);
                logMsg(ex.Message);
                logMsg(ex.StackTrace);
                return null;
            }
            return image;

        }

        private string fetchImageUrl(ImageSizes imageSize)
        {
            switch (Settings.quality)
            {

                case 0:
                    return imageSize.TinyImageUrl;
                case 1:
                    return imageSize.SmallImageUrl;
                case 2:
                    return imageSize.MediumImageUrl;
                case 3:
                    return imageSize.LargeImageUrl;
                case 4:
                    return imageSize.X3LargeImageUrl;
                case 5:
                    return imageSize.OriginalImageUrl;
                default:
                    return imageSize.MediumImageUrl;

            }
        }

        private string fetchImageUrlSize()
        {
            switch (Settings.quality)
            {

                case 0:
                    return "TinyImageUrl";
                case 1:
                    return "SmallImageUrl";
                case 2:
                    return "MediumImageUrl";
                case 3:
                    return "LargeImageUrl";
                case 4:
                    return "X3LargeImageUrl";
                case 5:
                    return "OriginalImageUrl";
                default:
                    return "MediumImageUrl";

            }
        }
        private async void loadImages(Album? a, bool singleAlbumMode, int size = 2)
        {
            if (a == null || a.Uris.AlbumImages == null)
            {
                return;
            }

            try
            {
                logMsg("loading album:" + a.Name);
                if (singleAlbumMode)
                {
                    lock (ImageDictionary)
                    {
                        ImageDictionary.Clear();
                    }
                }

                var images = await Api.GetAlbumImagesWithSizes(a, Debug_limit);

                if (images == null)
                {
                    doException("images is null!");
                }
                logMsg("loaded " + images.AlbumImages.Count() + " images from album " + a.Name);
                {
                    Parallel.ForEach(images.AlbumImages,
                       new ParallelOptions { MaxDegreeOfParallelism = 4 },
                       i =>
                       {
                           var imageSizes = images.ImageSizes.Where(x => x.Key.Contains(i.ImageKey));
                           var imageSize = imageSizes.First().Value.ImageSizes;
                           if (imageSize == null || i == null)
                           {
                               throw new Exception("null imagesize");
                           }
                           var imageUrl = fetchImageUrl(imageSize);
                           if (imageSizes != null && i.ImageKey != null)
                           {
                               lock (ImageDictionary)
                               {
                                   try
                                   {
                                       if (!ImageDictionary.ContainsKey(i.ImageKey))
                                       {
                                           ImageDictionary.Add(
                                                   i.ImageKey,
                                                   new ImageSet(
                                                       imageUrl,
                                                       string.IsNullOrEmpty(i.Caption) ? "" : i.Caption,
                                                       string.IsNullOrEmpty(i.FileName) ? "" : i.FileName,
                                                       i.Date == null ? DateTime.Now : i.Date,
                                                       string.IsNullOrEmpty(getFolder(a)) ? "" : getFolder(a),
                                                       string.IsNullOrEmpty(a.Name) ? "" : a.Name
                                                       )
                                                   );
                                       }
                                       else
                                       {
                                           logMsg("duplicate image: " + i.ImageKey);
                                       }
                                   }
                                   catch (ArgumentException ex)
                                   {
                                       doException("duplicate image: " + i.FileName + " : " + ex.Message);
                                   }
                                   catch (Exception ex)
                                   {
                                       doException(ex.Message);
                                   }
                               }
                           }
                           else
                           {
                               Console.WriteLine("andy");
                           }

                       });
                }
            }
            catch (Exception ex)
            {
                //relatively safe.
                //                    doException("loadImages: " + ex.Message);
                logMsg(ex.Message);
            }
        }



        public delegate void fireExceptionDel(string msg);
        public event fireExceptionDel fireException;

        List<ImageSet> _allImages = new List<ImageSet>();

        private bool isLoadingAlbums = true;
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

        Dictionary<string, ImageSet> playedImages = new();

        private ImageSet getRandomImage()
        {
            checkLogin(Envelope);
            {
                var imageSet = new ImageSet();
                //imageSet.Bm = null; //moved to constructor.

                lock (ImageDictionary)
                {
                    if (ImageDictionary.Count > 0)//|| playedImages.Count > 0)
                                                  // only enter if images either are loaded, or have completed at some point in the past.
                    {
                        try
                        {
                            var myQuality = ImageQueue.Count > 0 ? settings.quality : 1;  //allow low res for first pics.

                            var imageIndex = R.Next(ImageDictionary.Count);
                            var key = ImageDictionary.Keys.ElementAt(imageIndex);
                            var element = ImageDictionary[key];  //optimizing to avoid multiple lookups.
                            ImageDictionary.Remove(key);
                            if (!PlayedImages.ContainsKey(key))
                            {
                                PlayedImages.Add(key, element);
                            }
                            var image = showImage(element.ImageURL);
                            if (image == null)
                            {
                                throw new Exception("image returned is null: " + element.ImageURL);
                            }
                            imageSet.BitmapImage = image;
                            imageSet.Name = element.Name;
                            imageSet.AlbumTitle = element.AlbumTitle;
                            imageSet.ImageURL = element.ImageURL;
                            imageSet.Category = element.Category;
                            imageSet.MyDate = element.MyDate;
                            imageSet.AlbumTitle = element.AlbumTitle; //element.Album.Title;
                            imageSet.Caption = element.Caption;
                            imageSet.Exif = element.Exif;
                        }
                        catch (Exception ex)
                        {
                            // doException("random: " + ex.Message);
                            //most likely a failed image download for some reason.  Turn off logger for now.
                            logMsg(ex.Message + "\r\n" + ex.StackTrace);
                        }
                    }
                    else if ((PlayedImages.Count > 0) && !IsLoadingAlbums1)
                    {// if we're out of images, and loading is completed - then let's start a new load.
                        Task.Factory.StartNew(() =>
                        {
                            logMsg("reloading library!!!");
                            rePullAlbums();
                        });
                        return null;
                    }
                    else { return null; }
                }
                return imageSet;
            }
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
