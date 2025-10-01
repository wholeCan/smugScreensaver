using System;
using System.Linq;
using System.Threading.Tasks;

namespace SMEngine
{
    internal static class ImageSelectionHelper
    {
        // Centralized random image selection + hydration logic
        public static CSMEngine.ImageSet TryGetRandomImage(CSMEngine engine)
        {
            engine.checkLogin(engine.Envelope);
            var imageSet = new CSMEngine.ImageSet();
            lock (engine.ImageDictionary)
            {
                if (engine.ImageDictionary.Count > 0)
                {
                    try
                    {
                        var imageIndex = CSMEngine.R.Next(engine.ImageDictionary.Count);
                        var key = engine.ImageDictionary.Keys.ElementAt(imageIndex);
                        var element = engine.ImageDictionary[key];
                        engine.ImageDictionary.Remove(key);
                        if (!engine.PlayedImages.ContainsKey(key))
                        {
                            engine.PlayedImages.Add(key, element);
                        }
                        var image = ImageLoader.DownloadImage(engine, element.ImageURL);
                        if (image == null)
                        {
                            throw new Exception("image returned is null: " + element.ImageURL);
                        }
                        imageSet.BitmapImage = image;
                        imageSet.Name = element.Name;
                        imageSet.AlbumTitle = element.AlbumTitle;
                        imageSet.ImageURL = element.ImageURL;
                        imageSet.Category = element.Category;
                        imageSet.MyDate = element.MyDate;
                        imageSet.AlbumTitle = element.AlbumTitle;
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
            return imageSet;
        }
    }
}
