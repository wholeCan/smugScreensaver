using System;
using System.IO;
using System.Linq;
using System.Threading;
using SMEngine;
using static SMEngine.CSMEngine;

namespace CliDownloader
{
    internal class Program
    {
        static string GetOutputDirectory(string[] args)
        {
            if (args.Length > 2 && !new[] { "list", "download", "all" }.Contains(args[args.Length - 1].ToLower()))
            {
                return args[args.Length - 1];
            }
            return Directory.GetCurrentDirectory();
        }

        static void WriteAlbumListToFile(string filePath, CSMEngine engine)
        {
            var albums = CSMEngine.AllAlbums.ToList();
            using (var writer = new StreamWriter(filePath, false))
            {
                foreach (var album in albums)
                {
                    writer.WriteLine(album.Name);
                }
            }
        }

        static void ListGalleries(CSMEngine engine, string outputDir)
        {
            var albums = CSMEngine.AllAlbums.ToList();
            if (albums.Count == 0)
            {
                Console.WriteLine("No galleries found.");
                return;
            }
            for (int i = 0; i < albums.Count; i++)
            {
                var album = albums[i];
                var imageCount = engine.ImageDictionary.Values.Count(img => img.AlbumTitle == album.Name);
                Console.WriteLine($"{i + 1}: gallery key: {album.UrlPath}, album name: {album.Name}, photo count: {imageCount}");
            }
            var albumListPath = Path.Combine(".\\", "albums_to_download.txt");
            WriteAlbumListToFile(albumListPath, engine);
            Console.WriteLine($"Album names written to {albumListPath}");

            Console.Write("Enter the number of the gallery to download: ");
            var input = Console.ReadLine();
            if (int.TryParse(input, out int selectedIndex) && selectedIndex >= 1 && selectedIndex <= albums.Count)
            {
                var selectedAlbum = albums[selectedIndex - 1];
                DownloadAlbum(engine, selectedAlbum, outputDir);
            }
            else
            {
                Console.WriteLine("Invalid selection.");
            }
        }

