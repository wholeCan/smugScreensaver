using System;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace andyScreenSaver.windows.Helpers
{
    internal static class ImageUtils
    {
        public static System.Windows.Media.Imaging.BitmapImage? BitmapToBitmapImage(Bitmap bitmap)
        {
            try
            {
                using (var memory = new MemoryStream())
                {
                    using (var clone = new Bitmap(bitmap))
                    {
                        clone.Save(memory, ImageFormat.Png);
                    }
                    memory.Position = 0;
                    var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = memory;
                    bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmapImage.EndInit();
                    return bitmapImage;
                }
            }
            catch
            {
                return null;
            }
        }

        public static Bitmap ScaleImage(Bitmap originalImage, int desiredHeight)
        {
            if (originalImage == null || desiredHeight <= 0) return originalImage;

            float scaleFactor = (float)desiredHeight / originalImage.Height;
            int newWidth = Math.Max(1, (int)(originalImage.Width * scaleFactor));

            var scaledImage = new Bitmap(newWidth, desiredHeight);
            using (Graphics g = Graphics.FromImage(scaledImage))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(originalImage, 0, 0, newWidth, desiredHeight);
            }
            return scaledImage;
        }

        public static Color GetAverageColor(Bitmap bitmap, Rectangle region)
        {
            int totalRed = 0;
            int totalGreen = 0;
            int totalBlue = 0;
            int totalPixels = 0;

            int bottom = Math.Min(region.Bottom, bitmap.Height);
            int right = Math.Min(region.Right, bitmap.Width);

            for (int y = Math.Max(0, region.Top); y < bottom; y++)
            {
                for (int x = Math.Max(0, region.Left); x < right; x++)
                {
                    Color pixelColor = bitmap.GetPixel(x, y);
                    totalRed += pixelColor.R;
                    totalGreen += pixelColor.G;
                    totalBlue += pixelColor.B;
                    totalPixels++;
                }
            }
            if (totalPixels <= 0)
            {
                return Color.Black;
            }
            int averageRed = totalRed / totalPixels;
            int averageGreen = totalGreen / totalPixels;
            int averageBlue = totalBlue / totalPixels;

            return Color.FromArgb(averageRed, averageGreen, averageBlue);
        }

        public static bool IsColorDark(Color color, double threshold = 0.7)
        {
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255d;
            return brightness < threshold;
        }

        public static Color GetContrastingColor(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0) return Color.White;
            int numberOfRows = 12;  // upper 12th
            int numberColumns = 3;  // left 3rd
            var region = new Rectangle(0, 0, Math.Max(1, bitmap.Width / numberColumns), Math.Max(1, bitmap.Height / numberOfRows));
            Color avg = GetAverageColor(bitmap, region);
            return IsColorDark(avg) ? Color.White : Color.Black;
        }

        public static int CalculateFontSize(int imageHeight, double percentOfHeight)
        {
            double fontSize = imageHeight * (percentOfHeight / 100.0);
            return (int)Math.Round(fontSize);
        }

        public static int GetCaptionFontSize(int imageHeight)
        {
            int configSize = 8;
            int.TryParse(ConfigurationSettings.AppSettings["captionPenSize"], out configSize);
            int minimumFontSize = configSize + 7;
            int maxFontSize = configSize + 19;
            double percentOfHeight = 3;
            int calculatedFont = CalculateFontSize(imageHeight, percentOfHeight);
            int baseValue = Math.Max(minimumFontSize, calculatedFont);
            int midValue = Math.Min(baseValue, maxFontSize);
            return midValue;
        }

        public static void AddCaption(string text, ref Bitmap referenceImage)
        {
            if (string.IsNullOrWhiteSpace(text) || referenceImage == null) return;
            var firstLocation = new System.Drawing.PointF(10f, 10f);
            try
            {
                using (var graphics = Graphics.FromImage(referenceImage))
                using (var penBrush = new SolidBrush(GetContrastingColor(referenceImage)))
                using (var arialFont = new Font("Arial", GetCaptionFontSize(referenceImage.Height)))
                {
                    graphics.DrawString(text, arialFont, penBrush, firstLocation);
                }
            }
            catch
            {
                // ignore known exceptions (e.g. indexed pixel format)
            }
        }
    }
}
