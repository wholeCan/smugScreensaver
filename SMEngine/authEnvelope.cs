namespace SMEngine
{
    public class authEnvelope
    {
        public string consumerToken;
        public string consumerSecret;
        public string token;
        public string tokenSecret;
        public authEnvelope()
        {

        }
        public authEnvelope(string a, string b, string c, string d)
        {
            consumerToken = a;
            consumerSecret = b;
            token = c;
            tokenSecret = d;
        }
    }

}
