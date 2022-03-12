using Microsoft.Win32;  //registry.
using SmugMug.NET;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;


namespace SMEngine
{

    public class CSMEngine
    {
        private static DateTime timeStarted = DateTime.Now;
        private static DateTime timeBooted = DateTime.Now;

        private Dictionary<String, ImageSet> _imageDictionary;
        private static Int64 imageCounter = 0;

        private SmugMugAPI api = null;
        private User _user = null;
        public System.Data.DataTable _galleryTable;
        private static List<Album> _allAlbums;
        private Queue<ImageSet> _imageQueue;

        private const int maximumQ = 10;   //window, only download if q is less than max and greater than min.
        private const int minQ = 2;
        private volatile bool running = false;
       

#if (DEBUG)
        private int debug_limit = 100;
#else
        private int debug_limit = 5000;
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
            restartCounter++;
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

            TaskScheduler.Instance.ScheduleTask(startHour, startMinute, frequencyHours,  //run at 11:15a daily
               () =>
               {
                   logMsg("reloading library!!!");
                   rePullAlbums();
                   //do the thing!
               });
            
        }

        public string getTimeSinceLast()
        {
            var timeSince = DateTime.Now.Subtract(lastImageRequested);
            return timeSince.TotalSeconds.ToString("0.00");
        }

        public string getRuntimeStatsInfo()
        {
            var msg = "running since " + timeBooted.ToShortDateString()
                + " : "
                + timeBooted.ToShortTimeString();
            msg += "\n Uptime: " + DateTime.Now.Subtract(timeBooted).ToString();
            lock (_imageDictionary)
            {
                msg += "\n images: " + _imageDictionary.Count();
            }
            lock (_allAlbums)
            {
                msg += "\n albums: " + _allAlbums.Count();
            }
            msg += "\n images shown: " + imageCounter;
            msg += "\n queue depth: " + _imageQueue.Count();
            msg += "\n image size: " + _settings.quality + " / " + fetchImageUrlSize();
            msg += "\n time between images: " + getTimeSinceLast();
            msg += "\n exceptions raised: " + exceptionsRaised;
            msg += "\n reloaded albums: " + restartCounter + " times.";
            msg += "\n memory: " + Process.GetCurrentProcess().WorkingSet64 / (1024*1024);
            msg += "\n Peak memory: " + Process.GetCurrentProcess().PeakPagedMemorySize64 / (1024 * 1024);
            msg += "\n Peak virtual memory: " + Process.GetCurrentProcess().PeakVirtualMemorySize64 / (1024 * 1024);

            msg += "\n Menu:";
            msg += "\n\t ESC or Q: exit program";
            msg += "\n\t s: show or hide stats";
            msg += "\n\t w: show window controls";
            msg += "\n\t b: go back to borderless mode";
            msg += "\n\t r: reload library";
            msg += "\n\t <- or ->: show next photo";
            lastImageRequested = DateTime.Now;
            return msg;
        }

