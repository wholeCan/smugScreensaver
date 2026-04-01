using System.Text;

namespace SMEngine
{
    // Simple, strongly-typed notes payload for Tracker
    public class TrackerNotes
    {
        public long? UptimeSeconds { get; set; }
        // Add more note fields over time as needed
        public string? version { get; set; }

        public long? imageCounter {  get; set; }
        public string? buildDate { get; set; }
        public string? triggeredBy { get; set; }
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
        
            if (version != null)  //todo, figure out why receiver isn't able to process this.
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"\"version\":\"{version}\"");
            }
            if (imageCounter != null)
            {
                if (!first) sb.Append(',');
                sb.Append($"\"imageCounter\":\"{imageCounter}\"");
            }
            if (buildDate != null)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"\"buildDate\":\"{buildDate}\"");
            }
            if (triggeredBy != null)
            {
                if (!first) sb.Append(',');
                sb.Append($"\"triggeredBy\":\"{triggeredBy}\"");
            }

            sb.Append('}');
            return sb.ToString();
        }
    }
}
