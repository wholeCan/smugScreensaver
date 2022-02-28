using System.Diagnostics;
using System.Net;
using SmugMug.NET;
using System.Configuration;
using System.IO;
using Nito.AsyncEx;

namespace SmugMugTest
{
    class Program
    {


        const string CONSUMERTOKEN = "SmugMugOAuthConsumerToken";
        const string CONSUMERSECRET = "SmugMugOAuthConsumerSecret";
        const string ACCESSTOKEN = "SmugmugOauthAccessToken";
        const string ACCESSTOKENSECRET = "SmugmugOauthAccessTokenSecret";

        public static SmugMugAPI AuthenticateUsingAnonymous()
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
        public static SmugMugAPI AuthenticateUsingOAuth()
        {
            //Access OAuth keys from App.config
            string consumerKey = fetchKey(CONSUMERTOKEN);
            

            string consumerSecret = fetchKey(CONSUMERSECRET);

            //todo: these should be stored/fetched from registry.
            string accessToken = fetchKey(ACCESSTOKEN);
            var accessTokenSecret = fetchKey(ACCESSTOKENSECRET);

            OAuthCredentials oAuthCredentials = null;
            if (accessToken != null)
            {//todo: right now this is obviously hardcoded
                oAuthCredentials = new OAuthCredentials(consumerKey, consumerSecret, accessToken, accessTokenSecret);
            }
            else
            {
                oAuthCredentials = GenerateOAuthAccessToken(consumerKey, consumerSecret); 
            }

            //Console.WriteLine("token {0} secret {1}", oAuthCredentials.AccessToken, oAuthCredentials.AccessTokenSecret);
                
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

            //todo: this is where authorization is setup
            #region Authorization
            string authorizationUrl = String.Format("{0}{1}?mode=auth_req_token&oauth_token={2}&Access=Full&Permissions=READ", baseUrl, authorizeUrl, requestToken);
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


        /// <summary>
        /// 
        static void Main(string[] args)
        {
            AsyncContext.Run(() => MainAsync(args));
        }

        static async void MainAsync(string[] args)
        {

            try
            {
                var api = AuthenticateUsingOAuth();
                

                var user = await api.GetUser("andyholkan");
                Console.WriteLine("Authenticated user {0}, downloading albums", user.Name);
                var albums = await api.GetAlbums(user, 100);

                var folder = albums.First().Uris.Folder;
                /*foreach (var album in albums.GetRange(0,10))
                {
                    Console.WriteLine(album.Name);
                }*/

                //var rootNode = await api.GetRootNode(user);

                var album1 = albums.First(
                    //x => x.Name.Contains("Canyon")
                    );
                Console.WriteLine("Album '{0}' has {1} images: gallery {2}", album1.Name, album1.ImageCount, album1.Comments);
                var albumImagesWithSizes = await api.GetAlbumImagesWithSizes(album1, 100);
                
                foreach (var image in albumImagesWithSizes.AlbumImages)
                {
                    var imageSizes = albumImagesWithSizes.ImageSizes.Where(x => x.Key.Contains(image.ImageKey));
                    var MediumImageUrl = imageSizes.First().Value.ImageSizes.MediumImageUrl;

                    //bingo: we now have the medium url - or whatever we want!
                    Console.WriteLine("fetching image {0}, {1}", 
                         (image.Caption != null) ?
                           image.Caption : image.Title, 
                         MediumImageUrl);
                  
                    //var albumImage = await api.GetAlbumImage(album1, image.ImageKey);
                    //Console.WriteLine("'{0}' ({1}) with keywords \"{2}\" {3}", image.Title, image.FileName, image.Keywords, image.Uri);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                Console.WriteLine("End");

            }
        }

    }
}
