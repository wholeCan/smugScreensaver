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
                                     Func<int, InitialTileImage> initialImageProvider,
                                     Func<double> calcOverlayWidth,
                                     Func<double> calcOverlayHeight)
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

                var tile = initialImageProvider != null ? initialImageProvider(imageIndex) : default;

                var img = new indexableImage
                {
                    Source = tile.Image,
                    ImageIndex = imageIndex,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (tile.ShowCaptions && !string.IsNullOrEmpty(tile.Caption))
                {
                    var container = new Grid { ClipToBounds = true };
                    container.Children.Add(img);
                    container.Children.Add(TileRenderer.BuildOverlay(tile.Caption, calcOverlayWidth, calcOverlayHeight));
                    border.Child = container;
                }
                else
                {
                    border.Child = img;
                }

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
