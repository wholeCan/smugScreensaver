namespace SMEngine
{
    public class CSettings
    {
        public bool load_all;
        public int quality;
        public int speed_s;
        public bool showInfo;
        public int gridWidth;
        public int gridHeight;
        public int borderThickness;
        public CSettings()
        {
            quality = 2;
            speed_s = 6;
            load_all = false;
            showInfo = true;
            gridWidth = 5;
            gridHeight = 4;
            borderThickness = 0;
        }
    }

}
