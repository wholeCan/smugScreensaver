namespace SMEngine
{
    // Strongly-typed container for tracker payload
    public class TrackerDetails
    {
        public string AppName { get; set; }
        public string Host { get; set; }
        public string Username { get; set; }
        public TrackerNotes Notes { get; set; }
    }
}
