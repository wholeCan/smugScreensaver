using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace andyScreenSaver.windows.Helpers
{
    internal static class TileGridBuilder
    {
        public static void BuildGrid(UniformGrid grid,
                                     int gridWidth,
                                     int gridHeight,
                                     int borderThickness,
                                     Func<int, BitmapImage> initialImageProvider)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            grid.Rows = gridHeight;
            grid.Columns = gridWidth;
            grid.Children.Clear();

            int totalCells = gridWidth * gridHeight;
            for (int imageIndex = 0; imageIndex < totalCells; imageIndex++)
            {
                var border = new Border
                {
                    BorderThickness = new Thickness(borderThickness)
                };

                var img = new indexableImage
                {
                    Source = initialImageProvider?.Invoke(imageIndex),
                    ImageIndex = imageIndex,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                border.Child = img;
                grid.Children.Add(border);
            }
        }

        public static Border GetBorderAt(UniformGrid grid, int x, int y)
        {
            // UniformGrid stores children in row-major order
            int index = (y * grid.Columns) + x;
            return grid.Children[index] as Border;
        }

        public static void SetImageHeights(UniformGrid grid, double height)
        {
            foreach (var child in grid.Children)
            {
                if (child is Border border && border.Child is indexableImage img)
                {
                    img.Height = height;
                }
            }
        }
    }
}
