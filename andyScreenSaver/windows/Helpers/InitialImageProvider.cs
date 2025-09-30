using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace andyScreenSaver.windows.Helpers
{
    internal static class InitialImageProvider
    {
        public static BitmapImage Build(int imageIndex, string storageDirectory, bool doSmartStart, string fallbackResourceUri)
        {
            string? path = null;
            if (doSmartStart)
            {
                var candidate = Path.Combine(storageDirectory, imageIndex + ".jpg");
                if (File.Exists(candidate))
                {
                    path = candidate;
                }
            }

            var chosen = path ?? fallbackResourceUri;
            return CreateBitmapImage(chosen, fallbackResourceUri);
        }

        private static BitmapImage CreateBitmapImage(string uri, string fallbackResourceUri)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                var kind = Uri.IsWellFormedUriString(uri, UriKind.Absolute) ? UriKind.Absolute : UriKind.Relative;
                bi.UriSource = new Uri(uri, kind);
                bi.EndInit();
                return bi;
            }
            catch
            {
                var fallback = new BitmapImage();
                fallback.BeginInit();
                fallback.CacheOption = BitmapCacheOption.OnLoad;
                fallback.UriSource = new Uri(fallbackResourceUri, UriKind.Relative);
                fallback.EndInit();
                return fallback;
            }
        }
    }
}
