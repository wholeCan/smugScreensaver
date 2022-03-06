using Microsoft.Win32;  //registry.
using SmugMug.NET;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;


namespace SMEngine
{

    public class authEnvelope
    {
        public string consumerToken;
        public string consumerSecret;
        public string token;
        public string tokenSecret;
    }
    /// <summary>
    /// Originally written -- 4/2014
    /// 
    /// revision 5/8/2018:  Switched to Smugmug api 1.3 by upgrading to nuget version of SmugMugModel
    /// SmugMug API is now available from nuget
    /// 
    /// 2/26/2022: major refactor to upgrade to smugmug 2.0 api

    //    Following along source from:
    //https://github.com/AlexGhiondea/SmugMug.NET/blob/master/nuGet/SmugMugModel.v2.nuspec

    //Need to better understand the api
    //https://api.smugmug.com/api/v2/doc/pages/concepts.html

    ///
    /// 2018 feature enhancements:
    /// put a timeout period, stop pulling images after a couple hours.  restart after 24 hours.
    /// </summary>

    //is this class needeD? maybe not
    public class ImageInfo
    {
        string key;
        string caption;
        string name;
    }

    
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
        private int debug_limit = 50;
#else
        private int debug_limit = 1000;
#endif



        private authEnvelope _envelope;
        private int exceptionsRaised = 0;

        #region NEW AUTH STUFF 2022

        const string CONSUMERTOKEN = "SmugMugOAuthConsumerToken";
        const string CONSUMERSECRET = "SmugMugOAuthConsumerSecret";
        const string ACCESSTOKEN = "SmugmugOauthAccessToken";
        const string ACCESSTOKENSECRET = "SmugmugOauthAccessTokenSecret";

        private DateTime lastImageRequested = DateTime.Now;

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
            msg += "\n time between images: " + getTimeSinceLast();
            msg += "\n exceptions raised: " + exceptionsRaised;
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
            var e = new authEnvelope();
                e.consumerToken = "a";
                e.consumerSecret = "b";
                e.token = "c";
                e.tokenSecret = "d";
            return null;  //return null to fetch from config
            //return e; // return e if we're testing just storing internally.
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
        public static SmugMugAPI AuthenticateUsingOAuth(authEnvelope? envelope)
        {
            if (envelope == null)
            {
                envelope = new authEnvelope();
                envelope.consumerToken = fetchKey(CONSUMERTOKEN);
                envelope.consumerSecret = fetchKey(CONSUMERSECRET);
                envelope.token = fetchKey(ACCESSTOKEN);
                envelope.tokenSecret = fetchKey(ACCESSTOKENSECRET);

            }

            OAuthCredentials oAuthCredentials = null;
            if (envelope.token!= null)
            {//todo: right now this is obviously hardcoded
                oAuthCredentials = new OAuthCredentials(envelope.consumerToken, envelope.consumerSecret, 
                    envelope.token, envelope.tokenSecret);
            }
            else
            {
                oAuthCredentials = GenerateOAuthAccessToken(envelope.consumerToken, envelope.consumerSecret);
            }

            //Connect to SmugMug using oAuth
            SmugMugAPI apiOAuth = new SmugMugAPI(LoginType.OAuth, oAuthCredentials);
            return apiOAuth;
        }

