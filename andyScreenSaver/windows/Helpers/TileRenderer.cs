using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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
                VerticalAlignment = VerticalAlignment.Bottom,
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

        private static Border BuildAudioIndicator()
        {
            var indicator = new Border
            {
                BorderBrush = Brushes.LimeGreen,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(2),
                Tag = "AudioIndicator",
                IsHitTestVisible = false
            };
            Panel.SetZIndex(indicator, int.MaxValue - 1);
            return indicator;
        }

        private static Grid BuildOverlayHostForVideo(VideoView vv)
        {
            var host = new Grid
            {
                Width = vv.Width,
                Height = vv.Height,
                HorizontalAlignment = vv.HorizontalAlignment,
                VerticalAlignment = vv.VerticalAlignment,
                IsHitTestVisible = false,
                Tag = "CaptionOverlayHost"
            };
            Panel.SetZIndex(host, int.MaxValue);
            return host;
        }

        private static void RemoveExistingOverlay(Grid container)
        {
            if (container == null) return;
            var overlays = container.Children.OfType<FrameworkElement>()
                .Where(b => (b.Tag as string) == "CaptionOverlay" || (b.Tag as string) == "CaptionOverlayHost").ToList();
            foreach (var o in overlays)
            {
                container.Children.Remove(o);
            }
        }

        private static void RemoveExistingAudioIndicator(Grid container)
        {
            if (container == null) return;
            var rings = container.Children.OfType<Border>().Where(b => (b.Tag as string) == "AudioIndicator").ToList();
            foreach (var r in rings)
            {
                container.Children.Remove(r);
            }
        }

        // Update only the caption overlay visibility/content at runtime
        public void UpdateOverlay(Border border, bool show, string overlayText)
        {
            if (border == null) return;
            border.Dispatcher.Invoke(() =>
            {
                if (border.Child is Grid container)
                {
                    RemoveExistingOverlay(container);
                    if (show && !string.IsNullOrEmpty(overlayText))
                    {
                        // Try to find the video to size the host correctly
                        var vv = container.Children.OfType<VideoView>().FirstOrDefault();
                        if (vv != null)
                        {
                            var host = BuildOverlayHostForVideo(vv);
                            host.Children.Add(BuildOverlay(overlayText, _calcWidth, _calcHeight));
                            container.Children.Add(host);
                        }
                        else
                        {
                            // Fallback: add overlay directly to container
                            container.Children.Add(BuildOverlay(overlayText, _calcWidth, _calcHeight));
                        }
                    }
                }
            });
        }

        // Update only the audio indicator (green ring) at runtime
        public void UpdateAudioIndicator(Border border, bool audioOn)
        {
            if (border == null) return;
            border.Dispatcher.Invoke(() =>
            {
                if (border.Child is Grid container)
                {
                    RemoveExistingAudioIndicator(container);
                    if (audioOn)
                    {
                        container.Children.Add(BuildAudioIndicator());
                    }
                }
            });
        }

        internal static void UpdateAudioIndicatorOnContainer(Grid container, bool audioOn)
        {
            if (container == null) return;
            RemoveExistingAudioIndicator(container);
            if (audioOn)
            {
                container.Children.Add(BuildAudioIndicator());
            }
        }

        private void ToggleMute(LibVLCSharp.Shared.MediaPlayer mp)
        {
            try
            {
                mp.ToggleMute();
                if (!mp.Mute && mp.Volume <= 0)
                {
                    mp.Volume = 80; // ensure audible
                }
            }
            catch { }
        }

        private void ForceUnmute(LibVLCSharp.Shared.MediaPlayer mp)
        {
            try
            {
                mp.Mute = false;
                if (mp.Volume <= 0)
                {
                    mp.Volume = 80; // ensure audible
                }
            }
            catch { }
        }

        public void ApplyGlobalMute(DependencyObject root, bool mute)
        {
            if (root == null) return;
            void Walk(DependencyObject d)
            {
                int count = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var child = VisualTreeHelper.GetChild(d, i);
                        if (child is VideoView vv && vv.MediaPlayer != null)
                        {
                            try
                            {
                                vv.MediaPlayer.Mute = mute;
                                if (!mute && vv.MediaPlayer.Volume <= 0)
                                {
                                    vv.MediaPlayer.Volume = 80;
                                }
                                // update indicator on containing grid
                                DependencyObject p = vv;
                                Grid container = null;
                                while (p != null)
                                {
                                    p = VisualTreeHelper.GetParent(p);
                                    if (p is Grid g) { container = g; break; }
                                }
                                if (container != null)
                                {
                                    UpdateAudioIndicatorOnContainer(container, audioOn: !mute);
                                }
                            }
                            catch { }
                        }
                        Walk(child);
                    }
                    catch (Exception ex)
                    {
                        //
                    }
                }
                // Ensure on UI thread
                if (root is DispatcherObject disp && !disp.Dispatcher.CheckAccess())
                {
                    disp.Dispatcher.Invoke(() => Walk(root));
                }
                else
                {
                    Walk(root);
                }
            }
        }

        public void RenderSync(Border border, indexableImage image, SMEngine.CSMEngine.ImageSet s, string overlayText, bool defaultMute)
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
                    var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(libVLC) { Mute = defaultMute };
                    var videoView = new VideoView
                    {
                        MediaPlayer = mediaPlayer,
                        Width = Math.Min(_calcWidth(), border.ActualWidth > 0 ? border.ActualWidth : _calcWidth()),
                        Height = Math.Min(border.ActualHeight > 0 ? border.ActualHeight : _calcHeight(), _calcHeight()),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    mediaPlayer.Play(new Media(libVLC, s.VideoSource, FromType.FromLocation));
                    mediaPlayer.EncounteredError += (sender, e) =>
                    {
                        _log($"Error encountered playing video {s.VideoSource} {e.ToString()}");
                        try { mediaPlayer.Stop(); } catch { }
                    };
                    Panel.SetZIndex(videoView, 0);
                    container.Children.Add(videoView);

                    // initial indicator state (Mute defaults to true)
                    UpdateAudioIndicatorOnContainer(container, audioOn: !mediaPlayer.Mute);

                    // Overlay over the video area
                    RemoveExistingOverlay(container);
                    if (!string.IsNullOrEmpty(overlayText))
                    {
                        var host = BuildOverlayHostForVideo(videoView);
                        host.Children.Add(BuildOverlay(overlayText, _calcWidth, _calcHeight));
                        container.Children.Add(host);
                    }
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

                    RemoveExistingOverlay(container);
                    if (!string.IsNullOrEmpty(overlayText))
                    {
                        container.Children.Add(BuildOverlay(overlayText, _calcWidth, _calcHeight));
                    }
                }

                border.Child = container;
            });
        }

        public async Task RenderAsync(Border border, indexableImage image, SMEngine.CSMEngine.ImageSet s, bool defaultMute)
        {
            if (border == null || s == null) return;

            if (s.IsVideo && !string.IsNullOrEmpty(s.VideoSource))
            {
                await border.Dispatcher.InvokeAsync(() =>
                {
                    // If a video is already playing and allowed to finish, skip
                    if (border.Child is Grid existingContainer)
                    {
                        var playing = existingContainer.Children.OfType<VideoView>().FirstOrDefault(v => v.MediaPlayer != null && v.MediaPlayer.IsPlaying);
                        if (playing != null)
                        {
                            _log($"skipping video in grid, still playing");
                            return;
                        }
                    }
                    if (border.Child is VideoView existingVv && existingVv.MediaPlayer != null && existingVv.MediaPlayer.IsPlaying)
                    {
                        _log($"skipping video, still playing");
                        return;
                    }

                    // Stop and dispose any old video
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

                    // Create container and video
                    var container = new Grid { ClipToBounds = true };
                   // AttachTileClick(container);

                    var libVLC = new LibVLC();
                    var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(libVLC) { Mute = defaultMute };
                    var videoView = new VideoView
                    {
                        MediaPlayer = mediaPlayer,
                        Width = Math.Min(_calcWidth(), border.ActualWidth > 0 ? border.ActualWidth : _calcWidth()),
                        Height = Math.Min(border.ActualHeight > 0 ? border.ActualHeight : _calcHeight(), _calcHeight()),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    mediaPlayer.Play(new Media(libVLC, s.VideoSource, FromType.FromLocation));
                    mediaPlayer.EncounteredError += (sender, e) =>
                    {
                        _log($"Error encountered playing video {s.VideoSource} {e.ToString()}");
                        try { mediaPlayer.Stop(); } catch { }
                    };
                    Panel.SetZIndex(videoView, 0);
                    container.Children.Add(videoView);

                    // initial indicator state
                    UpdateAudioIndicatorOnContainer(container, audioOn: !mediaPlayer.Mute);

                    border.Child = container;
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
                else if (border.Child is Grid gridWithVideo && gridWithVideo.Children.OfType<VideoView>().Any())
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
