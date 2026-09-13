using System;
using System.IO;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace andyScreenSaver.windows.Helpers
{
    // Metadata cached alongside a smart-start jpg so the caption can be restored on restart.
    internal sealed class CachedImageMeta
    {
        public string Caption { get; set; } = string.Empty;
        public bool ShowCaptions { get; set; }
    }

    internal readonly struct InitialTileImage
    {
        public InitialTileImage(BitmapImage image, string caption, bool showCaptions)
        {
            Image = image;
            Caption = caption ?? string.Empty;
            ShowCaptions = showCaptions;
        }

        public BitmapImage Image { get; }
        public string Caption { get; }
        public bool ShowCaptions { get; }
    }

    internal static class InitialImageProvider
    {
        public static InitialTileImage Build(int imageIndex, string storageDirectory, bool doSmartStart, string fallbackResourceUri)
        {
            string? path = null;
            var meta = new CachedImageMeta();

            if (doSmartStart)
            {
                var candidate = Path.Combine(storageDirectory, imageIndex + ".jpg");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    meta = LoadMeta(GetMetaPath(storageDirectory, imageIndex)) ?? meta;
                }
            }

            var chosen = path ?? fallbackResourceUri;
            var image = CreateBitmapImage(chosen, fallbackResourceUri);
            return new InitialTileImage(image, meta.Caption, meta.ShowCaptions);
        }

        public static void SaveMeta(string storageDirectory, int imageIndex, string caption, bool showCaptions)
        {
            var meta = new CachedImageMeta { Caption = caption ?? string.Empty, ShowCaptions = showCaptions };
            var json = JsonConvert.SerializeObject(meta);
            File.WriteAllText(GetMetaPath(storageDirectory, imageIndex), json);
        }

        private static string GetMetaPath(string storageDirectory, int imageIndex) =>
            Path.Combine(storageDirectory, imageIndex + ".json");

        private static CachedImageMeta? LoadMeta(string metaPath)
        {
            try
            {
                if (!File.Exists(metaPath)) return null;
                var json = File.ReadAllText(metaPath);
                return JsonConvert.DeserializeObject<CachedImageMeta>(json);
            }
            catch
            {
                return null;
            }
        }

        private static BitmapImage CreateBitmapImage(string uri, string fallbackResourceUri)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = ResolveUri(uri);
                bi.EndInit();
                return bi;
            }
            catch
            {
                var fallback = new BitmapImage();
                fallback.BeginInit();
                fallback.CacheOption = BitmapCacheOption.OnLoad;
                fallback.UriSource = ResolveUri(fallbackResourceUri);
                fallback.EndInit();
                return fallback;
            }
        }

        // Disk paths (the smart-start jpg cache) are always rooted filesystem paths, which the
        // Uri class parses fine on its own (IsWellFormedUriString is too strict about escaping
        // for raw Windows paths and can't be used to distinguish the two cases). WPF
        // pack-resource references (the built-in fallback image) start with "/" and need an
        // explicit pack://application base, or WPF resolves them against the current directory.
        private static Uri ResolveUri(string uri)
        {
            if (uri.StartsWith("/"))
                return new Uri("pack://application:,,," + uri, UriKind.Absolute);

            return new Uri(uri, UriKind.Absolute);
        }
    }
}
