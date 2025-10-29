namespace SMEngine
{
    public class CSettings
    {
        public bool load_all;
        public int quality;
        public int speed_s;
        public bool showImageCaptions;
        public int gridWidth;
        public int gridHeight;
        public int borderThickness;
        public int startTime = 8;
        public int stopTime = 23;
        public bool allowVideoToFinish = true;
        public CSettings()
        {
            quality = 2;
            speed_s = 6;
            load_all = false;
            showImageCaptions = true;
            gridWidth = 5;
            gridHeight = 4;
            borderThickness = 0;
            startTime = 8;
            stopTime = 23;
            allowVideoToFinish = true;
        }
    }

}
