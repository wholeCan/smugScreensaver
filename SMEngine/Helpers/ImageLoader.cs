using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using SmugMug.NET;

namespace SMEngine
{
    internal static class ImageLoader
    {
        public static string GetBestImageUrl(CSMEngine engine, ImageSizes imageSize)
        {
            switch (engine.Settings.quality)
            {
                case 0: return imageSize.TinyImageUrl;
                case 1: return imageSize.SmallImageUrl;
                case 2: return imageSize.MediumImageUrl;
                case 3: return imageSize.LargeImageUrl;
                case 4: return imageSize.X3LargeImageUrl;
                case 5: return imageSize.OriginalImageUrl;
                default: return imageSize.MediumImageUrl;
            }
        }

        public static BitmapImage DownloadImage(CSMEngine engine, string url)
        {
            var bytesToRead = 2500;
            var image = new BitmapImage();
            try
            {
                if (url != null)
                {
                    var request = WebRequest.Create(new Uri(url, UriKind.Absolute));
                    request.Timeout = -1;
                    try
                    {
                        var response = (HttpWebResponse)request.GetResponse();
                        if (response.StatusCode != HttpStatusCode.OK)
                        {
                            throw new Exception("image not returned: " + url);
                        }
                        var responseStream = response.GetResponseStream();
                        var reader = new BinaryReader(responseStream);
                        var memoryStream = new MemoryStream();
                        var bytebuffer = new byte[bytesToRead];
                        var bytesRead = reader.Read(bytebuffer, 0, bytesToRead);
                        var sw = new Stopwatch();
                        sw.Start();
                        while (bytesRead > 0)
                        {
                            memoryStream.Write(bytebuffer, 0, bytesRead);
                            bytesRead = reader.Read(bytebuffer, 0, bytesToRead);
                        }
                        sw.Stop();
                        Debug.WriteLine($"Get Image {url} took: {sw.ElapsedMilliseconds}ms.");
                        image.BeginInit();
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        image.StreamSource = memoryStream;
                        image.EndInit();
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            catch (WebException ex)
            {
                engine.doException(ex.Message);
                Debug.WriteLine(ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                engine.doException(ex.Message);
                Debug.WriteLine(ex.Message);
                Debug.WriteLine(ex.StackTrace);
                return null;
            }
            return image;
        }

        public static async Task LoadImagesForAlbum(CSMEngine engine, Album a, bool singleAlbumMode, int size = 2)
        {
            if (a == null || a.Uris.AlbumImages == null) return;
            try
            {
                Debug.WriteLine("loading album:" + a.Name);
                if (singleAlbumMode)
                {
                    lock (engine.ImageDictionary)
                    {
                        engine.ImageDictionary.Clear();
                    }
                }
                var images = await engine.Api.GetAlbumImagesWithSizes(a, engine.Debug_limit);
                if (images == null)
                {
                    engine.doException("images is null!");
                }
                Debug.WriteLine("loaded " + images.AlbumImages.Count() + " images from album " + a.Name);
                Parallel.ForEach(images.AlbumImages,
                    new ParallelOptions { MaxDegreeOfParallelism = 4 },
                    i =>
                    {
                        var imageSizes = images.ImageSizes.Where(x => x.Key.Contains(i.ImageKey));
                        var imageSize = imageSizes.First().Value.ImageSizes;
                        if (imageSize == null || i == null)
                        {
                            throw new Exception("null imagesize");
                        }
                        var imageUrl = GetBestImageUrl(engine, imageSize);
                        bool isVideo = false;
                        string videoSource = null;
                        if (!string.IsNullOrEmpty(i.FileName))
                        {
                            var ext = System.IO.Path.GetExtension(i.FileName).ToLowerInvariant();
                            if (ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".wmv" || ext == ".mkv")
                            {
                                isVideo = true;
                                if (!string.IsNullOrEmpty(imageSize.VideoUrl1920)) videoSource = imageSize.VideoUrl1920;
                                else if (!string.IsNullOrEmpty(imageSize.VideoUrl1280)) videoSource = imageSize.VideoUrl1280;
                                else if (!string.IsNullOrEmpty(imageSize.VideoUrl960)) videoSource = imageSize.VideoUrl960;
                                else if (!string.IsNullOrEmpty(imageSize.VideoUrl640)) videoSource = imageSize.VideoUrl640;
                                else if (!string.IsNullOrEmpty(imageSize.VideoUrl320)) videoSource = imageSize.VideoUrl320;
                                else if (!string.IsNullOrEmpty(imageSize.VideoUrl200)) videoSource = imageSize.VideoUrl200;
                                else if (!string.IsNullOrEmpty(imageSize.VideoUrl110)) videoSource = imageSize.VideoUrl110;
                                else { isVideo = false; videoSource = imageUrl; }
                            }
                        }
                        if (imageSizes != null && i.ImageKey != null)
                        {
                            lock (engine.ImageDictionary)
                            {
                                try
                                {
                                    if (!engine.ImageDictionary.ContainsKey(i.ImageKey))
                                    {
                                        var imgSet = new CSMEngine.ImageSet(
                                            imageUrl,
                                            string.IsNullOrEmpty(i.Caption) ? "" : i.Caption,
                                            string.IsNullOrEmpty(i.FileName) ? "" : i.FileName,
                                            i.Date == null ? DateTime.Now : i.Date,
                                            string.IsNullOrEmpty(engine.getFolder(a)) ? "" : engine.getFolder(a),
                                            string.IsNullOrEmpty(a.Name) ? "" : a.Name
                                        );
                                        imgSet.IsVideo = isVideo;
                                        imgSet.VideoSource = videoSource;
                                        engine.ImageDictionary.Add(i.ImageKey, imgSet);
                                    }
                                    else
                                    {
                                        Debug.WriteLine("duplicate image: " + i.ImageKey);
                                    }
                                }
                                catch (ArgumentException ex)
                                {
                                    engine.doException("duplicate image: " + i.FileName + " : " + ex.Message);
                                }
                                catch (Exception ex)
                                {
                                    engine.doException(ex.Message);
                                }
                            }
                        }
                    });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
    }
}
