using System.Text;
using SMEngine;

namespace andyScreenSaver.windows.Helpers
{
    internal static class CaptionBuilder
    {
        public static string Build(SMEngine.CSMEngine.ImageSet s)
        {
            var sb = new StringBuilder();
            if (s == null) return string.Empty;

            if (!string.IsNullOrEmpty(s.AlbumTitle))
                sb.Append($"{s.Category}: {s.AlbumTitle}");
            else
                sb.Append(s.Category);

            if (!string.IsNullOrEmpty(s.Caption) && !s.Caption.Contains("OLYMPUS"))
                sb.Append($": {s.Caption}");

            return sb.ToString();
        }
    }
}
