using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;


namespace SMEngine
{
    public class Tracker
    {
        private static readonly HttpClient _http = new HttpClient();

        // Hard-coded configuration (no ConfigurationManager)
        private const string Endpoint = "http://smugtracker.andyholkan.com:3003/track"; // curl-compatible endpoint
        private const bool Enabled = true; // Set true to enable tracking
        private const int TimeoutSeconds = 3;

        private static string getEndpoint()
        {
            if (!Enabled) return null;
            return string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint;
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public void phoneHome(string appName, string host, string username)
        {
            var endpoint = getEndpoint();
            if (string.IsNullOrWhiteSpace(endpoint)) return; // disabled if not configured
            
            // Fire-and-forget
            Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                    // Match curl body: {"appName":"MyApp","host":"localhost:8080","username":"john_doe"}
                    var payload = "{\"appName\":\"" + JsonEscape(appName) + "\"," +
                                  "\"host\":\"" + JsonEscape(host) + "\"," +
                                  "\"username\":\"" + JsonEscape(username) + "\"}";
                    using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                    using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = content
                    };
                    var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
                    // no throw; just best-effort
                }
                catch (Exception ex)
                {
                    // swallow all; optionally trace in debug
                    Debug.WriteLine($"Tracker phoneHome failed: {ex.Message}");
                }
            });
        }
    }

}
