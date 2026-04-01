using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;


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
        private static DateTime? _startTime;
        private DateTime? _lastWeeklyUpdate;
        private System.Timers.Timer _weeklyUpdateTimer;
        private long _lastImageCounter = 0;

        private static string getEndpoint()
        {
            if (!Enabled) return null;
            return string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint;
        }

        // Initialize details and send an initial phoneHome
        private void Setup(TrackerDetails details)
        {
            if (details == null)
                return;
            _username = details.Username;
            _host = details.Host;
            _appname = details.AppName;

            _startTime ??= DateTime.Now;

            phoneHome(details);
        }

        public Tracker(TrackerDetails details)
        {
            Setup(details);
            StartWeeklyUpdateTimer();
        }

        //unused
        private Tracker()
        {
            StartWeeklyUpdateTimer();
        }

        private void StartWeeklyUpdateTimer()
        {
            // Timer interval: 1 hour (in milliseconds) - check weekly status every hour
            const double timerIntervalMs = 60 * 60 * 1000; // 1 hour

            _weeklyUpdateTimer = new System.Timers.Timer(timerIntervalMs)
            {
                AutoReset = true,
                Enabled = true
            };
            _weeklyUpdateTimer.Elapsed += OnWeeklyUpdateTimerElapsed;
        }

        private void OnWeeklyUpdateTimerElapsed(object sender, ElapsedEventArgs e)
        {
            // Check if a week has passed since last update
            if (_lastWeeklyUpdate == null || (DateTime.Now - _lastWeeklyUpdate.GetValueOrDefault()).TotalDays >= 7)
            {
                SendWeeklyUpdate();
            }
        }

        /// <summary>
        /// Updates the image counter for use in weekly statistics.
        /// Should be called periodically (e.g., when images are displayed).
        /// </summary>
        public void UpdateImageCounter(long imageCounter)
        {
            _lastImageCounter = imageCounter;
        }

        /// <summary>
        /// Retrieves the last image counter value.
        /// Used to restore the counter when the engine is reinitialized.
        /// </summary>
        public long GetLastImageCounter()
        {
            return _lastImageCounter;
        }

        public void SendTriggeredUpdate()
        {
            if (_startTime == null || string.IsNullOrEmpty(_appname) || string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_host))
                return;

            var uptime = DateTime.Now - _startTime.GetValueOrDefault();
            var notes = new TrackerNotes
            {
                UptimeSeconds = (long)Math.Max(0, uptime.TotalSeconds),
                version = (Assembly.GetEntryAssembly()?.GetName().Version).ToString(),
                imageCounter = _lastImageCounter,
                buildDate = GetBuildDate(),
                triggeredBy = "manual"
            };

            phoneHome(new TrackerDetails
            {
                AppName = _appname,
                Host = _host,
                Username = _username,
                Notes = notes
            });
        }

        private void SendWeeklyUpdate()
        {
            if (_startTime == null || string.IsNullOrEmpty(_appname) || string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_host))
            {
                // Not yet initialized, skip this update
                return;
            }

            var uptime = DateTime.Now - _startTime.GetValueOrDefault();
            var notes = new TrackerNotes
            {
                UptimeSeconds = (long)Math.Max(0, uptime.TotalSeconds),
                version = (Assembly.GetEntryAssembly()?.GetName().Version).ToString(),
                imageCounter = _lastImageCounter,
                buildDate = GetBuildDate(),
                triggeredBy = "weekly-uptime"
            };

            phoneHome(new TrackerDetails
            {
                AppName = _appname,
                Host = _host,
                Username = _username,
                Notes = notes
            });

            _lastWeeklyUpdate = DateTime.Now;
        }

        public void shutdown(long imageCounter)
        {
            TrackerNotes notes = null;
            if (_startTime != default(DateTime))
            {
                var uptime = DateTime.Now - _startTime.GetValueOrDefault();
                notes = new TrackerNotes
                {
                    UptimeSeconds = (long)Math.Max(0, uptime.TotalSeconds),
                    version = (Assembly.GetEntryAssembly()?.GetName().Version).ToString(),
                    imageCounter = imageCounter,
                    buildDate = GetBuildDate(),
                    triggeredBy = "shutdown"
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
            Thread.Sleep(1); // brief pause to help ensure send completes before process exits
        }

        /// <summary>
        /// Gets the build date/time of the entry assembly.
        /// </summary>
        /// <returns>ISO 8601 formatted build date string, or null if unable to determine.</returns>
        private static string? GetBuildDate()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly();
                if (assembly == null) return null;

                var filePath = assembly.Location;
                if (string.IsNullOrEmpty(filePath)) return null;

                var fileInfo = new System.IO.FileInfo(filePath);
                return fileInfo.LastWriteTimeUtc.ToString("O"); // ISO 8601 format
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Disposes the tracker resources, including the weekly update timer.
        /// </summary>
        public void Dispose()
        {
            _weeklyUpdateTimer?.Stop();
            _weeklyUpdateTimer?.Dispose();
        }
    }

}