        public authEnvelope getCode()
        {

            var envelope = new authEnvelope();

            //todo: can I get build time variables from environment?
            // todo: look into https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-6.0&tabs=windows

            //salty tokens
            var CONSUMERSECRET_SALTED_KEY = "SmugMugOAuthConsumerSecretSalted";
            var consumerTokenKey = "SmugMugOAuthConsumerTokenSalted";
            envelope.consumerToken = Authenticator.Decrypt(fetchKey(CONSUMERSECRET_SALTED_KEY), salt);
            envelope.consumerSecret = Authenticator.Decrypt(fetchKey(consumerTokenKey), salt);

            try
            {

                envelope.token = 
                    Authenticator.Decrypt(
                    ReadRegistryValue(ACCESSTOKEN, ""), salt
                    );
                envelope.tokenSecret =
                    Authenticator.Decrypt(
                    ReadRegistryValue(ACCESSTOKENSECRET, ""), salt
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
            var keySetting = config.AppSettings.Settings[CONSUMERTOKEN];
            if (keySetting != null)
            {
                consumerKey = keySetting.Value;
            }

            if (String.IsNullOrEmpty(consumerKey))
            {
                throw new ConfigurationErrorsException("The OAuth consumer token must be specified in App.config");
            }

            //Connect to SmugMug using Anonymous access
            SmugMugAPI apiAnonymous = new SmugMugAPI(LoginType.Anonymous, new OAuthCredentials(consumerKey));
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
           WriteRegistryValue(ACCESSTOKEN, Authenticator.Encrypt(envelope.token, salt));
           WriteRegistryValue(ACCESSTOKENSECRET, Authenticator.Encrypt(envelope.tokenSecret, salt));
        }
        public static SmugMugAPI AuthenticateUsingOAuth(authEnvelope envelope)
        {
           

            OAuthCredentials oAuthCredentials = null;
            if (envelope.token!= null && envelope.token != "")
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
            api = apiOAuth;
            _loggedin = true;
            return true;
        }

        public void  logout()
        {
            var a = new authEnvelope("","","","");
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
            System.Diagnostics.Process.Start(ps);
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
            _settings = new CSettings();
            _imageQueue = new Queue<ImageSet>();
            _galleryTable = new System.Data.DataTable();
            _imageDictionary = new Dictionary<string, ImageSet>();  //image id, and url
            _galleryTable.Columns.Add(new System.Data.DataColumn("Category", typeof(String)));
            _galleryTable.Columns.Add(new System.Data.DataColumn("Album", typeof(String)));
            _allAlbums = new List<Album>();
            _login = new loginInfo();
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
            _login = l;
        }
        CSettings _settings;
        public void setSettings(CSettings set)
        {
            _settings = set;
        }

         public bool checkCategoryForAlbums(String category)
        {
            //we are checking to see if there are any albums in this category
            var albums = _allAlbums.FirstOrDefault(x => getFolder(x).Equals(category));
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
                String subKey = "SOFTWARE\\andysScreensaver\\login";
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
                        // sk1.Close();
                        //rk.Close();
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
                            //doException(e.Message);
                            logMsg(e.Message);
                            // AAAAAAAAAAARGH, an error!
                            //ShowErrorMessage(e, "Reading registry " + KeyName.ToUpper());
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
            WriteRegistryValue("quality", _settings.quality.ToString());
            WriteRegistryValue("Speed_S", _settings.speed_s.ToString());
            WriteRegistryValue("LoadAll", _settings.load_all ? 1.ToString() : 0.ToString());
            WriteRegistryValue("ShowInfo", _settings.showInfo ? 1.ToString() : 0.ToString());

            WriteRegistryValue("gridH", _settings.gridHeight.ToString());
            WriteRegistryValue("gridW", _settings.gridWidth.ToString());
            WriteRegistryValue("borderT", _settings.borderThickness.ToString());

        }
        public void loadSettings()
        {
            try
            {
                _settings.quality = Int32.Parse(ReadRegistryValue("quality","2"));
                _settings.speed_s = Int32.Parse(ReadRegistryValue("Speed_S","5"));
                var loadAll = Int32.Parse(ReadRegistryValue("LoadAll","1"));
                var showInfo = Int32.Parse(ReadRegistryValue("ShowInfo","1" ));
                _settings.load_all = loadAll == 1 ? true : false;
                _settings.showInfo = showInfo == 1 ? true : false;

                _settings.gridHeight = Int32.Parse(ReadRegistryValue("gridH","3"));
                _settings.gridWidth = Int32.Parse(ReadRegistryValue("gridW","4"));
                _settings.borderThickness = Int32.Parse(ReadRegistryValue("borderT","1"));
            }
            catch (Exception ex)
            {
                doException(ex.Message);
                logMsg(ex.Message);
                _settings = new CSettings();
            }
        }
        public CSettings settings
        {
            get
            {
                return _settings;
            }
        }
        public loginInfo getLogin()
        {
            return _login;
        }
        private void loadGalleries()
        {
            int galleries_present = 0;
            try
            {
                galleries_present = Int32.Parse(ReadRegistryValue("GalleryCount","12"));
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
            int galleries_present = _galleryTable.Rows.Count;
            WriteRegistryValue("GalleryCount", galleries_present.ToString());
            for (int i = 0; i < galleries_present; i++)
            {
                var g = new GalleryEntry();
                g.category = _galleryTable.Rows[i].ItemArray[0].ToString();
                g.gallery = _galleryTable.Rows[i].ItemArray[1].ToString();
                WriteRegistryValue("Cat_" + i.ToString(), g.category);
                WriteRegistryValue("Gal_" + i.ToString(), g.gallery);

            }
        }

        private void loadConfiguration()
        {
            loadGalleries();
            loadSettings();
        }
        public void addGallery(String cat, String gal)
        {
            _galleryTable.Rows.Add(cat, gal);
        }

        public Collection<String> gteAllCategories()
        {
            var catList = new Collection<String>();
            foreach(var a in _allAlbums)
            {
                var folderName = getFolder(a);
                if (!catList.Contains(folderName))
                {
                    catList.Add(folderName);
                }
            }
            return catList;
        }

        public String[] getCategoriesAsync()
        {
            var categories = new List<String>();

            gettingCategories = true;
            foreach (var album in _allAlbums)
            {
                if (!categories.Contains(getFolder(album)))
                {
                    categories.Add(getFolder(album)); 
                }
            }
            gettingCategories = false;
            return categories.ToArray();
            if (_user != null)
            {
                var myCats = getCategories(); 
                foreach (var c in myCats)
                {
                    categories.Add(c);
                }
            }
            gettingCategories = false;
            return categories.ToArray();
        }
        
        bool gettingCategories = false;

        public String[] getCategories()
        {
            List<String> categories = new List<String>();
            lock (this)
            {
                gettingCategories = true;
                throw new NotImplementedException();
            }
                /*
                if (_user != null)
                {
                    var myCats = _user.GetCategories();  //how can I make this async?
                    myCats.Sort((c1, c2) => c1.Name.CompareTo(c2.Name));
                    foreach (Category c in myCats)
                    {
                        categories.Add(c.Name);
                    }
                }
                gettingCategories = false;
            }
            return categories.ToArray();
                */
        }
        

        public string getFolder(Album album) { 
        
            if (album.Uris == null || album.Uris.Folder == null){
                logMsg("album is null!");
                return "";
            }
            var fullPath= album.Uris.Folder.Uri.ToString();
            var category = fullPath.Substring(fullPath.LastIndexOf('/') + 1);
            return category;
        }

        public String[] getAlbums(String category)
        {
            var catAlbums = new List<String>();
            foreach (Album album in _allAlbums)
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
            _galleryTable.Clear();
            foreach (Album a in _allAlbums)
            {
                addGallery(getFolder(a), a.Name);
            }
        }

        public void addAllAlbums(string byCategoryName)
        {
            var albums = _allAlbums.Where(x => getFolder(x) == byCategoryName);
            foreach (var a in albums)
            {
                    addGallery(getFolder(a), a.Name);
            }
        }


        private bool checkConnection(authEnvelope e)
        {
            _envelope = e;
            bool Success = false;
            if (api != null)
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
            //if (!_loggedin)
                //login(e);

            return _loggedin;
        }

        public Album checkCreateAlbum(String cat, String album)
        {
            throw new NotImplementedException();
            /* I don't think this function is needed)
            var v = _allAlbums.Find(x => x.Name == album && getFolder(x) == cat);
            try
            {
                if (v == null)
                {//create album
                    var myCategory = api.GetCategories();
                    var c = myCategory.Find(x => x.Name == cat);

                    if (c == null)
                    {
                        c = _user.CreateCategory(cat);
                    }
                    if (c == null)
                        throw new Exception("AHH");

                    v = c.CreateAlbum(album, false);

                    if (v == null)
                        throw new Exception("AHHHH");

                    //let's default to private.
                    v.Protected = true;
                    v.WorldSearchable = false;
                    v.SmugSearchable = false;
                    v.Public = false;
                    v.ChangeSettings();
                    lock (_allAlbums)
                    {
                        _allAlbums.Add(v);
                    }

                }
                //  return v

            }
            catch (Exception ex)
            {
                logMsg(ex.Message);
            }
            return v;
            */
        }

        public void disableStuff()
        {
            running = false;
        }
        
        public bool isConnected()
        {
            return _loggedin;
        }
        public bool login(authEnvelope envelope)
        {
            _envelope = envelope;
            
            try
            {
                _loggedin = false;
                if (api != null) return true;
                api = AuthenticateUsingOAuth(envelope);
                if (api == null)
                    return false;
               // loadAlbums();
                _loggedin = true; // used for thread control.
                //note: the old login would also load albums and load the user, it's not clear we want to do this yet.
                return true;
            }
            catch (Exception ex)
            {
                //todo: log error.
                doException("login: " + ex.Message);
                return false;
            }
        }

        private async void loadAlbums(string userNickName = null) //do we want to take in a list of accounts, or just the primary?  we can do both.
        {
            try
            {
                if (userNickName != null)
                {
                    _user = await api.GetUser(userNickName);
                }
                else
                {
                    _user = await api.GetAuthenticatedUser();
                }

                
                var albums = await api.GetAlbums(_user, debug_limit); // todo: do we care about the limit?
                logMsg("returned albums: " + albums.Count());
                lock (_allAlbums)
                {
                    _allAlbums.AddRange(albums);
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
                return _imageQueue.Count;
            }
        }

        private static void logMsg(string msg)
        {
            Debug.WriteLine(DateTime.Now.ToLongTimeString() + ": " + msg);
        }

        private void runImageCollection()
        {
           // System.Threading.Thread.Sleep(50);//experimental.
            if (running == false )
            {
             //   while (gettingCategories)
             //       System.Threading.Thread.Sleep(50);//experimental.
                running = true;
                bool startIt = true;
                while (running)
                {
                    if (_imageQueue.Count < minQ)
                        startIt = true;
                    if (_imageQueue.Count < maximumQ && startIt)
                    {
                        try
                        {
                            if (!screensaverExpired())  //new test, ensuring not pulling image while asleep
                            {
                                var imageSet = getRandomImage();
                                if (imageSet != null)
                                {
                                    lock (_imageQueue)
                                    {
                                        _imageQueue.Enqueue(imageSet);
                                        logMsg($"Image queue depth: {qSize}");
                                    }
                                }
                                else
                                {
                                    //logMsg("waiting - used to sleep.");
                                    //   System.Threading.Thread.Sleep(5);
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
                            running = false;
                            logMsg("Invalid login");
                        }
                    }
                    else
                    {
                        startIt = false;
                    }

                    //logMsg("Sleeping longer");
                    System.Threading.Thread.Sleep(10);//don't overrun processor.
                }
                running = false;//reset if stopped for any reason.
            }
        }
        ThreadStart ts = null;
        Thread t = null;

        ThreadStart tsAlbumLoad = null;
        Thread tAlbumLoad = null;
       // String[] userNameList = { "MY_NAME", "user2" };

        private String[] fetchUsersToLoad()
        {
            var usernameList = fetchKey("UserNameList");
            if (usernameList == null)
            {
                usernameList = "MY_NAME";
            }
            var list= usernameList.Split(',');
            return list; 
            //return userNameList;
        }
        private void start()
        {
            _envelope = getCode();
            if (tAlbumLoad == null)
            {
                tsAlbumLoad = new ThreadStart(loadAllImages);
                tAlbumLoad = new Thread(tsAlbumLoad);
                tAlbumLoad.IsBackground = true;
                tAlbumLoad.Start();
            }
            //System.Threading.Thread.Sleep(50);//experimental.
            if (t == null)
            {
                ts = new ThreadStart(runImageCollection);
                t = new Thread(ts);
                t.IsBackground = true;
                t.Start();
            }

        }

        private System.Drawing.Bitmap BitmapImage2Bitmap(BitmapImage bitmapImage)
        {
            System.Drawing.Bitmap bitmap = null;
            if (bitmapImage != null)
            {
                try
                {
                    using (var outStream = new MemoryStream())
                    {
                        var enc = new BmpBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                        enc.Save(outStream);
                        bitmap = new System.Drawing.Bitmap(outStream);
                        // return bitmap; <-- leads to problems, stream is closed/closing ...
                        outStream.Close();
                    }
                }
                catch (Exception ex)
                {
                    doException("bm2m: " + ex.Message);
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

            var hoursToRun = 1;
#if (DEBUG)
            var minutesToRun = 15;//put back to 30
#else
            var minutesToRun = 30;
#endif
            var maxRuntimeSeconds = minutesToRun*60;  //only run for an hour to conserve smugmugs api.
            var hourOfDay = DateTime.Now.Hour;
            var totalRuntimeSeconds = DateTime.Now.Subtract(timeStarted).TotalSeconds;
            logMsg("runtime is:" + totalRuntimeSeconds.ToString("0.00"));
            //we want to allow to run for a couple hours if manually woken up.
            var wakeupTime = 8;
            var goToBedTime = 12 + 10;  //9PM  
            expired = (totalRuntimeSeconds > maxRuntimeSeconds) &&
                !(hourOfDay >= wakeupTime && hourOfDay < goToBedTime);  //for testing, let it run a couple hours. then see if it wakes back up at 2p.

            return expired;
        }

        //todo: delete this
        private bool isExpired()
        { return expired; }
        public void resetExpiredImageCollection()
        {
            if (expired)
            {// we only want to set this if expired, to avoid always resetting the time set.
                timeStarted = DateTime.Now;
                timeWentBlack = null;
                expired = false;
            }
            //start();
        }
        private bool expired = false;
        private void setScreensaverToExpired()
        { //todo: delete this
            expired = true;
        }
        public ImageSet getImage()
        {
          //  lastImageRequested = DateTime.Now;
            ImageSet b = new ImageSet();
            logMsg("fetching image...");
            lock (_imageQueue)
            {
                if (qSize > 0)
                {
                    b = _imageQueue.Dequeue();
                    logMsg($"Dequeue, Image queue depth: { qSize}");
                    imageCounter++;
                }
                else
                {
                  //  return null;
                }
            }
            if (!screensaverExpired())
            {
                b.b = BitmapImage2Bitmap(b.bm); //cropImage(b.bm);//used to be simple bitmap2BitmapImage
            }
            else
            {
                b.b = getBlackImagePixel();
               // setScreensaverToExpired();
            }
            return b;
        }

        public System.Drawing.Bitmap getBlackImagePixel()
        {
            //just generate a black image

            if (timeWentBlack == null)
            {
                timeWentBlack = DateTime.Now;
            }
            var bb = new System.Drawing.Bitmap(1, 1);
            bb.SetPixel(0, 0, System.Drawing.Color.Black);
            logMsg("Setting a black pixel!");

            return bb;
        }


        private double w = 0, h = 0;
        public void setScreenDimensions(double _w, double _h)
        {
            w = _w;
            h = _h;
        }
        private System.Drawing.Bitmap cropAtRect(System.Drawing.Bitmap b, System.Drawing.Rectangle r)
        {
            System.Drawing.Bitmap nb = new System.Drawing.Bitmap(r.Width, r.Height);
            System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(nb);
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
                if (w != 0 && h != 0)
                {
                    ///use w and h to determine the size to crop to.
                    var aRatioScreen = h / w;
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

            var resized = new System.Drawing.Bitmap(img, new System.Drawing.Size(Convert.ToInt32((double)original.Width / scale), Convert.ToInt32((double)original.Height / scale)));
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
                    //load image;

                    int BytesToRead = 2500;
                    if (myimage != null)
                    {
                        String URL = myimage.SmallImageUrl;
                        switch (quality)
                        {
                            case 0:
                                URL = myimage.TinyImageUrl;
                                break;
                            case 1:
                                URL = myimage.MediumImageUrl;
                                break;
                            case 2:
                                URL = myimage.SmallImageUrl;
                                break;

                            case 3:
                                URL = myimage.LargeImageUrl; break;
                            case 4:
                                URL = myimage.X3LargeImageUrl; break;
                            case 5:
                                URL = myimage.OriginalImageUrl;
                                break;
                            default:
                                break;


                        }
                        return showImage(URL);
                    }
                }
            }
            catch (System.Net.WebException ex)
            {//no connection, wait longer.
                doException(ex.Message);
                logMsg(ex.Message);
               // System.Threading.Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                doException(ex.Message);
                //doException("showImage: " + ex.Message);
                logMsg(ex.Message);
                logMsg(ex.StackTrace);
               // System.Threading.Thread.Sleep(50);
            }
            return image;

        }

        private BitmapImage showImage(string URL)
        {
            int BytesToRead = 2500;
            var image = new BitmapImage();

            try {
                if (URL != null)
                {
                    WebRequest request = WebRequest.Create(new Uri(URL, UriKind.Absolute));

                    request.Timeout = -1;
                    try
                    {
                        HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                    
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception("image not returned: " + URL);
                    }
                    var responseStream = response.GetResponseStream();
                    var reader = new BinaryReader(responseStream);
                    var memoryStream = new MemoryStream();

                    var bytebuffer = new byte[BytesToRead];
                    int bytesRead = reader.Read(bytebuffer, 0, BytesToRead);
                    var sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    while (bytesRead > 0)
                    {
                        memoryStream.Write(bytebuffer, 0, bytesRead);
                        bytesRead = reader.Read(bytebuffer, 0, BytesToRead);
                    }

                    sw.Stop();
                    logMsg($"Get Image { URL} took: {sw.ElapsedMilliseconds}ms.");
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
           //     System.Threading.Thread.Sleep(1000);
                return null;
            }
            catch (Exception ex)
            {
                doException(ex.Message);
                //doException("showImage: " + ex.Message);
                logMsg(ex.Message);
                logMsg(ex.StackTrace);
               // System.Threading.Thread.Sleep(50);
                return null;
            }
            return image;

        }
                

             

        public async Task<ICollection<AlbumImage>> GetImageList(string cat, String albumName)
        {
            var a = _allAlbums.Find(x => x.Name == albumName);
            //System.Diagnostics.Debug.Assert(a.Name == cat);
            var _imageList = await api.GetAlbumImages(a);
            return _imageList;
        }

        private string fetchImageUrl(ImageSizes imageSize)
        {
            switch (_settings.quality)
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
            switch (_settings.quality)
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
            if (a == null || a.Uris.AlbumImages == null) { 
                return; 
            }
            
                try
                {
                logMsg("loading album:" + a.Name);
                    if (singleAlbumMode)
                    {
                        lock (_imageDictionary)
                        {
                            _imageDictionary.Clear();
                        }
                    }
                
                   var images = await api.GetAlbumImagesWithSizes(a, debug_limit); //todo: do we care about sizes?
                    if (images == null)
                    {
                        doException("images is null!");
                    }
                    logMsg("loaded "+ images.AlbumImages.Count() + " images from album " +  a.Name);
                   // lock (_imageDictionary)
                    {
                    Parallel.ForEach(images.AlbumImages,
                           new ParallelOptions { MaxDegreeOfParallelism = 4 },
                           i =>
                           //(var i in images.AlbumImages)
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
                            lock (_imageDictionary)
                            {
                                       try
                                       {
                                           _imageDictionary.Add(
                                                   i.ImageKey,
                                                   new ImageSet(
                                                       imageUrl,
                                                       i.Caption == null ? "" : i.Caption,
                                                       i.FileName == null ? "" : i.FileName,
                                                       i.Date == null ? DateTime.Now : i.Date,
                                                       getFolder(a) == null ? "" : getFolder(a),
                                                       a.Name == null ? "" : a.Name
                                                       )
                                                   );
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
                    doException("loadImages: " + ex.Message);
                    logMsg(ex.Message);
                }
        }

        

        public delegate void fireExceptionDel(String msg);
        public event fireExceptionDel fireException;
        public class ImageSet
        {
            public System.Drawing.Bitmap b;
            public BitmapImage bm;
            public String caption;
            public String albumTitle;
            public String CAtegory;
            public String exif;
            //public String date;
            public DateTime MyDate;
            public String Name;
            public String ImageURL;
            public ImageSet(string mediumUrl, string Caption, string name, DateTime mydate, string folder, string albumname)
            {
                ImageURL = mediumUrl;
                caption = Caption;
                Name = name;
                MyDate = mydate;
                CAtegory = folder;
                albumTitle = albumname;

            }
            public ImageSet()
            {
                caption = "";
                exif = "";
               // date = "";
            }
        }

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
            return isLoadingAlbums;
        }
        private void loadAllImages()
        {
            _allAlbums = new List<Album>();
            try
            {
                while (_loggedin == false)
                {// I don't think I really want to do this. what if running app, we would probably want to start up config.
                    Thread.Sleep(100); //waiting for login to complete.
                }
                if (checkLogin(_envelope))
                {
                    isLoadingAlbums = true;
                    foreach (var username in fetchUsersToLoad())
                    {
                        if (username == "MY_NAME")
                        {
                            loadAlbums(); //todo: pass in the username/s to load
                        }
                        else
                        {
                            loadAlbums(username); //todo: pass in the username/s to load
                        }
                    }
                    isLoadingAlbums = false;
                    
                    if (_settings.load_all)
                    {
                        var ary = getCategoriesAsync();
                        var rand = new Random();
                        var rnd = new Random();
                        var shuffledCats = ary.OrderBy(x => rnd.Next()).ToList();

                        var shuffledAlbums = _allAlbums.OrderBy(x => rnd.Next()).ToList();

                        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        Parallel.ForEach(
                            shuffledAlbums,
                               new ParallelOptions { MaxDegreeOfParallelism = 4 },
                            a =>
                      {
                          if (a == null)
                          {
                              //do nothing.continue;
                          }
                          //while (gettingCategories)
                          //   System.Threading.Thread.Sleep(50);
                          if (getFolder(a).Contains("Surveilance"))
                          {
                              logMsg("Throwing out surveilance");
                          }
                          else
                          {
                              if (!screensaverExpired())
                              {
                                  //   logMsg($"Loading {getFolder(a)}:{a.Name}");
                                  try
                                  {
                                      loadImages(a, false);
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
                          //  System.Threading.Thread.Sleep(1);
                      });

                        logMsg($"Time to load all albums: {sw.ElapsedMilliseconds} milliseconds");
                    }
                    else
                    {
                        //we are loading only selected albums, based on configuration!
                        Album a = null;
                        if (_galleryTable.Rows.Count > 0)
                        {
                            for (int i = 0; i < _galleryTable.Rows.Count; i++)
                            {
                                var cat = _galleryTable.Rows[i].ItemArray[0].ToString();
                                var gal = _galleryTable.Rows[i].ItemArray[1].ToString();
                                a = _allAlbums.FirstOrDefault(x => getFolder(x) == cat && x.Name == gal);
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

        }



        private ImageSet getRandomImage()
        {

       
            checkLogin(_envelope);
            {
                //assume configuration is loaded.
                //load an image from smugmug, and return it.
              //  var myimage = new SmugMugModel.Image();//todo..  select a random image;



//                Album a = null;
                var imageSet = new ImageSet();
                imageSet.bm = null;

                lock (_imageDictionary)
                {
                    if (_imageDictionary.Count > 0)
                    {
                        var imageIndex = r.Next(_imageDictionary.Count);
                        try
                        {
                            var myQuality = _imageQueue.Count > 0 ? settings.quality : 1;  //allow low res for first pics.
                            var element = _imageDictionary.ElementAt(imageIndex).Value;  //optimizing to avoid multiple lookups.
                            var image = showImage(element.ImageURL); 
                            if (image == null){
                                throw new Exception("image returned is null: " + element.ImageURL);
                            }
                            imageSet.bm = image;
                            imageSet.Name = element.Name;
                            imageSet.albumTitle = element.albumTitle;
                            imageSet.ImageURL = element.ImageURL;
                            imageSet.CAtegory = element.CAtegory;
                            imageSet.MyDate = element.MyDate;
                            imageSet.albumTitle = element.albumTitle; //element.Album.Title;
                            imageSet.caption = element.caption;
                            imageSet.exif = element.exif;

                            
                            //imageSet.exif = element.GetEXIF().ToString();
                            // imageSet = element;
                        }
                        catch (Exception ex)
                        {
                            doException("random: " + ex.Message);
                            logMsg(ex.Message + "\r\n" + ex.StackTrace);
                        }

                    }
                    else { return null; }
                }
 




                return imageSet;
            }
        }
        public void doException(String msg)
        {
            exceptionsRaised++;
            if (fireException != null)
                fireException(msg);
        }
    }

}
