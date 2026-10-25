using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SMEngine
{
    internal static class ImageSelectionHelper
    {
        // Centralized random image selection + hydration logic
        public static CSMEngine.ImageSet TryGetRandomImage(CSMEngine engine, CancellationToken cancellationToken = default)
        {
            engine.checkLogin(engine.Envelope);
            var imageSet = new CSMEngine.ImageSet();
            CSMEngine.ImageSet element = null;
            // Only pick/remove the entry under the lock. The download below is slow network I/O
            // and must not hold the lock, or anything else needing it (e.g. the stats overlay on
            // the UI thread) freezes until the download completes.
            lock (engine.ImageDictionary)
            {
                if (engine.ImageDictionary.Count > 0)
                {
                    try
                    {
                        // Use cryptographically secure random for better randomness
                        int imageIndex;
                        using (var rng = RandomNumberGenerator.Create())
                        {
                            var randomBytes = new byte[4];
                            rng.GetBytes(randomBytes);
                            var randomInt = BitConverter.ToUInt32(randomBytes, 0);
                            imageIndex = (int)(randomInt % (uint)engine.ImageDictionary.Count);
                        }
                        var key = engine.ImageDictionary.Keys.ElementAt(imageIndex);
                        element = engine.ImageDictionary[key];
                        engine.ImageDictionary.Remove(key);
                        if (!engine.PlayedImages.ContainsKey(key))
                        {
                            engine.PlayedImages.Add(key, element);
                        }
                    }
                    catch (Exception ex)
                    {
                        engine.doException(ex.Message);
                        element = null;
                    }
                }
                else if ((engine.PlayedImages.Count > 0) && !engine.IsLoadingAlbums1)
                {
                    Task.Factory.StartNew(() =>
                    {
                        engine.RePullAlbumsSafe();
                    });
                    return null;
                }
                else { return null; }
            }

            if (element != null)
            {
                try
                {
                    if (!element.IsVideo)
                    {
                        var image = ImageLoader.DownloadImage(engine, element.ImageURL, cancellationToken);
                        if (image == null)
                        {
                            engine.RegisterCacheImageFailure();
                            throw new Exception("image returned is null: " + element.ImageURL);
                        }
                        imageSet.BitmapImage = image;
                    }
                    imageSet.Name = element.Name;
                    imageSet.AlbumTitle = element.AlbumTitle;
                    imageSet.ImageURL = element.ImageURL;
                    imageSet.Category = element.Category;
                    imageSet.MyDate = element.MyDate;
                    imageSet.Caption = element.Caption;
                    imageSet.Exif = element.Exif;
                    imageSet.IsVideo = element.IsVideo;
                    imageSet.VideoSource = element.VideoSource;
                }
                catch (Exception ex)
                {
                    engine.doException(ex.Message);
                }
            }
            return imageSet;
        }
    }
}