        static void DownloadAllFromList(CSMEngine engine, string outputDir)
        {
            var albumListPath = Path.Combine(".\\", "albums_to_download.txt");
            var failedAlbumsPath = Path.Combine(".\\", "failed_albums.csv");
            if (!File.Exists(albumListPath))
            {
                Console.WriteLine($"Album list file not found: {albumListPath}");
                return;
            }
            var albumNames = File.ReadAllLines(albumListPath);
            using (var failedWriter = new StreamWriter(failedAlbumsPath, true))
            {
                Int64 totalCount = 0;
                var albumCount = 0;
                foreach (var albumName in albumNames)
                {
                    var album = CSMEngine.AllAlbums.FirstOrDefault(a => a.Name.Equals(albumName.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (album == null)
                    {
                        Console.WriteLine($"Album not found: {albumName}");
                        failedWriter.WriteLine($"\"{albumName}\",\"Not found\"");
                        continue;
                    }
                    try
                    {
                        totalCount += DownloadAlbum(engine, album, outputDir);
                        albumCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading album '{album.Name}': {ex.Message}");
                        failedWriter.WriteLine($"\"{album.Name}\",\"{ex.Message}\"");
                    }
                }
                Console.WriteLine($"Total files downloaded: {totalCount} from {albumCount} albums");
            }
            Console.WriteLine($"Failed albums written to {failedAlbumsPath}");
        }

        static void DownloadSingleGallery(CSMEngine engine, string galleryName, string outputDir)
        {
            var album = CSMEngine.AllAlbums.FirstOrDefault(a => a.Name.Equals(galleryName, StringComparison.OrdinalIgnoreCase));
            if (album == null)
            {
                Console.WriteLine($"Gallery '{galleryName}' not found.");
                return;
            }
            DownloadAlbum(engine, album, outputDir);
        }

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  list galleries");
                Console.WriteLine("  download <gallery name>");
                Console.WriteLine("  download all");
                return;
            }

            var engine = new CSMEngine(false);
            var envelope = engine.getCode();
            if (!engine.login(envelope))
            {
                Console.WriteLine("Failed to login to SmugMug. Check your credentials.");
                return;
            }

            engine.IsConfigurationMode = false;
            engine.settings.quality = 5; // Original quality = 5, low = 1
            Console.WriteLine("Downloading all album names - be patient... this takes several minutes....");
            var albumLoadThread = new Thread(() => engine.GetType().GetMethod("loadAllImages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(engine, null));
            albumLoadThread.Start();
            albumLoadThread.Join();

            string outputDir = GetOutputDirectory(args);

            if (args[0] == "list" && args.Length >= 1)
            {
                ListGalleries(engine, outputDir);
            }
            else if (args[0] == "download" && args.Length > 1)
            {
                if (args[1] == "all")
                {
                    DownloadAllFromList(engine, outputDir);
                }
                else
                {
                    int galleryNameEnd = args.Length > 2 && !new[] { "list", "download", "all" }.Contains(args[args.Length - 1].ToLower()) ? args.Length - 1 : args.Length;
                    var galleryName = string.Join(" ", args.Skip(1).Take(galleryNameEnd - 1));
                    DownloadSingleGallery(engine, galleryName, outputDir);
                }
            }
            else
            {
                Console.WriteLine("Unknown command.");
            }
        }

        static Int64 DownloadAlbum(CSMEngine engine, SmugMug.NET.Album album, string outputDir)
        {
            Console.WriteLine($"Downloading gallery: {album.Name}");
            var loadImagesMethod = typeof(SMEngine.CSMEngine).GetMethod("loadImages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            loadImagesMethod.Invoke(engine, new object[] { album, true, 2 });

            var imagesLoading = true;
            var startTime = DateTime.Now;
            while (imagesLoading)
            {
                var loadedImages = engine.ImageDictionary.Values.Count(i => i.AlbumTitle == album.Name);
                if (loadedImages >= album.ImageCount)
                {
                    imagesLoading = false;
                }
                else if ((DateTime.Now - startTime).TotalMinutes > 10)
                {
                    Console.WriteLine($"Timeout waiting for images in album: {album.Name}");
                    break;
                }
                else
                {
                    Thread.Sleep(500);
                }
            }

            var images = engine.ImageDictionary.Values.Where(i => i.AlbumTitle == album.Name).ToList();
            if (images.Count == 0)
            {
                Console.WriteLine($"No images found in gallery: {album.Name}");
                return 0;                
            }

            var safeAlbumDir = album.UrlPath.Replace("/", "\\");
            if (safeAlbumDir.StartsWith("\\"))
            {
                safeAlbumDir = safeAlbumDir.Substring(1);
            }
            var fullAlbumDir = Path.Combine(outputDir, safeAlbumDir);
            Directory.CreateDirectory(fullAlbumDir);

            var captionFilePath = Path.Combine(fullAlbumDir, "captions.txt");
            var failureLogPath = Path.Combine(outputDir, "download_failures.txt");
            int downloadedCount = 0;
            using (var captionWriter = new StreamWriter(captionFilePath, false))
            using (var failureLog = new StreamWriter(failureLogPath, true))
            {
                foreach (var image in images)
                {
                    var originalUrl = image.ImageURL;
                    if (!string.IsNullOrEmpty(originalUrl))
                    {
                        var fileName = Path.Combine(fullAlbumDir, image.Name);
                        int attempts = 0;
                        bool success = false;
                        while (attempts < 3 && !success)
                        {
                            try
                            {
                                using (var client = new System.Net.WebClient())
                                {
                                    client.DownloadFile(originalUrl, fileName);
                                    Console.WriteLine($"Downloaded: {fileName}");
                                    downloadedCount++;
                                }
                                success = true;
                            }
                            catch (Exception ex)
                            {
                                attempts++;
                                Console.WriteLine($"Failed to download {originalUrl} (attempt {attempts}): {ex.Message}");
                                Thread.Sleep(1000);
                                if (attempts == 3)
                                {
                                    failureLog.WriteLine($"{album.Name}\t{image.Name}\t{originalUrl}\t{ex.Message}");
                                }
                            }
                        }
                        if (!success)
                        {
                            Console.WriteLine($"Giving up on {originalUrl} after 3 attempts.");
                        }
                        captionWriter.WriteLine($"{image.Name}\t{image.Caption}");
                    }
                }
            }
            Console.WriteLine($"Downloaded {downloadedCount} files from gallery: {album.Name}");
            return downloadedCount;
        }
    }
}
