using System.Text;

namespace SMEngine
{
    // Simple, strongly-typed notes payload for Tracker
    public class TrackerNotes
    {
        public long? UptimeSeconds { get; set; }
        // Add more note fields over time as needed

        internal string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            bool first = true;

            if (UptimeSeconds.HasValue)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("\"uptimeSeconds\":").Append(UptimeSeconds.Value);
            }

            sb.Append('}');
            return sb.ToString();
        }
    }
}