        private static OAuthCredentials GenerateOAuthAccessToken(string consumerKey, string secret)
        {
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

        #endregion

        //The following information was gathered by pulling info from the test app that came with the SmugMug API.
        // static Token accessTok = new Token() { id = "8NH2CxKTCFjcPLGddGNVp4rqDz5ffzgV", Secret = "6ChgJLLcqzz8FgMMPZWZCN7THGmR3hGLFD2Z5jFmxp2vxtTzBJWHtZH7CQb7ZZJG" };
      //  static Token accessTok = new Token() { id = null, Secret = null };


        private loginInfo _login;
        static Random r = new Random();
        public CSMEngine(bool doStart)
        {
            salt = 0xbad7eed;
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

        /**categories don't exist
         * public bool checkCategoryForAlbums(String category)
        {
            //todo
            bool retVal = false;
            foreach (Album a in _allAlbums)
            {
                if (a.Category.Name == category)
                {
                    retVal = true;
                    break;
                }
            }
            return retVal;
        }
        */
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


        public bool Write(string KeyName, object Value)
        {
            try
            {
                // Setting
                RegistryKey rk = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default); ;
                // I have to use CreateSubKey 
                String subKey = "SOFTWARE\\andysScreensaver\\login";
                // (create or open it if already exits), 
                // 'cause OpenSubKey open a subKey as read-only
                var sk1 = rk.CreateSubKey(subKey);
                // Save the value
                sk1.SetValue(KeyName.ToUpper(), Value);

                sk1.Close();
                rk.Close();
                return true;
            }
            catch (Exception e)
            {
                logMsg(e.Message);
                doException(e.Message);
                return false;
            }
        }
        private string Read(string KeyName)
        {
            // Opening the registry key
            var rk = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            // Open a subKey as read-only
            var subKey = "SOFTWARE\\andysScreensaver\\login";
            var sk1 = rk.OpenSubKey(subKey);
            // If the RegistrySubKey doesn't exist -> (null)
            if (sk1 == null)
            {
                sk1.Close();
                rk.Close();
                return null;
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
                    doException(e.Message);
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

        private int salt;


        private void loadPassword()
        {//load from registry.
            String tmp = Read("Password");
            if (tmp != null)
                _login.password = Authenticator.Decrypt(tmp, salt.ToString());
            _login.login = Read("Login");

        }

        public void saveSettings()
        {
            Write("quality", _settings.quality.ToString());
            Write("Speed_S", _settings.speed_s.ToString());
            Write("LoadAll", _settings.load_all ? 1.ToString() : 0.ToString());
            Write("ShowInfo", _settings.showInfo ? 1.ToString() : 0.ToString());

            Write("gridH", _settings.gridHeight.ToString());
            Write("gridW", _settings.gridWidth.ToString());
            Write("borderT", _settings.borderThickness.ToString());

        }
        public void loadSettings()
        {
            try
            {
                _settings.quality = Int32.Parse(Read("quality"));
                _settings.speed_s = Int32.Parse(Read("Speed_S"));
                var loadAll = Int32.Parse(Read("LoadAll"));
                var showInfo = Int32.Parse(Read("ShowInfo"));
                _settings.load_all = loadAll == 1 ? true : false;
                _settings.showInfo = showInfo == 1 ? true : false;

                _settings.gridHeight = Int32.Parse(Read("gridH"));
                _settings.gridWidth = Int32.Parse(Read("gridW"));
                _settings.borderThickness = Int32.Parse(Read("borderT"));
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
                galleries_present = Int32.Parse(Read("GalleryCount"));
            }
            catch (Exception ex)
            {
                logMsg(ex.Message);
                doException(ex.Message);
                galleries_present = 0;

            }
            for (int i = 0; i < galleries_present; i++)
            {
                GalleryEntry g = new GalleryEntry();
                g.category = Read("Cat_" + i.ToString());
                g.gallery = Read("Gal_" + i.ToString());

                addGallery(g.category, g.gallery);
            }


        }
        private void saveGalleries()
        {
            int galleries_present = _galleryTable.Rows.Count;
            Write("GalleryCount", galleries_present.ToString());
            for (int i = 0; i < galleries_present; i++)
            {
                var g = new GalleryEntry();
                g.category = _galleryTable.Rows[i].ItemArray[0].ToString();
                g.gallery = _galleryTable.Rows[i].ItemArray[1].ToString();
                Write("Cat_" + i.ToString(), g.category);
                Write("Gal_" + i.ToString(), g.gallery);

            }
        }

        private void loadConfiguration()
        {
            loadPassword();
            loadGalleries();
            loadSettings();
        }
        public void addGallery(String cat, String gal)
        {
            _galleryTable.Rows.Add(cat, gal);
        }

        public String[] getCategoriesAsync()
        {
            List<String> categories = new List<String>();

            gettingCategories = true;
            foreach (var album in _allAlbums)
            {
                if (!categories.Contains(getFolder(album)))
                { categories.Add(getFolder(album)); }
            }
            gettingCategories = false;
            return categories.ToArray();
            
            /*
            //todo
            if (_user != null)
            {
                var myCats = await _user.GetCategoriesAsync();  //how can I make this async?
                                                                //    myCats.Sort();
                                                                //     myCats.Reverse();

                foreach (Category c in myCats)
                {
                    categories.Add(c.Name);
                }
            }
            gettingCategories = false;

            return categories.ToArray();
            */
            //throw new NotImplementedException();
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
            //  _galleryTable.Clear();
            foreach (Album a in _allAlbums)
            {
                if (getFolder(a) == byCategoryName)
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
            if (!_loggedin)
                login(e);

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
        
        public bool login(authEnvelope envelope)
        {
            _envelope = envelope;
            
            try
            {
                if (api != null) return true;
                api = AuthenticateUsingOAuth(envelope);
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

        private void logMsg(string msg)
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
        private async void loadImages(Album? a, bool singleAlbumMode, int size = 2)
        {
            if (a == null || a.Uris.AlbumImages == null) { 
                return; 
            }
            if (a.AlbumKey=="B4rMTp")
            {
                Console.WriteLine("andy");
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
                        var imageUrl = imageSize.MediumImageUrl;
                        if (size != 2)
                        {

                            imageUrl = imageSize.TinyImageUrl;
                            logMsg("dummy code - using small size");
                        } //test code

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
        private void loadAllImages()
        {
            _allAlbums = new List<Album>();
            try
            {
                if (checkLogin(_envelope))
                {
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
                        Album a = null;
                        if (_galleryTable.Rows.Count > 0)
                        {
                            for (int i = 0; i < _galleryTable.Rows.Count; i++)
                            {
                                var cat = _galleryTable.Rows[i].ItemArray[0].ToString();
                                var gal = _galleryTable.Rows[i].ItemArray[1].ToString();
                                a = _allAlbums.Find(Album => Album.Name == gal);
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


    public class Authenticator
    {
        static public string Encrypt(string password, string salt)
        {
            var passwordBytes = Encoding.Unicode.GetBytes(password);
            var saltBytes = Encoding.Unicode.GetBytes(salt);

            var cipherBytes = ProtectedData.Protect(passwordBytes, saltBytes, DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(cipherBytes);
        }

        static public string Decrypt(string cipher, string salt)
        {
            var cipherBytes = Convert.FromBase64String(cipher);
            var saltBytes = Encoding.Unicode.GetBytes(salt);

            var passwordBytes = ProtectedData.Unprotect(cipherBytes, saltBytes, DataProtectionScope.CurrentUser);

            return Encoding.Unicode.GetString(passwordBytes);
        }
    }


    public class loginInfo
    {
        public String login;
        public String password;
    }
    public class GalleryEntry
    {
        public String category;
        public String gallery;
    }

    public class CSettings
    {
        public bool load_all;
        public int quality;
        public int speed_s;
        public bool showInfo;
        public int gridWidth;
        public int gridHeight;
        public int borderThickness;
        public CSettings()
        {
            quality = 2;
            speed_s = 6;
            load_all = false;
            showInfo = true;
            gridWidth = 5;
            gridHeight = 4;
            borderThickness = 0;
        }
    }

}
