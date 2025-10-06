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

            var albumLoadThread = new Thread(() => engine.GetType().GetMethod("loadAllImages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(engine, null));
            albumLoadThread.Start();
            albumLoadThread.Join();
//            while (engine.IsLoadingAlbums1) { Thread.Sleep(100); }


            if (args[0] == "list" && args.Length > 1 && args[1] == "galleries")
            {
                foreach (var album in CSMEngine.AllAlbums)
                {
                    
                    var imageCount = engine.ImageDictionary.Values.Count(i => i.AlbumTitle == album.Name);
                    Console.WriteLine($"gallery name: {album.AlbumKey}, album name: {album.Name}, photo count: {imageCount}");

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
            // Wait for images to load (simple sleep, could be improved)
            Thread.Sleep(5000);
            var images = engine.ImageDictionary.Values.Where(i => i.AlbumTitle == album.Name).ToList();
            if (images.Count == 0)
            {
                Console.WriteLine($"No images found in gallery: {album.Name}");
                return;
            }
            var safeAlbumName = string.Join("_", album.Name.Split(Path.GetInvalidFileNameChars()));
            Directory.CreateDirectory(safeAlbumName);

            // Prepare caption file
            var captionFilePath = Path.Combine(safeAlbumName, "captions.txt");
            using (var captionWriter = new StreamWriter(captionFilePath, false))
            {
                foreach (var image in images)
                {
                    if (!string.IsNullOrEmpty(image.ImageURL))
                    {
                        var fileName = Path.Combine(safeAlbumName, image.Name);
                        try
                        {
                            using (var client = new System.Net.WebClient())
                            {
                                client.DownloadFile(image.ImageURL, fileName);
                                Console.WriteLine($"Downloaded: {fileName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to download {image.ImageURL}: {ex.Message}");
                        }
                        // Write caption info
                        captionWriter.WriteLine($"{image.Name}\t{image.Caption}");
                    }
                }
            }
        }
    }
}

































































