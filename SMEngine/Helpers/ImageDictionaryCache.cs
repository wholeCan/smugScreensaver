using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SMEngine
{
    /// <summary>
    /// Persists the loaded image dictionary (and played-images set) to a local JSON file so
    /// the app can skip re-fetching the album/image list from the network on every startup.
    /// The cache is only ever consulted at startup; it is never refreshed in the background.
    /// </summary>
    internal static class ImageDictionaryCache
    {
        private class CacheEnvelope
        {
            public DateTime CreatedUtc { get; set; }
            public string Fingerprint { get; set; }
            public int AlbumCount { get; set; }
            public Dictionary<string, CSMEngine.ImageSet> Images { get; set; }
            public Dictionary<string, CSMEngine.ImageSet> PlayedImages { get; set; }
        }

        private static string GetCacheFilePath(string appName)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                string.IsNullOrEmpty(appName) ? "smugScreensaver" : appName,
                "cache");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "imagedictionary.json");
        }

        /// <summary>
        /// Computes a fingerprint representing everything that should invalidate the cache
        /// if it changes: which account(s) are configured, whether we're loading all albums
        /// or a specific set, which albums are selected (single-album mode), and any excluded
        /// folder keywords.
        /// </summary>
        public static string ComputeFingerprint(CSettings settings, DataTable galleryTable, IEnumerable<string> usernames)
        {
            var sb = new StringBuilder();
            sb.Append("users=").Append(string.Join(",", usernames ?? Enumerable.Empty<string>()));
            sb.Append(";loadAll=").Append(settings?.load_all ?? false);
            sb.Append(";excluded=").Append(string.Join(",", settings?.excludedFolders ?? new List<string>()));

            if (settings != null && !settings.load_all && galleryTable != null)
            {
                var albums = galleryTable.Rows
                    .Cast<DataRow>()
                    .Select(r => $"{r.ItemArray[0]}|{r.ItemArray[1]}")
                    .OrderBy(s => s, StringComparer.Ordinal);
                sb.Append(";albums=").Append(string.Join(",", albums));
            }

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return Convert.ToBase64String(bytes);
            }
        }

        /// <summary>
        /// Attempts to load the cache. Returns false (with empty dictionaries) on any miss:
        /// no file, unreadable/corrupt file, fingerprint mismatch, or expired age.
        /// </summary>
        public static bool TryLoad(
            string appName,
            string fingerprint,
            TimeSpan maxAge,
            out Dictionary<string, CSMEngine.ImageSet> images,
            out Dictionary<string, CSMEngine.ImageSet> playedImages,
            out int albumCount)
        {
            images = null;
            playedImages = null;
            albumCount = 0;
            var path = GetCacheFilePath(appName);
            try
            {
                if (!File.Exists(path)) return false;

                var json = File.ReadAllText(path);
                var envelope = JsonConvert.DeserializeObject<CacheEnvelope>(json);
                if (envelope == null) return false;

                if (envelope.Fingerprint != fingerprint)
                {
                    logMsg("Image dictionary cache miss: fingerprint mismatch (settings/account changed).");
                    return false;
                }

                if (DateTime.UtcNow - envelope.CreatedUtc > maxAge)
                {
                    logMsg("Image dictionary cache miss: expired.");
                    return false;
                }

                images = envelope.Images ?? new Dictionary<string, CSMEngine.ImageSet>();
                playedImages = envelope.PlayedImages ?? new Dictionary<string, CSMEngine.ImageSet>();
                albumCount = envelope.AlbumCount;
                logMsg($"Image dictionary cache hit: {images.Count} images, {playedImages.Count} played, age {DateTime.UtcNow - envelope.CreatedUtc}.");
                return true;
            }
            catch (Exception ex)
            {
                logMsg($"Image dictionary cache load failed, ignoring cache: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Writes the cache atomically (write to temp file, then replace) so a crash mid-write
        /// never leaves a corrupt/partial cache file behind.
        /// </summary>
        public static void Save(
            string appName,
            string fingerprint,
            Dictionary<string, CSMEngine.ImageSet> images,
            Dictionary<string, CSMEngine.ImageSet> playedImages,
            int albumCount)
        {
            var path = GetCacheFilePath(appName);
            var tempPath = path + ".tmp";
            try
            {
                var envelope = new CacheEnvelope
                {
                    CreatedUtc = DateTime.UtcNow,
                    Fingerprint = fingerprint,
                    AlbumCount = albumCount,
                    Images = images ?? new Dictionary<string, CSMEngine.ImageSet>(),
                    PlayedImages = playedImages ?? new Dictionary<string, CSMEngine.ImageSet>()
                };
                var json = JsonConvert.SerializeObject(envelope);
                File.WriteAllText(tempPath, json);

                if (File.Exists(path)) File.Delete(path);
                File.Move(tempPath, path);

                logMsg($"Image dictionary cache saved: {envelope.Images.Count} images, {envelope.PlayedImages.Count} played.");
            }
            catch (Exception ex)
            {
                logMsg($"Image dictionary cache save failed: {ex.Message}");
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            }
        }

        public static void Invalidate(string appName)
        {
            try
            {
                var path = GetCacheFilePath(appName);
                if (File.Exists(path)) File.Delete(path);
                logMsg("Image dictionary cache invalidated.");
            }
            catch (Exception ex)
            {
                logMsg($"Image dictionary cache invalidate failed: {ex.Message}");
            }
        }

        private static void logMsg(string msg)
        {
            Debug.WriteLine(DateTime.Now.ToLongTimeString() + ": " + msg);
        }
    }
}
