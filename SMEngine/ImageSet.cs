using System;
using System.Drawing;
using System.Windows.Media.Imaging;


namespace SMEngine
{

    public partial class CSMEngine
    {
        bool isScreensaver = false;
        public void IsScreensaver(bool screensaverModeDisabled)
        {
            isScreensaver = !screensaverModeDisabled;
        }

        public class ImageSet
        {
            private System.Drawing.Bitmap b;
            private BitmapImage bm;
            private string caption;
            private string albumTitle;
            private string cAtegory;
            private string exif;
            private DateTime myDate;
            private string name;
            private string imageURL;

            public Bitmap B { get => b; set => b = value; }
            public BitmapImage Bm { get => bm; set => bm = value; }
            public string Caption { get => caption; set => caption = value; }
            public string AlbumTitle { get => albumTitle; set => albumTitle = value; }
            public string CAtegory { get => cAtegory; set => cAtegory = value; }
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
                CAtegory = folder;
                AlbumTitle = albumname;

            }
            public ImageSet()
            {
                Caption = "";
                Exif = "";
            }
        }
    }

}
