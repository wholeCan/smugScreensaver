using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;


namespace SMEngine
{
    public class Tracker
    {
        private static readonly HttpClient _http = new HttpClient();

        // Hard-coded configuration (no ConfigurationManager)
        private const string Endpoint = "http://smugtracker.andyholkan.com:3003/track"; // curl-compatible endpoint
        private const bool Enabled = true; // Set true to enable tracking
        private const int TimeoutSeconds = 3;
        private string _username, _host, _appname;
        private DateTime _startTime;

        private static string getEndpoint()
        {
            if (!Enabled) return null;
            return string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint;
        }

        public void shutdown()
        {
            IDictionary<string, object> notes = null;
            if (_startTime != default(DateTime))
            {
                var uptime = DateTime.Now - _startTime;
                notes = new Dictionary<string, object>
                {
                    ["uptimeSeconds"] = (long)Math.Max(0, uptime.TotalSeconds)
                };
            }
            phoneHome(_appname ?? "SMEngine", _host ?? "shutdown", _username ?? Environment.UserName, notes);
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void AppendJsonValue(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Boolean:
                    sb.Append(((bool)value) ? "true" : "false");
                    break;
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case TypeCode.DateTime:
                    sb.Append('"').Append(((DateTime)value).ToUniversalTime().ToString("o")).Append('"');
                    break;
                default:
                    sb.Append('"').Append(JsonEscape(Convert.ToString(value))).Append('"');
                    break;
            }
        }

        private static string SerializeNotes(IDictionary<string, object> notes)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var kv in notes)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(JsonEscape(kv.Key)).Append('"').Append(':');
                AppendJsonValue(sb, kv.Value);
                
            }
            sb.Append('}');
            return sb.ToString();
        }

        public void phoneHome(string appName, string host, string username, IDictionary<string, object> notes = null)
        {
            _username = username;
            _host = host;
            _appname = appName;
            // Initialize start time only once; preserve for uptime calculation
            if (_startTime == default(DateTime))
            {
                _startTime = DateTime.Now;
            }

            var endpoint = getEndpoint();
            if (string.IsNullOrWhiteSpace(endpoint)) return; // disabled if not configured
            
            // Fire-and-forget
            Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));

                    var sb = new StringBuilder();
                    sb.Append("{\"appName\":\"").Append(JsonEscape(appName)).Append("\",");
                    sb.Append("\"host\":\"").Append(JsonEscape(host)).Append("\",");
                    sb.Append("\"username\":\"").Append(JsonEscape(username)).Append("\"");

                    if (notes != null && notes.Count > 0)
                    {
                        sb.Append(",\"notes\":").Append(SerializeNotes(notes));
                    }

                    sb.Append("}");

                    using var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json");
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
