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


            if (args[0] == "list" && args.Length > 1 && args[1] == "galleries")
            {
                while (true)
                {
                    var albums = CSMEngine.AllAlbums.ToList(); // Reload albums each time
                    if (albums.Count == 0)
                    {
                        Console.WriteLine("No galleries found.");
                        break;
                    }
                    for (int i = 0; i < albums.Count; i++)
                    {
                        var album = albums[i];
                        var imageCount = engine.ImageDictionary.Values.Count(img => img.AlbumTitle == album.Name);
                        Console.WriteLine($"{i + 1}: gallery key: {album.AlbumKey}, album name: {album.Name}, photo count: {imageCount}");
                    }

                    Console.Write("Enter the number of the gallery to download (or 'quit' to exit): ");
                    var input = Console.ReadLine();
                    if (input.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
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
            // Thread.Sleep(5000);

            // Enhanced synchronous wait for all image downloads to complete
            var imagesLoading = true;
            while (imagesLoading)
            {
                // Check if all images for the album have been loaded
                var loadedImages = engine.ImageDictionary.Values.Count(i => i.AlbumTitle == album.Name);
                if (loadedImages >= album.ImageCount)
                {
                    imagesLoading = false;
                }
                else
                {
                    Thread.Sleep(500); // Poll every 0.5 seconds
                }
            }

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
                    // Try to get the highest quality image URL
                    var originalUrl = image.ImageURL; // fallback to ImageURL
                    // If there is a property or method to get original quality, use it here
                    // e.g. originalUrl = image.OriginalImageUrl ?? image.ImageURL;
                    // But currently only ImageURL is available
                    if (!string.IsNullOrEmpty(originalUrl))
                    {
                        var fileName = Path.Combine(safeAlbumName, image.Name);
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
                        // Write caption info
                        captionWriter.WriteLine($"{image.Name}\t{image.Caption}");
                    }
                }
            }
        }
    }
}
