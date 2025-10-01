using System;

namespace andyScreenSaver.windows.Helpers
{
    internal sealed class LayoutHelper
    {
        private readonly Func<int> _getWidth;
        private readonly Func<int> _getHeight;
        private readonly Func<int> _getGridWidth;
        private readonly Func<int> _getGridHeight;
        private readonly Func<int> _getBorderWidth;

        public LayoutHelper(Func<int> getWidth,
                            Func<int> getHeight,
                            Func<int> getGridWidth,
                            Func<int> getGridHeight,
                            Func<int> getBorderWidth)
        {
            _getWidth = getWidth;
            _getHeight = getHeight;
            _getGridWidth = getGridWidth;
            _getGridHeight = getGridHeight;
            _getBorderWidth = getBorderWidth;
        }

        public double CalculateImageHeight()
        {
            var h = Math.Max(1, _getHeight());
            var gh = Math.Max(1, _getGridHeight());
            return h / (double)gh - (100 / Math.Pow(2, gh));
        }

        public double CalculateImageWidth()
        {
            var w = Math.Max(1, _getWidth());
            var gw = Math.Max(1, _getGridWidth());
            return w / (double)gw - (100 / Math.Pow(2, gw));
        }

        public int GetImageIndex(int x, int y)
        {
            return x + (y * Math.Max(1, _getGridWidth()));
        }
    }
}
