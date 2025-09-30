using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using SMEngine;
using SmugMug.NET;

namespace SMEngine
{
    internal static class AuthHelper
    {
        public static authEnvelope ReadTokensFromRegistry()
        {
            var envelope = new authEnvelope();
            try
            {
                envelope.consumerToken = Constants.apiToken;
                envelope.consumerSecret = Constants.apiSecret;
            }
            catch
            {
                throw new ApplicationException("invalid token, sorry you lose.");
            }

            try
            {
                envelope.token = Authenticator.Decrypt(RegistryHelper.ReadString(CSMEngine.ACCESSTOKEN1, string.Empty), CSMEngine.Salt);
                envelope.tokenSecret = Authenticator.Decrypt(RegistryHelper.ReadString(CSMEngine.ACCESSTOKENSECRET1, string.Empty), CSMEngine.Salt);
            }
            catch
            {
                envelope.token = string.Empty;
                envelope.tokenSecret = string.Empty;
            }
            return envelope;
        }

        public static void WriteTokensToRegistry(authEnvelope envelope)
        {
            RegistryHelper.WriteString(CSMEngine.ACCESSTOKEN1, Authenticator.Encrypt(envelope.token, CSMEngine.Salt));
            RegistryHelper.WriteString(CSMEngine.ACCESSTOKENSECRET1, Authenticator.Encrypt(envelope.tokenSecret, CSMEngine.Salt));
        }

        public static SmugMugAPI AuthenticateUsingOAuth(authEnvelope envelope)
        {
            if (!string.IsNullOrEmpty(envelope?.token))
            {
                var creds = new OAuthCredentials(envelope.consumerToken, envelope.consumerSecret, envelope.token, envelope.tokenSecret);
                return new SmugMugAPI(LoginType.OAuth, creds);
            }
            return null;
        }

        public static SmugMugAPI AuthenticateUsingAnonymous()
        {
            string consumerKey = null;
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var keySetting = config.AppSettings.Settings[CSMEngine.CONSUMERTOKEN1];
            if (keySetting != null)
            {
                consumerKey = keySetting.Value;
            }
            if (string.IsNullOrEmpty(consumerKey))
            {
                throw new ConfigurationErrorsException("The OAuth consumer token must be specified in App.config");
            }
            return new SmugMugAPI(LoginType.Anonymous, new OAuthCredentials(consumerKey));
        }

        // Moved from CSMEngine: Request token + open browser for authorization
        public static authEnvelope GenerateOAuthAccessToken_UI_PART1(string consumerKey, string secret)
        {
            string baseUrl = "http://api.smugmug.com";
            string requestUrl = "/services/oauth/1.0a/getRequestToken";
            string authorizeUrl = "/services/oauth/1.0a/authorize";
            string requestToken = null;
            string requestTokenSecret = null;

            // Request Token
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

            // Authorization
            string authorizationUrl = string.Format("{0}{1}?mode=auth_req_token&oauth_token={2}&Access=Full&Permissions=Modify", baseUrl, authorizeUrl, requestToken);
            var ps = new ProcessStartInfo(authorizationUrl)
            {
                UseShellExecute = true,
                Verb = "open"
            };
            try
            {
                Process.Start(ps);
            }
            catch (System.ComponentModel.Win32Exception noBrowser)
            {
                if (noBrowser.ErrorCode == -2147467259)
                {
                    throw new Exception("no browser");
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("AuthHelper exception: " + e.Message);
                throw;
            }

            return new authEnvelope("", "", requestToken, requestTokenSecret);
        }

        // Moved from CSMEngine: Exchange request token + verifier for access token
        public static OAuthCredentials GenerateOAuthAccessToken_UI_PART2(authEnvelope envelope, string sixDigitCode)
        {
            string baseUrl = "http://api.smugmug.com";
            string accessUrl = "/services/oauth/1.0a/getAccessToken";
            string verifier = sixDigitCode;

            var gRequest = OAuth.OAuthRequest.ForAccessToken(
                envelope.consumerToken,
                envelope.consumerSecret,
                envelope.token,
                envelope.tokenSecret,
                verifier);
            gRequest.RequestUrl = baseUrl + accessUrl;
            var auth = gRequest.GetAuthorizationHeader();
            var request = (HttpWebRequest)WebRequest.Create(gRequest.RequestUrl);
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

            return new OAuthCredentials(
                envelope.consumerToken,
                envelope.consumerSecret,
                envelope.token,
                envelope.tokenSecret
            );
        }
    }
}
