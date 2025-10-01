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
        private static readonly string Endpoint = Constants.trackingUrl;
        private const bool Enabled = true; // Set true to enable tracking
        private const int TimeoutSeconds = 2;

        // Session state used for shutdown
        private string _username, _host, _appname;
        private DateTime _startTime;

        private static string getEndpoint()
        {
            if (!Enabled) return null;
            return string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint;
        }

        // Initialize details and send an initial phoneHome
        public void setup(TrackerDetails details)
        {
            if (details == null) return;
            _username = details.Username;
            _host = details.Host;
            _appname = details.AppName;
            if (_startTime == default(DateTime))
            {
                _startTime = DateTime.Now;
            }
            phoneHome(details);
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            try
            {
                var payload = BuildPayload(details);
                // Synchronously wait (bounded) so process doesn't exit before send completes
                SendAsync(endpoint, payload, cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // best-effort only; ignore failures/timeouts
                Debug.WriteLine($"Tracker phoneHome failed: {ex.Message}");
            }
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

            sb.Append('\u007d'); // '}'
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
