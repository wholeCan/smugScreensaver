using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace andyScreenSaver
{
    public class rotatableImage : System.Windows.Controls.Image
    {
        private SMEngine.CSMEngine _engine = null;
        private int _imageIndex = 0;
        private int _imageCounter = 0;
        public int ImageIndex
        {
            get { return _imageIndex; }
            set { _imageIndex = value; }
        }

        public RotateTransform AnimatedRotateTransform = new RotateTransform();
        public rotatableImage(SMEngine.CSMEngine engine)
        {
            _engine = engine;
            AnimatedRotateTransform.Angle = 0;

        }

        private BitmapImage Bitmap2BitmapImage(System.Drawing.Bitmap bitmap)
        {
            BitmapImage bitmapImage = null;
            try
            {
                var memory = new MemoryStream();
                var b = new Bitmap(bitmap);
                b.Save(memory, ImageFormat.Png);
                memory.Position = 0;
                memory.Seek(0, SeekOrigin.Begin);
                bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                memory.Close();

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return bitmapImage;
        }
        private void updateImage()
        {

            var storageDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures) + @"\SmugAndy\";
            var fileNameStorage = storageDirectory + @"\" + _imageIndex + @".jpg"; // used for file storage.

            //experimantal, not in use yet.  in idea, we add event to 'click' and load new image.
            var gridWidth = 5;  // 2->4x4, 3 => 8x8, 6 => 64 x 64
            var gridHeight = 4;
            var borderWidth = 5;
            var lastUpdate = DateTime.Now;

            var lm = new listManager(gridWidth * gridHeight);

            var s = new SMEngine.CSMEngine.ImageSet();
            this.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate ()
                {

                    var bmyImage = s.B;

                    if (bmyImage != null)
                    {
                        try
                        {
                            var maxTotalCells = Convert.ToInt32(gridWidth) * Convert.ToInt32(gridHeight);
                            var beginIndex = 0;

                            var rIndex = new Random().Next(beginIndex, maxTotalCells);
                            var randWidth = rIndex % gridWidth;
                            var randHeight = rIndex / gridWidth;
                            while (lm.isInList(new Tuple<int, int>(randWidth, randHeight)))
                            {
                                rIndex = new Random().Next(beginIndex, maxTotalCells);
                                randWidth = rIndex % gridWidth;
                                randHeight = rIndex / gridWidth;

                            }

                            s = _engine.getImage();
                            Bitmap bmyImage2;
                            if (s.B != null)
                            {
                                bmyImage2 = s.B;
                                var image = this;
                                var myHeight = Height;
                                image.Height = /*this.Height*/ myHeight / gridHeight - (borderWidth / gridHeight); //161; 

                                (image as System.Windows.Controls.Image).Source = Bitmap2BitmapImage(bmyImage2);
                                if (_imageCounter == 0)
                                {
                                    //store off the first image.
                                    try
                                    {
                                        bmyImage2.Save(fileNameStorage);//untested addition as of 4/23/2015.
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine("Could not save img:  " + ex.Message);
                                    }
                                }
                                _imageCounter++;
                                lm.addToList(new Tuple<int, int>(randWidth, randHeight));
                            }

                            var h = bmyImage.Height;
                            var w = bmyImage.Width;
                            var aspect = h / w;
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("unknown failure");
                        }
                        if (_engine.settings.showInfo)
                        {
                            throw new Exception("SEE BELOW");
                        }


                        //int bufHeight = 0;//was 40
                        //we are a vertical picture.int bufHeight = 40;
                        //image1.Height = tabControl.Height - bufHeight;
                        lastUpdate = DateTime.Now;
                    }

                }));

        }

        public void resetRotation()
        {
            this.Dispatcher.BeginInvoke(new Action(delegate ()
               {
                   try
                   {
                       AnimatedRotateTransform.Angle = 0;
                   }
                   catch (Exception ex)
                   {
                       System.Diagnostics.Debug.WriteLine(ex.Message);
                   }
               }));
        }

        private void rotImage()
        {
            if (true)//not ready yet.
            {
                this.Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    try
                    {
                        AnimatedRotateTransform.Angle += 1;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ex.Message);
                    }
                }));


            }
        }
    }
}
