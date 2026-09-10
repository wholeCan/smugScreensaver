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
    /// the app can skip re-fetching the album/image list from the network. Consulted at the
    /// start of every load (startup or exhaustion-triggered); a network load only happens when
    /// the cache is missing, expired, invalidated, or its fingerprint no longer matches the
    /// current account/settings. Never refreshed in the background.
    /// </summary>
    internal static class ImageDictionaryCache
    {
        private class CacheEnvelope
        {
            // Order matters: streaming reads validate Fingerprint/CreatedUtc before ever
            // touching Images/PlayedImages, so the cheap header fields must serialize first.
            [JsonProperty(Order = 0)] public DateTime CreatedUtc { get; set; }
            [JsonProperty(Order = 1)] public string Fingerprint { get; set; }
            [JsonProperty(Order = 2)] public int AlbumCount { get; set; }
            [JsonProperty(Order = 3)] public Dictionary<string, CSMEngine.ImageSet> Images { get; set; }
            [JsonProperty(Order = 4)] public Dictionary<string, CSMEngine.ImageSet> PlayedImages { get; set; }
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
        /// Attempts to load the cache, streaming entries directly into <paramref name="targetImages"/>
        /// and <paramref name="targetPlayedImages"/> as they're parsed (one dictionary-lock per entry,
        /// same as the network loader) rather than parsing the whole file into memory first and bulk-
        /// copying it. This lets playback start as soon as the first few entries are read instead of
        /// stalling until the entire cache file (which can be large) has been fully parsed.
        /// Returns false on any miss: no file, unreadable/corrupt file, fingerprint mismatch, or expired
        /// age - in which case neither target dictionary is touched.
        /// </summary>
        public static bool TryLoad(
            string appName,
            string fingerprint,
            TimeSpan maxAge,
            object dictionaryLock,
            Dictionary<string, CSMEngine.ImageSet> targetImages,
            Dictionary<string, CSMEngine.ImageSet> targetPlayedImages,
            out int albumCount)
        {
            albumCount = 0;
            var path = GetCacheFilePath(appName);
            if (!File.Exists(path)) return false;

            try
            {
                using (var stream = File.OpenRead(path))
                using (var streamReader = new StreamReader(stream))
                using (var reader = new JsonTextReader(streamReader))
                {
                    var serializer = JsonSerializer.CreateDefault();
                    string readFingerprint = null;
                    DateTime createdUtc = default;
                    int imageCount = 0, playedCount = 0;

                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.PropertyName) continue;
                        var propertyName = (string)reader.Value;
                        if (!reader.Read()) break; // advance to the value token

                        switch (propertyName)
                        {
                            case nameof(CacheEnvelope.CreatedUtc):
                                createdUtc = serializer.Deserialize<DateTime>(reader);
                                break;
                            case nameof(CacheEnvelope.Fingerprint):
                                readFingerprint = (string)reader.Value;
                                if (readFingerprint != fingerprint)
                                {
                                    logMsg("Image dictionary cache miss: fingerprint mismatch (settings/account changed).");
                                    return false;
                                }
                                break;
                            case nameof(CacheEnvelope.AlbumCount):
                                albumCount = Convert.ToInt32(reader.Value);
                                break;
                            case nameof(CacheEnvelope.Images):
                                if (readFingerprint == null || DateTime.UtcNow - createdUtc > maxAge)
                                {
                                    logMsg("Image dictionary cache miss: expired or malformed header.");
                                    return false;
                                }
                                lock (dictionaryLock) { targetImages.Clear(); }
                                imageCount = StreamEntriesInto(reader, serializer, dictionaryLock, targetImages);
                                break;
                            case nameof(CacheEnvelope.PlayedImages):
                                playedCount = StreamEntriesInto(reader, serializer, dictionaryLock, targetPlayedImages);
                                break;
                        }
                    }

                    if (readFingerprint == null)
                    {
                        logMsg("Image dictionary cache miss: no fingerprint found in file.");
                        return false;
                    }

                    logMsg($"Image dictionary cache hit: {imageCount} images, {playedCount} played, age {DateTime.UtcNow - createdUtc}.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                logMsg($"Image dictionary cache load failed, ignoring cache: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads a JSON object of the form { "key": {ImageSet}, ... } and adds each entry into
        /// <paramref name="target"/> as it's parsed, taking <paramref name="dictionaryLock"/> only
        /// for the duration of each individual add.
        /// </summary>
        private static int StreamEntriesInto(
            JsonTextReader reader,
            JsonSerializer serializer,
            object dictionaryLock,
            Dictionary<string, CSMEngine.ImageSet> target)
        {
            var count = 0;
            if (reader.TokenType == JsonToken.Null) return count;
            if (reader.TokenType != JsonToken.StartObject) { reader.Skip(); return count; }

            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if (reader.TokenType != JsonToken.PropertyName) continue;
                var key = (string)reader.Value;
                if (!reader.Read()) break; // advance to the ImageSet object
                var imageSet = serializer.Deserialize<CSMEngine.ImageSet>(reader);
                if (imageSet == null) continue;
                lock (dictionaryLock)
                {
                    target[key] = imageSet;
                }
                count++;
            }
            return count;
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
