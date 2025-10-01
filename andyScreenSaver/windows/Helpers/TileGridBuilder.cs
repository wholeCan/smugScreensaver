using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace andyScreenSaver.windows.Helpers
{
    internal static class TileGridBuilder
    {
        public static void BuildGrid(StackPanel rootHorizontalStack,
                                     int gridWidth,
                                     int gridHeight,
                                     int borderThickness,
                                     Func<int, BitmapImage> initialImageProvider,
                                     double initialTileHeight)
        {
            if (rootHorizontalStack == null) throw new ArgumentNullException(nameof(rootHorizontalStack));
            rootHorizontalStack.Children.Clear();

            int imageIndex = 0;
            for (int col = 0; col < gridWidth; col++)
            {
                var columnStack = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Orientation = Orientation.Vertical
                };

                for (int row = 0; row < gridHeight; row++)
                {
                    var border = new Border
                    {
                        BorderThickness = new Thickness(borderThickness)
                    };

                    var img = new indexableImage
                    {
                        Source = initialImageProvider?.Invoke(imageIndex),
                        ImageIndex = imageIndex,
                        Height = initialTileHeight,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    border.Child = img;
                    columnStack.Children.Add(border);
                    imageIndex++;
                }

                rootHorizontalStack.Children.Add(columnStack);
            }
        }

        public static Border GetBorderAt(StackPanel rootHorizontalStack, int x, int y)
        {
            var column = rootHorizontalStack.Children[x] as StackPanel;
            return column.Children[y] as Border;
        }

        public static void SetImageHeights(StackPanel rootHorizontalStack, double height)
        {
            foreach (var v in rootHorizontalStack.Children)
            {
                if (v is StackPanel col)
                {
                    foreach (var child in col.Children)
                    {
                        if (child is Border border && border.Child is indexableImage img)
                        {
                            img.Height = height;
                        }
                    }
                }
            }
        }
    }
}
