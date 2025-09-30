using System;

namespace andyScreenSaver.windows.Helpers
{
    internal sealed class TilePlacementService
    {
        private readonly listManager _lm;
        private readonly Func<int> _getGridWidth;
        private readonly Func<int> _getGridHeight;
        private readonly Random _random = new Random();

        public TilePlacementService(listManager lm, Func<int> getGridWidth, Func<int> getGridHeight)
        {
            _lm = lm ?? throw new ArgumentNullException(nameof(lm));
            _getGridWidth = getGridWidth ?? throw new ArgumentNullException(nameof(getGridWidth));
            _getGridHeight = getGridHeight ?? throw new ArgumentNullException(nameof(getGridHeight));
        }

        public Tuple<int, int> PickNextCell()
        {
            int gw = Math.Max(1, _getGridWidth());
            int gh = Math.Max(1, _getGridHeight());
            int maxTotalCells = gw * gh;
            int attempts = 0;
            while (attempts < Math.Max(10, maxTotalCells))
            {
                int idx = _random.Next(0, maxTotalCells);
                int x = idx % gw;
                int y = idx / gw;
                var t = new Tuple<int, int>(x, y);
                if (!_lm.isInList(t))
                {
                    return t;
                }
                attempts++;
            }
            // Fallback to linear search if we were unlucky
            for (int i = 0; i < maxTotalCells; i++)
            {
                int x = i % gw;
                int y = i / gw;
                var t = new Tuple<int, int>(x, y);
                if (!_lm.isInList(t))
                {
                    return t;
                }
            }
            // If all cells considered used, just return (0,0) to avoid crash; caller can handle reuse
            return new Tuple<int, int>(0, 0);
        }

        public void MarkPlaced(int x, int y)
        {
            _lm.addToList(new Tuple<int, int>(x, y));
        }
    }
}
