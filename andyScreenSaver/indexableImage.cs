using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows;
using DrawingImage = System.Drawing.Image;

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

        // Video support
        public bool IsVideo { get; set; } = false;
        public string? VideoSource { get; set; } = null;
    }
}
