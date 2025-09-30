using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace andyScreenSaver.windows.Helpers
{
    internal sealed class TileRenderer
    {
        private readonly Func<double> _calcWidth;
        private readonly Func<double> _calcHeight;
        private readonly Action<string> _log;

        public TileRenderer(Func<double> calculateWidth,
                            Func<double> calculateHeight,
                            Action<string> log)
        {
            _calcWidth = calculateWidth ?? throw new ArgumentNullException(nameof(calculateWidth));
            _calcHeight = calculateHeight ?? throw new ArgumentNullException(nameof(calculateHeight));
            _log = log ?? (_ => { });
        }

        private static Border BuildOverlay(string text, Func<double> calcWidth, Func<double> calcHeight)
        {
            var overlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                Padding = new Thickness(6, 2, 6, 2),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Tag = "CaptionOverlay",
                IsHitTestVisible = false
            };
            Panel.SetZIndex(overlay, int.MaxValue);
            var tb = new TextBlock
            {
                Text = text ?? string.Empty,
                Foreground = Brushes.White,
                FontSize = Math.Max(10, calcHeight() * 0.045),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = calcWidth() * 0.9
            };
            overlay.Child = tb;
            return overlay;
        }

        private static void RemoveExistingOverlay(Grid container)
        {
            if (container == null) return;
            var overlays = container.Children.OfType<Border>().Where(b => (b.Tag as string) == "CaptionOverlay").ToList();
            foreach (var o in overlays)
            {
                container.Children.Remove(o);
            }
        }

        // NEW: add/remove overlay at runtime without touching the video
        public void UpdateOverlay(Border border, bool show, string overlayText)
        {
            if (border == null) return;
            border.Dispatcher.Invoke(() =>
            {
                if (border.Child is Grid container)
                {
                    // Remove current overlay(s)
                    RemoveExistingOverlay(container);
                    if (show && !string.IsNullOrEmpty(overlayText))
                    {
                        container.Children.Add(BuildOverlay(overlayText, _calcWidth, _calcHeight));
                    }
                }
            });
        }

        public void RenderSync(Border border, indexableImage image, SMEngine.CSMEngine.ImageSet s, string overlayText)
        {
            if (border == null || s == null) return;

            border.Dispatcher.Invoke(() =>
            {
                var container = new Grid { ClipToBounds = true };

                if (s.IsVideo && !string.IsNullOrEmpty(s.VideoSource))
                {
                    // Stop and dispose any previous video in either direct child or container
                    if (border.Child is VideoView oldVv && oldVv.MediaPlayer != null)
                    {
                        try { oldVv.MediaPlayer.Stop(); } catch { }
                        try { oldVv.MediaPlayer.Dispose(); } catch { }
                        try { oldVv.Dispose(); } catch { }
                        border.Child = null;
                    }
                    else if (border.Child is Grid g)
                    {
                        foreach (var child in g.Children)
                        {
                            if (child is VideoView vv && vv.MediaPlayer != null)
                            {
                                try { vv.MediaPlayer.Stop(); } catch { }
                                try { vv.MediaPlayer.Dispose(); } catch { }
                                try { vv.Dispose(); } catch { }
                            }
                        }
                        border.Child = null;
                    }

                    var libVLC = new LibVLC();
                    var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(libVLC) { Mute = true };
                    var videoView = new VideoView
                    {
                        MediaPlayer = mediaPlayer,
                        Width = Math.Min(_calcWidth(), border.ActualWidth > 0 ? border.ActualWidth : _calcWidth()),
                        Height = Math.Min(border.ActualHeight > 0 ? border.ActualHeight : _calcHeight(), _calcHeight()),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    // Toggle mute on click
                    videoView.MouseLeftButtonUp += (s2, e2) =>
                    {
                        try
                        {
                            if (videoView.MediaPlayer != null)
                            {
                                videoView.MediaPlayer.Mute = !videoView.MediaPlayer.Mute;
                                _log($"video mute toggled to {videoView.MediaPlayer.Mute}");
                            }
                        }
                        catch { }
                        e2.Handled = true;
                    };
                    mediaPlayer.Play(new Media(libVLC, s.VideoSource, FromType.FromLocation));
                    mediaPlayer.EncounteredError += (sender, e) =>
                    {
                        _log($"Error encountered playing video {s.VideoSource} {e.ToString()}");
                        try { mediaPlayer.Stop(); } catch { }
                    };
                    Panel.SetZIndex(videoView, 0);
                    container.Children.Add(videoView);
                }
                else
                {
                    // Ensure an image exists
                    indexableImage targetImg = null;
                    if (border.Child is indexableImage existingImg)
                    {
                        targetImg = existingImg;
                    }
                    else
                    {
                        targetImg = image ?? new indexableImage();
                    }
                    targetImg.MaxHeight = _calcHeight();
                    targetImg.Width = _calcWidth();
                    Panel.SetZIndex(targetImg, 0);
                    container.Children.Add(targetImg);
                }

                // Re-add overlay last so it always stays on top
                RemoveExistingOverlay(container);
                if (!string.IsNullOrEmpty(overlayText))
                {
                    container.Children.Add(BuildOverlay(overlayText, _calcWidth, _calcHeight));
                }

                border.Child = container;
            });
        }

        public async Task RenderAsync(Border border, indexableImage image, SMEngine.CSMEngine.ImageSet s)
        {
            if (border == null || s == null) return;

            if (s.IsVideo && !string.IsNullOrEmpty(s.VideoSource))
            {
                await border.Dispatcher.InvokeAsync(() =>
                {
                    // If a video is already playing and allowed to finish, skip
                    if (border.Child is VideoView existingVv && existingVv.MediaPlayer != null && existingVv.MediaPlayer.IsPlaying)
                    {
                        _log($"skipping video, still playing");
                        return;
                    }
                    // If a container with an already playing VideoView exists, skip to preserve overlay
                    if (border.Child is Grid container && container.Children.Count > 0)
                    {
                        foreach (var child in container.Children)
                        {
                            if (child is VideoView vv && vv.MediaPlayer != null && vv.MediaPlayer.IsPlaying)
                            {
                                _log($"skipping video in grid, still playing");
                                return;
                            }
                        }
                    }

                    // If an old video exists, stop and dispose (direct)
                    if (border.Child is VideoView oldVv && oldVv.MediaPlayer != null)
                    {
                        try { oldVv.MediaPlayer.Stop(); } catch { }
                        try { oldVv.MediaPlayer.Dispose(); } catch { }
                        try { oldVv.Dispose(); } catch { }
                        border.Child = null;
                    }
                    // Or if an old video is inside a container, stop and dispose
                    else if (border.Child is Grid g)
                    {
                        foreach (var child in g.Children)
                        {
                            if (child is VideoView vv && vv.MediaPlayer != null)
                            {
                                try { vv.MediaPlayer.Stop(); } catch { }
                                try { vv.MediaPlayer.Dispose(); } catch { }
                                try { vv.Dispose(); } catch { }
                            }
                        }
                        border.Child = null;
                    }

                    // Create and play new video (no overlay in async path)
                    var libVLC = new LibVLC();
                    var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(libVLC)
                    {
                        Mute = true
                    };
                    var videoView = new VideoView
                    {
                        MediaPlayer = mediaPlayer,
                        Width = Math.Min(_calcWidth(), border.ActualWidth > 0 ? border.ActualWidth : _calcWidth()),
                        Height = Math.Min(border.ActualHeight > 0 ? border.ActualHeight : _calcHeight(), _calcHeight()),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    // Toggle mute on click
                    videoView.MouseLeftButtonUp += (s2, e2) =>
                    {
                        try
                        {
                            if (videoView.MediaPlayer != null)
                            {
                                videoView.MediaPlayer.Mute = !videoView.MediaPlayer.Mute;
                                _log($"video mute toggled to {videoView.MediaPlayer.Mute}");
                            }
                        }
                        catch { }
                        e2.Handled = true;
                    };
                    mediaPlayer.Play(new Media(libVLC, s.VideoSource, FromType.FromLocation));
                    mediaPlayer.EncounteredError += (sender, e) =>
                    {
                        _log($"Error encountered playing video {s.VideoSource} {e.ToString()}");
                        try { mediaPlayer.Stop(); } catch { }
                    };
                    border.Child = videoView;
                });
                return;
            }

            // Render photo: ensure the child is an indexableImage
            await border.Dispatcher.InvokeAsync(() =>
            {
                indexableImage targetImg = image;

                if (border.Child is VideoView oldVv && oldVv.MediaPlayer != null)
                {
                    try { oldVv.MediaPlayer.Stop(); } catch { }
                    try { oldVv.MediaPlayer.Dispose(); } catch { }
                    try { oldVv.Dispose(); } catch { }
                    border.Child = null;
                    targetImg = new indexableImage();
                    border.Child = targetImg;
                }
                else if (border.Child is Grid gridWithVideo && gridWithVideo.Children.Count > 0 && gridWithVideo.Children[0] is VideoView)
                {
                    foreach (var child in gridWithVideo.Children)
                    {
                        if (child is VideoView vv && vv.MediaPlayer != null)
                        {
                            try { vv.MediaPlayer.Stop(); } catch { }
                            try { vv.MediaPlayer.Dispose(); } catch { }
                            try { vv.Dispose(); } catch { }
                        }
                    }
                    border.Child = null;
                    targetImg = new indexableImage();
                    border.Child = targetImg;
                }
                else if (border.Child is indexableImage existingImg)
                {
                    targetImg = existingImg;
                }
                else if (border.Child == null)
                {
                    targetImg = new indexableImage();
                    border.Child = targetImg;
                }

                // Size and assign image
                targetImg.MaxHeight = _calcHeight();
                targetImg.Width = _calcWidth();
                targetImg.Source = ImageUtils.BitmapToBitmapImage(s.Bitmap);
            });
        }
    }
}
