using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace andyScreenSaver
{
    //this used to be a rotatableImage, but refactored for simplicity after I stopped being silly.
    public class indexableImage : System.Windows.Controls.Image
    {
        private int _imageIndex = 0;
        public int ImageIndex
        {
            get { return _imageIndex; }
            set { _imageIndex = value; }
        }
    }

}
