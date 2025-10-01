using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace SMEngine
{
    public class Tracker
    {
        private static readonly HttpClient _http = new HttpClient();

        // Hard-coded configuration (no ConfigurationManager)
        private const string Endpoint = "http://smugtracker.andyholkan.com:3003/track"; // curl-compatible endpoint
        private const bool Enabled = true; // Set true to enable tracking
        private const int TimeoutSeconds = 3;

        // Session state used for shutdown
        private string _username, _host, _appname;
        private DateTime _startTime;

        private static string getEndpoint()
        {
            if (!Enabled) return null;
            return string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint;
        }

        public void shutdown()
        {
            TrackerNotes notes = null;
            if (_startTime != default(DateTime))
            {
                var uptime = DateTime.Now - _startTime;
                notes = new TrackerNotes
                {
                    UptimeSeconds = (long)Math.Max(0, uptime.TotalSeconds)
                };
            }

            phoneHome(new TrackerDetails
            {
                AppName = _appname ?? "SMEngine",
                Host = _host ?? "shutdown",
                Username = _username ?? Environment.UserName,
                Notes = notes
            });
        }

        // Escapes a string for JSON string literal context
        private static string JsonEscape(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public void phoneHome(TrackerDetails details)
        {
            if (details == null) return;

            // Cache for shutdown and initialize session start
            _username = details.Username;
            _host = details.Host;
            _appname = details.AppName;
            if (_startTime == default(DateTime))
            {
                _startTime = DateTime.Now;
            }

            var endpoint = getEndpoint();
            if (string.IsNullOrWhiteSpace(endpoint)) return; // disabled if not configured

            // Fire-and-forget send
            Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
                try
                {
                    var payload = BuildPayload(details);
                    await SendAsync(endpoint, payload, cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Tracker phoneHome failed: {ex.Message}");
                }
            });
        }

        private string BuildPayload(TrackerDetails details)
        {
            var sb = new StringBuilder();
            sb.Append("{\"appName\":\"").Append(JsonEscape(details.AppName)).Append("\",");
            sb.Append("\"host\":\"").Append(JsonEscape(details.Host)).Append("\",");
            sb.Append("\"username\":\"").Append(JsonEscape(details.Username)).Append("\"");

            if (details.Notes != null)
            {
                sb.Append(",\"notes\":").Append(details.Notes.ToJson());
            }

            sb.Append('}');
            return sb.ToString();
        }

        private async Task SendAsync(string endpoint, string payload, CancellationToken token)
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            await _http.SendAsync(req, token).ConfigureAwait(false);
        }
    }

}
