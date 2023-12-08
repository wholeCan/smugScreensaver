using System;
using System.Drawing;
using System.Windows.Media.Imaging;


namespace SMEngine
{

    public partial class CSMEngine
    {
        bool isScreensaver = false;

        public string getUptime()
        {
            return (DateTime.Now - timeStarted).TotalHours.ToString("0.00");
        }

        public void IsScreensaver(bool screensaverModeDisabled)
        {
            isScreensaver = !screensaverModeDisabled;
        }

        public class ImageSet
        {
            private System.Drawing.Bitmap bitmap;
            private BitmapImage bitmapImage;
            private string caption;
            private string albumTitle;
            private string category;
            private string exif;
            private DateTime myDate;
            private string name;
            private string imageURL;

            public Bitmap Bitmap { get => bitmap; set => bitmap = value; }
            public BitmapImage BitmapImage { get => bitmapImage; set => bitmapImage = value; }
            public string Caption { get => caption; set => caption = value; }
            public string AlbumTitle { get => albumTitle; set => albumTitle = value; }
            public string Category { get => category; set => category = value; }
            public string Exif { get => exif; set => exif = value; }
            public DateTime MyDate { get => myDate; set => myDate = value; }
            public string Name { get => name; set => name = value; }
            public string ImageURL { get => imageURL; set => imageURL = value; }

            public ImageSet(string mediumUrl, string Caption, string name, DateTime mydate, string folder, string albumname)
            {
                ImageURL = mediumUrl;
                this.Caption = Caption;
                Name = name;
                MyDate = mydate;
                Category = folder;
                AlbumTitle = albumname;

            }
            public ImageSet()
            {
                Caption = "";
                Exif = "";
                BitmapImage = null;
            }
        }
    }

}
