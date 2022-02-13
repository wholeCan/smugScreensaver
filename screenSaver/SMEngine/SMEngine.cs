using andyUtility;
using Microsoft.Win32;  //registry.
using SmugMugModel;
using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Originally written -- 4/2014
    /// 
    /// revision 5/8/2018:  Switched to Smugmug api 1.3 by upgrading to nuget version of SmugMugModel
    /// SmugMug API is now available from nuget

    //    Following along source from:
    //https://github.com/AlexGhiondea/SmugMug.NET/blob/master/nuGet/SmugMugModel.v2.nuspec

    //Need to better understand the api
    //https://api.smugmug.com/api/v2/doc/pages/concepts.html

    ///
    /// 2018 feature enhancements:
    /// put a timeout period, stop pulling images after a couple hours.  restart after 24 hours.
    /// </summary>

    public class CSMEngine
    {
        private static DateTime timeStarted = DateTime.Now;

        Dictionary<String, SmugMugModel.Image> _imageDictionary;
        private Site mysite;
        private MyUser _user;
        public System.Data.DataTable _galleryTable;
        private static List<Album> _allAlbums;
        Queue<ImageSet> _imageQueue;


        private const int maximumQ = 10;   //window, only download if q is less than max and greater than min.
        private const int minQ = 5;
        private volatile bool running = false;


        private const String apiKey = "TflvkBWIC1j26vLhO2GDLn7Lv6WrfdWn";  //from website
        private const String apiSecret = "9ecd2a3c4348e319c481f28d23b2941e"; //from website

        //The following information was gathered by pulling info from the test app that came with the SmugMug API.
        // static Token accessTok = new Token() { id = "8NH2CxKTCFjcPLGddGNVp4rqDz5ffzgV", Secret = "6ChgJLLcqzz8FgMMPZWZCN7THGmR3hGLFD2Z5jFmxp2vxtTzBJWHtZH7CQb7ZZJG" };
        static Token accessTok = new Token() { id = null, Secret = null };


        static string token = null, tokenSecret = null;
        private loginInfo _login;
        private const int qualityValue = 0;
        static Random r = new Random();
        public CSMEngine(bool doStart)
        {
            //if (r==null)
            //r = new Random();

            salt = 0xbad7eed;
            _settings = new CSettings();
            _imageQueue = new Queue<ImageSet>();
            _galleryTable = new System.Data.DataTable();
            _imageDictionary = new Dictionary<string, Image>();
            _galleryTable.Columns.Add(new System.Data.DataColumn("Category", typeof(String)));
            _galleryTable.Columns.Add(new System.Data.DataColumn("Album", typeof(String)));
            //  lock (_allAlbums)
            //{
            _allAlbums = new List<Album>();
            //}
            _login = new loginInfo();
            loadConfiguration();
            if (doStart)
                start();
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

                // AAAAAAAAAAARGH, an error!
                // ShowErrorMessage(e, "Writing registry " + KeyName.ToUpper());
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
                    // AAAAAAAAAAARGH, an error!
                    //ShowErrorMessage(e, "Reading registry " + KeyName.ToUpper());
                    return null;
                }
            }
            sk1.Close();
            rk.Close();

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
                galleries_present = 0;

            }
            for (int i = 0; i < galleries_present; i++)
            {
                GalleryEntry g = new GalleryEntry();
                g.cat = Read("Cat_" + i.ToString());
                g.gal = Read("Gal_" + i.ToString());
                //              _galleries.Add(g);

                addGallery(g.cat, g.gal);
                //System.Threading.Thread.Sleep(0);
            }


        }
        private void saveGalleries()
        {
            int galleries_present = _galleryTable.Rows.Count;
            Write("GalleryCount", galleries_present.ToString());
            for (int i = 0; i < galleries_present; i++)
            {
                var g = new GalleryEntry();
                g.cat = _galleryTable.Rows[i].ItemArray[0].ToString();
                g.gal = _galleryTable.Rows[i].ItemArray[1].ToString();
                Write("Cat_" + i.ToString(), g.cat);
                Write("Gal_" + i.ToString(), g.gal);

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


        public async Task<String[]> getCategoriesAsync()
        {
            List<String> categories = new List<String>();

            gettingCategories = true;

            //todo
            if (_user != null)
            {
                var myCats = await _user.GetCategoriesAsync();  //how can I make this async?
                                                                //    myCats.Sort();
                                                                //     myCats.Reverse();

                //myCats.Sort((c1, c2) => c1.Name.CompareTo(c2.Name));
                //myCats.Reverse();
                foreach (Category c in myCats)
                {
                    categories.Add(c.Name);
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

                //todo
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

        }

        public String[] getAlbums(String category)
        {
            List<String> catAlbums = new List<String>();
            foreach (Album a in _allAlbums)
            {
                if (a.Category.Name.Equals(category)  /*&& (bool)a..Public == true*/)
                {
                    catAlbums.Add(a.Title);
                }
            }
            return catAlbums.ToArray();
        }
        public void addAllAlbums()
        {
            _galleryTable.Clear();
            foreach (Album a in _allAlbums)
            {
                addGallery(a.Category.Name, a.Title);
            }
        }

        public void addAllAlbums(string byCategoryName)
        {
            //  _galleryTable.Clear();
            foreach (Album a in _allAlbums)
            {
                if (a.Category.Name == byCategoryName)
                    addGallery(a.Category.Name, a.Title);
            }
        }


        private bool checkConnection()
        {
            bool Success = false;
            if (mysite != null && _user != null)
            {
                try
                {
                    Success = true;
                }
                catch (Exception ex)
                {
                    doException(ex.Message);
                    Console.WriteLine(ex.Message);
                }
            }
            else
            {
                Success = login();
            }
            return Success;

        }
        private bool _loggedin = false;
        public bool checkLogin()
        {
            if (!_loggedin)
                login();

            return _loggedin;
        }

        public Album checkCreateAlbum(String cat, String album)
        {
            var v = _allAlbums.Find(x => x.Title == album && x.Category.Name == cat);
            try
            {
                if (v == null)
                {//create album
                    var myCategory = _user.GetCategories();
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
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
            return v;
        }

        public void disableStuff()
        {
            running = false;
        }

        private static MyUser AuthorizeToSmugMug(Site mySite)
        {
            //These values should have already been populated
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                return null;
            }

            //  accessTok = new Token() { id = token, Secret = tokenSecret };
            string token, tokenSecret, tmpApi, tmpSecret;
            SmugMugSecretsAccess.ReadSecretsFromFile(out tmpApi, out tmpSecret, out token, out tokenSecret);
            accessTok.id = token;
            accessTok.Secret = tokenSecret;

            MyUser user = null;
            //if we have a token
            if (!(string.IsNullOrEmpty(accessTok.id) || string.IsNullOrEmpty(accessTok.Secret)))
            {
                user = mySite.Login(accessTok);  //works, with pre-defined token.
            }

            // If the token was valid
            if (user == null)
            {
                do
                {
                    // reauthorize

                    //andy: Authorization does notn work from here.  throw an exception for user to run utility.

                    //    accessTok = SmugMugAuthorize.AuthorizeSmugMug(mySite);

                    user = mySite.Login(accessTok);

                    if (user == null)
                    {
                        Console.WriteLine("Authorizing the user failed.");
                        throw new Exception("Authorizing the user failed");
                    }
                    else
                    {
                        Console.WriteLine("do nothing");
                        ////save the secrets to the file
                        //SmugMugSecretsAccess.SaveSecretsToFile(apiKey, apiSecret, accessTok.id, accessTok.Secret);
                    }
                } while (user == null);
            }

            return user;
        }


        public bool login()
        {
            //todo
            bool success = false;
            lock (this)
            {
                if (!_loggedin)
                {
                    //These values should have already been populated
                    if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
                    {
                        throw new Exception("Invalid entry");
                    }
                    Site.Proxy = WebRequest.DefaultWebProxy;
                    mysite = new Site(apiKey, apiSecret);

                    try
                    {
                        // Token accessTok = new Token() { id = token, Secret = tokenSecret };
                        //for memory, use either email or andyholkan
                        //      accessTok = SmugMugAuthorize.AuthorizeSmugMug(mysite);
                        _user = AuthorizeToSmugMug(mysite);  //api 1.3
                                                             //    _user = mysite.Login(accessTok);
                        lock (_allAlbums)
                        {
                            _allAlbums = new List<Album>();
                            _allAlbums = _user.GetAlbums(false);
                        }
                        success = true;

                    }
                    catch (Exception ex)
                    {
                        doException("login: " + ex.Message);
                        success = false;
                    }
                    _loggedin = success;
                }
                else
                {
                    success = _loggedin;
                }
            }
            return success;
        }

        public int qSize
        {
            get
            {
                return _imageQueue.Count;
            }
        }

        private void runImageCollection()
        {
            if (running == false)
            {
                while (gettingCategories)
                    System.Threading.Thread.Sleep(50);//experimental.
                running = true;
                bool startIt = true;
                while (running)
                {
                    if (_imageQueue.Count < minQ)
                        startIt = true;
                    if (_imageQueue.Count <= maximumQ && startIt)
                    {
                        try
                        {
                            var imageSet = getRandomImage();
                            if (imageSet.albumTitle != null)
                            {
                                lock (_imageQueue)
                                {
                                    _imageQueue.Enqueue(imageSet);
                                    System.Diagnostics.Debug.WriteLine($"Image queue depth: {qSize}");
                                }
                            }
                            else
                            {
                                System.Threading.Thread.Sleep(5);
                            }
                        }
                        catch (Exception ex)
                        {
                            running = false;
                            System.Diagnostics.Debug.WriteLine("Invalid login");
                        }
                    }
                    else
                    {
                        startIt = false;
                    }

                    System.Threading.Thread.Sleep(1);//don't overrun processor.
                }
                running = false;//reset if stopped for any reason.
            }
        }
        ThreadStart ts = null;
        Thread t = null;

        ThreadStart tsAlbumLoad = null;
        Thread tAlbumLoad = null;
        private void start()
        {
            if (tAlbumLoad == null)
            {
                tsAlbumLoad = new ThreadStart(loadAllImages);
                tAlbumLoad = new Thread(tsAlbumLoad);
                tAlbumLoad.IsBackground = true;
                tAlbumLoad.Start();
            }
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
                    var outStream = new MemoryStream();

                    var enc = new BmpBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bitmapImage));

                    enc.Save(outStream);
                    bitmap = new System.Drawing.Bitmap(outStream);

                    // return bitmap; <-- leads to problems, stream is closed/closing ...
                    outStream.Close();
                }
                catch (Exception ex)
                {
                    doException("bm2m: " + ex.Message);
                    Console.WriteLine(ex.Message);
                }
            }
            return bitmap;

        }


        DateTime? timeWentBlack = null;

        public bool warm()
        {
            const int warmupTime = 60; //seconds, don't black out images until alive for a minute.
            return DateTime.Now.Subtract(timeStarted).TotalSeconds > warmupTime;
        }
        public bool screensaverExpired()
        {
            const int maxRuntime = 1;  //only run for an hour to conserve smugmugs api.
            return DateTime.Now.Subtract(timeStarted).TotalHours > maxRuntime;
        }
        public bool isExpired()
        { return expired; }
        public void resetExpiredImageCollection()
        {
            timeStarted = DateTime.Now;
            timeWentBlack = null;
            expired = false;
            //start();
        }
        bool expired = false;
        void checkResetExpiration()
        {
            expired = true;
        }
        public ImageSet getImage()
        {
            ImageSet b = new ImageSet();
            lock (_imageQueue)
            {
                if (qSize > 0)
                {
                    b = _imageQueue.Dequeue();
                    System.Diagnostics.Debug.WriteLine($"Deque, Image queue depth: { qSize}");
                }
            }
            if (!screensaverExpired())
            {
                b.b = BitmapImage2Bitmap(b.bm); //cropImage(b.bm);//used to be simple bitmap2BitmapImage
            }
            else
            {
                b.b = getBlackImagePixel();
                checkResetExpiration();
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
            System.Diagnostics.Debug.WriteLine("Setting a black pixel!");

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

                    System.Diagnostics.Debug.WriteLine("#################### CROPPING IMAGE: w " + oldWidth + " to " + newWidth);
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
            System.Diagnostics.Debug.WriteLine("Scaling by " + scale.ToString());
            return resized;
        }

        private BitmapImage showImage(SmugMugModel.Image myimage, int quality)
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
                        String URL = myimage.SmallURL;
                        switch (quality)
                        {
                            case 0:
                                URL = myimage.TinyURL;
                                break;
                            case 1:
                                URL = myimage.MediumURL;
                                break;
                            case 2:
                                URL = myimage.SmallURL;
                                break;

                            case 3:
                                URL = myimage.LargeURL; break;
                            case 4:
                                URL = myimage.X3LargeURL; break;
                            case 5:
                                URL = myimage.OriginalURL;
                                break;
                            default:
                                break;


                        }

                        if (URL != null)
                        {
                            WebRequest request = WebRequest.Create(new Uri(URL, UriKind.Absolute));

                            request.Timeout = -1;
                            var response = request.GetResponse();
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
                            System.Diagnostics.Debug.WriteLine($"Get Image { myimage.FileName} took: {sw.ElapsedMilliseconds}ms.");
                            image.BeginInit();
                            memoryStream.Seek(0, SeekOrigin.Begin);

                            image.StreamSource = memoryStream;
                            image.EndInit();
                        }
                    }

                }
            }

            catch (System.Net.WebException ex)
            {//no connection, wait longer.
                System.Threading.Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                doException("showImage: " + ex.Message);
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                System.Threading.Thread.Sleep(50);
            }
            return image;

        }

        public ICollection<SmugMugModel.Image> getImageList(string cat, String albumName)
        {
            var a = _allAlbums.Find(x => x.Title == albumName);
            System.Diagnostics.Debug.Assert(a.Category.Name == cat);
            var _imageList = a.GetImages(true);
            return _imageList;
        }
        private void loadImages(Album a, bool singleAlbumMode)
        {
            //lock (_imageDictionary)
            {
                try
                {
                    if (singleAlbumMode)
                    {
                        lock (_imageDictionary)
                        {
                            _imageDictionary.Clear();
                        }
                    }
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();

                    List<SmugMugModel.Image> _imageList = a.GetImages(true);  //getting a list of all the images in the library.
                    sw.Stop();
                    lock (_imageDictionary)
                    {
                        foreach (var i in _imageList)
                        {
                            _imageDictionary.Add(i.Key, i);
                        }
                    }
                }
                catch (Exception ex)
                {
                    doException("loadImages: " + ex.Message);
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                }
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
            public String date;
            public ImageSet()
            {
                caption = "";// new String("");
                exif = "";// new String("");
                date = "";// new String("");
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
        private async void loadAllImages()
        {
            try
            {
                if (checkLogin())
                {
                    if (_settings.load_all)
                    {
                        var ary = await getCategoriesAsync();
                        //Array.Sort(ary, StringComparer.InvariantCulture);


                        var rand = new Random();
                        var rnd = new Random();
                        var shuffledCats = ary.OrderBy(x => rnd.Next()).ToList();

                        var shuffledAlbums = _allAlbums.OrderBy(x => rnd.Next()).ToList();

                        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        foreach (var a in shuffledAlbums)
                        {
                            while (gettingCategories)
                                System.Threading.Thread.Sleep(50);
                            if (a.Category.Name == "Surveilance")
                            {
                                System.Diagnostics.Debug.WriteLine("Throwing out surveilance");
                            }
                            else
                            {
                                if (!screensaverExpired())
                                {
                                    System.Diagnostics.Debug.WriteLine($"Loading {a.Category.Name}:{a.Title}");
                                    loadImages(a, false);
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("Skipping loadImages - expired");
                                }
                            }
                            System.Threading.Thread.Sleep(1);
                        }

                        System.Diagnostics.Debug.WriteLine($"Time to load all albums: {sw.ElapsedMilliseconds} milliseconds");
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
                                a = _allAlbums.Find(Album => Album.Title == gal);
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
                //invalid login.  Stopping.
                System.Diagnostics.Debug.WriteLine("invalid login.  Stopping.");
            }

        }



        private ImageSet getRandomImage()
        {

            checkLogin();
            //            if (checkConnection())
            {
                //assume configuration is loaded.
                //load an image from smugmug, and return it.
                var myimage = new SmugMugModel.Image();//todo..  select a random image;



                Album a = null;
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
                            imageSet.bm = showImage(element, myQuality);
                            imageSet.albumTitle = element.Album.Title;
                            imageSet.CAtegory = element.Album.Category.Name;
                            imageSet.caption = element.Caption ?? "";//.Caption;
                            imageSet.date = element.Date ?? null;
                            imageSet.exif = element.GetEXIF().ToString();
                        }
                        catch (Exception ex)
                        {
                            doException("random: " + ex.Message);
                            System.Diagnostics.Debug.WriteLine(ex.Message + "\r\n" + ex.StackTrace);
                        }

                    }
                }




                return imageSet;
            }
        }
        public void doException(String msg)
        {
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
        public String cat;
        public String gal;
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
