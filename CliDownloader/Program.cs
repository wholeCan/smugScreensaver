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

            // Wait for albums to load
            engine.IsConfigurationMode = false;          
            engine.settings.quality = 5; // Set to highest quality

            var albumLoadThread = new Thread(() => engine.GetType().GetMethod("loadAllImages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(engine, null));
            albumLoadThread.Start();
            albumLoadThread.Join();
//            while (engine.IsLoadingAlbums1) { Thread.Sleep(100); }


            if (args[0] == "list" && args.Length >= 1)
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

                Console.Write("Enter the number of the gallery to download: ");
                var input = Console.ReadLine();
                if (int.TryParse(input, out int selectedIndex) && selectedIndex >= 1 && selectedIndex <= albums.Count)
                {
                    var selectedAlbum = albums[selectedIndex - 1];
                    DownloadAlbum(engine, selectedAlbum);
                }
                else
                {
                    Console.WriteLine("Invalid selection.");
                }
            }
            else if (args[0] == "download" && args.Length > 1)
            {
                if (args[1] == "all")
                {
                    foreach (var album in CSMEngine.AllAlbums)
                    {
                        DownloadAlbum(engine, album);
                    }
                }
                else
                {
                    var galleryName = string.Join(" ", args.Skip(1));
                    var album = CSMEngine.AllAlbums.FirstOrDefault(a => a.Name.Equals(galleryName, StringComparison.OrdinalIgnoreCase));
                    if (album == null)
                    {
                        Console.WriteLine($"Gallery '{galleryName}' not found.");
                        return;
                    }
                    DownloadAlbum(engine, album);
                }
            }
            else
            {
                Console.WriteLine("Unknown command.");
            }
        }

        static void DownloadAlbum(CSMEngine engine, SmugMug.NET.Album album)
        {
            Console.WriteLine($"Downloading gallery: {album.Name}");
            var loadImagesMethod = typeof(SMEngine.CSMEngine).GetMethod("loadImages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            loadImagesMethod.Invoke(engine, new object[] { album, true, 2 });

            var imagesLoading = true;
            while (imagesLoading)
            {
                var loadedImages = engine.ImageDictionary.Values.Count(i => i.AlbumTitle == album.Name);
                if (loadedImages >= album.ImageCount)
                {
                    imagesLoading = false;
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
                return;
            }

            // Use album.UrlPath to create a subdirectory for the album
            var safeAlbumDir = album.UrlPath.Replace("/","\\");
            if (safeAlbumDir.StartsWith("\\"))
            {
                safeAlbumDir = safeAlbumDir.Substring(1);
            }
   
            Directory.CreateDirectory(safeAlbumDir);

            var captionFilePath = Path.Combine(safeAlbumDir, "captions.txt");
            using (var captionWriter = new StreamWriter(captionFilePath, false))
            {
                foreach (var image in images)
                {
                    var originalUrl = image.ImageURL;
                    if (!string.IsNullOrEmpty(originalUrl))
                    {
                        var fileName = Path.Combine(safeAlbumDir, image.Name);
                        try
                        {
                            using (var client = new System.Net.WebClient())
                            {
                                client.DownloadFile(originalUrl, fileName);
                                Console.WriteLine($"Downloaded: {fileName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to download {originalUrl}: {ex.Message}");
                        }
                        captionWriter.WriteLine($"{image.Name}\t{image.Caption}");
                    }
                }
            }
        }
    }
}
