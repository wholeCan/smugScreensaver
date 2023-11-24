// See https://aka.ms/new-console-template for more information



using System.Windows.Media.Imaging;
using static SMEngine.CSMEngine;

var engine = new SMEngine.CSMEngine(true);
var loggedIn = true;

for (int i = 0; i < 4; i++)
{
    ImageSet image = null;
    while (image == null)
    {
        image = engine.getImage();
        Thread.Sleep(100);
    }
    Console.WriteLine("Logged in: {0} album: {1} category {2}", loggedIn.ToString(), image.AlbumTitle, image.CAtegory);

    var name = image.Name != null ? image.Name : "unknown";
    var outStream = new FileStream("outputimage" + name + ".bmp", FileMode.Create);

    if (image.Bm != null)
    {
        var enc = new BmpBitmapEncoder();
        var bitmapImage = image.Bm;
        enc.Frames.Add(BitmapFrame.Create(bitmapImage));

        enc.Save(outStream);
        var bitmap = new System.Drawing.Bitmap(outStream);

        outStream.Close();
    }
    else
    {
        Console.WriteLine("empty image!");
    }

}
Console.WriteLine("Hello, World!");
