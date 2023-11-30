using Microsoft.Win32;  //registry.
using SmugMug.NET;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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

        private const int maximumQ = 10;   //window, only download if q is less than max and greater than min.
        private const int minQ = 2;
        private volatile bool running = false;


#if (DEBUG)
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
# if(DEBUG)
            var frequencyHours = 24;/// 24; //24 = 1 per day.  1 = 1 per hour
            var startHour = 15;// DateTime.Now.Hour;
            var startMinute = 15;// DateTime.Now.Minute + 1;
#else
            var frequencyHours = 24;// run once per day
            var startHour = 2;
            var startMinute = 15;
#endif

            TaskScheduler.Instance.ScheduleTask(startHour, startMinute, frequencyHours,  //run at 2:15a daily
               () =>
               {
                   logMsg("reloading library!!!");
                   rePullAlbums();
                   //do the thing!
               });

        }

        public string getTimeSinceLast()
        {
            var timeSince = DateTime.Now.Subtract(LastImageRequested);
            return timeSince.TotalSeconds.ToString("0.00");
        }

        public string getRuntimeStatsInfo()
        {
            var msg = new StringBuilder();
            msg.AppendLine("running since " +
                TimeBooted.ToShortDateString()
                + " : "
                + TimeBooted.ToShortTimeString()
                );

            msg.AppendLine("Uptime: " + DateTime.Now.Subtract(TimeBooted).ToString());

            lock (ImageDictionary)
            {
                msg.AppendLine("images: " + ImageDictionary.Count);
            }
            lock (AllAlbums)
            {
                msg.AppendLine("albums: " + AllAlbums.Count);
            }
            msg.AppendLine("images shown: " + ImageCounter);
            msg.AppendLine("images deduped: " + PlayedImages.Count);
            msg.AppendLine("queue depth: " + ImageQueue.Count);
            msg.AppendLine("image size: " + Settings.quality + " / " + fetchImageUrlSize());
            msg.AppendLine("time between images: " + getTimeSinceLast());
            msg.AppendLine("exceptions raised: " + ExceptionsRaised);
            msg.AppendLine("reloaded albums: " + RestartCounter + " times.");
            msg.AppendLine("memory: " + Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024));
            msg.AppendLine("Peak memory: " + Process.GetCurrentProcess().PeakPagedMemorySize64 / (1024 * 1024));
            msg.AppendLine("Peak virtual memory: " + Process.GetCurrentProcess().PeakVirtualMemorySize64 / (1024 * 1024));
            msg.AppendLine("Schedule: " + Settings.startTime.ToString() + " - " + Settings.stopTime.ToString() +
                "Menu:");
            msg.AppendLine("\ts: show or hide stats");
            msg.AppendLine("\tw: show window controls");
            msg.AppendLine("\tb: go back to borderless mode");
            msg.AppendLine("\tr: reload library");
            msg.AppendLine("\t<- or ->: show next photo");
            msg.AppendLine("\tESC or Q: exit program");
            LastImageRequested = DateTime.Now;
            return msg.ToString();
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
        //static OAuth.OAuthRequest gRequest;
        private static OAuthCredentials GenerateOAuthAccessToken(string consumerKey, string secret)
        {
            throw new NotImplementedException("this is for console use only!");
            string baseUrl = "http://api.smugmug.com";
            string requestUrl = "/services/oauth/1.0a/getRequestToken";
            string authorizeUrl = "/services/oauth/1.0a/authorize";
            string accessUrl = "/services/oauth/1.0a/getAccessToken";


            string requestToken = null;
            string requestTokenSecret = null;
            string accesstoken = null;
            string accessTokenSecret = null;

            #region Request Token
            var oAuthRequest = OAuth.OAuthRequest.ForRequestToken(consumerKey, secret, "oob");

            oAuthRequest.RequestUrl = baseUrl + requestUrl;
            string auth = oAuthRequest.GetAuthorizationHeader();

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(oAuthRequest.RequestUrl);
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
            System.Diagnostics.Process.Start(ps);

            //todo: once we fire off the web request, we should get the code from the wpf form.
            // then we can continue the rest of the code.

            Console.WriteLine("Enter the six-digit code: ");
            string verifier = Console.ReadLine();
            #endregion

            #region Access Token
            oAuthRequest = OAuth.OAuthRequest.ForAccessToken(consumerKey, secret, requestToken, requestTokenSecret, verifier);
            oAuthRequest.RequestUrl = baseUrl + accessUrl;
            auth = oAuthRequest.GetAuthorizationHeader();
            request = (HttpWebRequest)WebRequest.Create(oAuthRequest.RequestUrl);


            request.Headers.Add("Authorization", auth);
            response = (HttpWebResponse)request.GetResponse();
            responseStream = response.GetResponseStream();
            readStream = new StreamReader(responseStream, System.Text.Encoding.UTF8);
            result = readStream.ReadToEnd();
            foreach (string token in result.Split('&'))
            {
                string[] splitToken = token.Split('=');

                switch (splitToken[0])
                {
                    case "oauth_token":
                        accesstoken = splitToken[1];
                        break;
                    case "oauth_token_secret":
                        accessTokenSecret = splitToken[1];
                        break;
                    default:
                        break;
                }
            }
            response.Close();
            #endregion

            return new OAuthCredentials(consumerKey, secret, accesstoken, accessTokenSecret);
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


        public void saveConfiguration(loginInfo l)
        {
            Login = l;
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
        private void saveConfiguration(loginInfo _login, List<GalleryEntry> _galleries)
        {
            saveGalleries();
            saveSettings();
            //save login, password, and selected galleries.
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
                Settings.speed_s = Int32.Parse(ReadRegistryValue("Speed_S", "5"));
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

        public Collection<string> gteAllCategories()
        {
            var catList = new Collection<string>();
            foreach (var a in AllAlbums)
            {
                var folderName = getFolder(a);
                if (!catList.Contains(folderName))
                {
                    catList.Add(folderName);
                }
            }
            return catList;
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


        private bool checkConnection(authEnvelope e)
        {
            Envelope = e;
            bool Success = false;
            if (Api != null)
            {
                try
                {
                    Success = true;
                }
                catch (Exception ex)
                {
                    doException(ex.Message);
                    logMsg(ex.Message);
                }
            }
            else
            {
                Success = login(e);
            }
            return Success;

        }
        private bool _loggedin = false;
        public bool checkLogin(authEnvelope e)
        {
            return Loggedin;
        }

        public void disableStuff()
        {
            Running = false;
        }

        public bool isConnected()
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

        public static DateTime TimeStarted { get => timeStarted; set => timeStarted = value; }

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
        public Thread T { get => t; set => t = value; }
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

        private string[] fetchUsersToLoad()
        {
            var usernameList = fetchKey("UserNameList");
            Debug.Assert(!string.IsNullOrEmpty(usernameList));
            if (usernameList == null)
            {
                usernameList = "MY_NAME";
            }
            var list = usernameList.Split(',');
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
            if (T == null)
            {
                Ts = new ThreadStart(runImageCollection);
                T = new Thread(Ts)
                {
                    IsBackground = true
                };
                T.Start();
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
            return DateTime.Now.Subtract(TimeStarted).TotalSeconds > warmupTime;
        }

        //how is this used vs isExpired?
        public bool screensaverExpired()
        {

#if (DEBUG)
            var minutesToRun = 15;//put back to 30
#else
            var minutesToRun = 30;
#endif
            var maxRuntimeSeconds = minutesToRun * 60;  //only run for an hour to conserve smugmugs api.

            var timeOfDay = DateTime.Now.Hour * 100 + DateTime.Now.Minute;
            var totalRuntimeSeconds = DateTime.Now.Subtract(TimeStarted).TotalSeconds;
            //logMsg("runtime is:" + totalRuntimeSeconds.ToString("0.00"));
            //we want to allow to run for a couple hours if manually woken up.

            var wakeupTime = Settings.startTime;  // 8am,  (800 = 8am
            var goToBedTime = Settings.stopTime;  //10PM,  2200=10pm)  

            Expired = (totalRuntimeSeconds > maxRuntimeSeconds) &&
                !(timeOfDay >= wakeupTime && timeOfDay < goToBedTime);  //for testing, let it run a couple hours. then see if it wakes back up at 2p.

            return Expired;
        }

        //todo: delete this
        private bool isExpired()
        { return Expired; }
        public void resetExpiredImageCollection()
        {
            if (Expired)
            {// we only want to set this if expired, to avoid always resetting the time set.
                TimeStarted = DateTime.Now;
                TimeWentBlack = null;
                Expired = false;
            }
        }
        private bool expired = false;
        private void setScreensaverToExpired()
        { //todo: delete this
            Expired = true;
        }
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
                b.B = BitmapImage2Bitmap(b.Bm);
            }
            else
            {
                b.B = getBlackImagePixel();
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
        private System.Drawing.Bitmap cropAtRect(System.Drawing.Bitmap b, System.Drawing.Rectangle r)
        {
            var nb = new System.Drawing.Bitmap(r.Width, r.Height);
            var g = System.Drawing.Graphics.FromImage(nb);
            g.DrawImage(b, -r.X, -r.Y);
            return nb;
        }

        private System.Drawing.Bitmap cropImage(BitmapImage i)
        {
            var img = BitmapImage2Bitmap(i);
            if (i != null)
            {

                var newWidth = 0;
                newWidth = img.Width;
                var oldWidth = img.Width;
                if (W != 0 && H != 0)
                {
                    ///use w and h to determine the size to crop to.
                    var aRatioScreen = H / W;
                    var aRatioImage = (double)img.Height / (double)img.Width;
                    if (aRatioImage < aRatioScreen)
                        newWidth = (int)(Convert.ToDouble(img.Height) / aRatioScreen);
                }

                var CropArea = new System.Drawing.Rectangle((img.Width / 2 - newWidth / 2), 0, (int)newWidth, (int)img.Height);

                var ABORT = true;
                if (img.Width != newWidth && !ABORT)
                {
                    img = cropAtRect(img, CropArea);
                    var targetHeight = 300;//todo; calculate this.

                    logMsg("#################### CROPPING IMAGE: w " + oldWidth + " to " + newWidth);
                    img = scaleImage(img, targetHeight);
                }

            }
            return img;
        }

        private System.Drawing.Bitmap scaleImage(System.Drawing.Bitmap img, int targetHeight)
        {
            var original = img;
            var curHeight = img.Height;
            var scale = (double)curHeight / (double)targetHeight;

            var resized = new System.Drawing.Bitmap(
                img,
                new System.Drawing.Size(
                    Convert.ToInt32((double)original.Width / scale),
                    Convert.ToInt32((double)original.Height / scale)
                    )
                );
            logMsg("Scaling by " + scale.ToString());
            return resized;
        }

        private BitmapImage showImage(ImageSizes myimage, int quality)
        {
            var image = new BitmapImage();
            try
            {
                if (myimage != null)
                {
                    if (myimage != null)
                    {
                        var URL = myimage.SmallImageUrl;
                        switch (quality)
                        {
                            case 0:
                                URL = myimage.TinyImageUrl; break;
                            case 1:
                                URL = myimage.MediumImageUrl; break;
                            case 2:
                                URL = myimage.SmallImageUrl; break;
                            case 3:
                                URL = myimage.LargeImageUrl; break;
                            case 4:
                                URL = myimage.X3LargeImageUrl; break;
                            case 5:
                                URL = myimage.OriginalImageUrl; break;
                            default: break;
                        }
                        return showImage(URL);
                    }
                }
            }
            catch (System.Net.WebException ex)
            {//no connection, wait longer.
                doException(ex.Message);
                logMsg(ex.Message);
            }
            catch (Exception ex)
            {
                doException(ex.Message);
                logMsg(ex.Message);
                logMsg(ex.StackTrace);
            }
            return image;

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
                    {
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




        public async Task<ICollection<AlbumImage>> GetImageList(string cat, string albumName)
        {
            var a = AllAlbums.Find(x => x.Name == albumName);
            Debug.Assert(!string.IsNullOrEmpty(a.Name));
            var _imageList = await Api.GetAlbumImages(a);
            return _imageList;
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

        public static void FisherYatesShuffle<T>(ref T[] array)
        {//using this to try to randomize a list.
            var r = new Random();
            for (var i = array.Length - 1; i > 0; i--)
            {
                var j = r.Next(0, i + 1);
                var temp = array[j];
                array[j] = array[i];
                array[i] = temp;
            }
        }

        private bool isLoadingAlbums = true;
        public bool IsLoadingAlbums()
        {
            return IsLoadingAlbums1;
        }
        bool isConfigurationMode = false;

        public void setIsConfigurationMode()
        {
            isConfigurationMode = true;
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
                        if (username == "MY_NAME")
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
                                  if (!screensaverExpired())
                                  {
                                      try
                                      {
                                          if (!isConfigurationMode)
                                          {
                                              loadImages(a, false);
                                          }
                                      }
                                      catch (Exception ex)
                                      {
                                          doException(ex.Message);
                                      }
                                  }
                                  else
                                  {
                                      logMsg("Skipping loadImages - expired");
                                  }
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

        Dictionary<string, ImageSet> playedImages = new();// Dictionary<string, ImageSet>();

        private ImageSet getRandomImage()
        {
            checkLogin(Envelope);
            {
                var imageSet = new ImageSet();
                imageSet.Bm = null;

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
                            imageSet.Bm = image;
                            imageSet.Name = element.Name;
                            imageSet.AlbumTitle = element.AlbumTitle;
                            imageSet.ImageURL = element.ImageURL;
                            imageSet.CAtegory = element.CAtegory;
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
                fireException(msg);
        }
    }

}
