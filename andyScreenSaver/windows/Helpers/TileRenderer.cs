using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
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
        private static readonly LibVLC _sharedLibVLC = new LibVLC(
            "--no-keyboard-events", "--no-mouse-events",
            "--freetype-background-color=0",       // black background behind marquee text
            "--freetype-background-opacity=140");  // ~55% opaque, matching image caption style
        private static readonly TimeSpan MaxDisplayDuration = TimeSpan.FromMinutes(20);
        private readonly Dictionary<Border, DateTime> _cellDisplayStartTimes = new Dictionary<Border, DateTime>();

        private readonly Func<double> _calcWidth;
        private readonly Func<double> _calcHeight;
        private readonly Action<string> _log;
        private readonly SMEngine.CSMEngine _engine;

        public TileRenderer(Func<double> calculateWidth,
                            Func<double> calculateHeight,
                            Action<string> log,
                            SMEngine.CSMEngine engine)
        {
            _calcWidth = calculateWidth ?? throw new ArgumentNullException(nameof(calculateWidth));
            _calcHeight = calculateHeight ?? throw new ArgumentNullException(nameof(calculateHeight));
            _log = log ?? (_ => { });
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        private bool HasContentExceededTimeout(Border border)
        {
            if (border == null) return false;
            if (!_cellDisplayStartTimes.TryGetValue(border, out var startTime))
                return false;
            return DateTime.Now - startTime > MaxDisplayDuration;
        }

        private void RecordDisplayStartTime(Border border)
        {
            if (border == null) return;
            _cellDisplayStartTimes[border] = DateTime.Now;
        }

        private static bool IsTrustedSmugMugUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
                if (!(uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)) return false;
                var host = uri.Host?.ToLowerInvariant();
                // Allow smugmug owned hosts (e.g., photos.smugmug.com, video.smugmug.com, cdn.smugmug.com)
                if (string.IsNullOrEmpty(host)) return false;
                if (host == "smugmug.com" || host.EndsWith(".smugmug.com")) return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static double GetCaptionFontSize(double cellHeight)
        {
            double pct = 3.5;
            var raw = ConfigurationManager.AppSettings["captionFontPercent"];
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) && parsed > 0)
                pct = parsed;
            return Math.Max(10, cellHeight * (pct / 100.0));
        }

        // Returns the font size for a VLC marquee, accounting for letterboxing/pillarboxing.
        // videoWidth/Height are the source video dimensions (0 if unknown — falls back to cellHeight).
        //
        // VLC marquee Size is in source video pixels (the filter runs pre-display-scaling).
        // To get a font that appears N display pixels tall, we need N * (sourceHeight / displayedHeight)
        // source pixels, where displayedHeight is the rendered video area in display-pixel space.
        private static int ComputeMarqueeFontSize(uint videoWidth, uint videoHeight, double cellWidth, double cellHeight)
        {
            if (videoWidth > 0 && videoHeight > 0)
            {
                double videoAspect = (double)videoWidth / videoHeight;
                double cellAspect = cellWidth / cellHeight;
                // Letterboxed: video wider than cell — rendered height is less than cellHeight.
                // Pillarboxed/exact: video height fills the cell.
                double displayedHeight = videoAspect > cellAspect ? cellWidth / videoAspect : cellHeight;
                double desiredDisplayPx = GetCaptionFontSize(displayedHeight);
                // Scale to source-pixel space so it appears the right size after display scaling.
                return (int)Math.Max(10, desiredDisplayPx * videoHeight / displayedHeight);
            }
            return (int)GetCaptionFontSize(cellHeight);
        }

        // Applies VLC marquee once playback starts and actual video dimensions are known.
        // The Playing event fires on a VLC thread; VLC docs say not to call back into libvlc
        // from within an event handler, so the actual API calls are dispatched via Task.Run.
        private void ScheduleMarqueeOnPlay(LibVLCSharp.Shared.MediaPlayer mp, string text, double cellWidth, double cellHeight)
        {
            EventHandler<EventArgs> handler = null;
            handler = (_, __) =>
            {
                mp.Playing -= handler;
                uint vw = 0, vh = 0; mp.Size(0, ref vw, ref vh);
                _ = Task.Run(() => ApplyVlcMarquee(mp, text, ComputeMarqueeFontSize(vw, vh, cellWidth, cellHeight)));
            };
            mp.Playing += handler;
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
                FontSize = GetCaptionFontSize(calcHeight()),
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

        private static void ApplyVlcMarquee(LibVLCSharp.Shared.MediaPlayer mp, string text, int fontSize)
        {
            try
            {
                mp.SetMarqueeInt(VideoMarqueeOption.Enable, 1);
                mp.SetMarqueeString(VideoMarqueeOption.Text, text);
                mp.SetMarqueeInt(VideoMarqueeOption.Color, 0xFFFFFF);
                mp.SetMarqueeInt(VideoMarqueeOption.Opacity, 180);
                mp.SetMarqueeInt(VideoMarqueeOption.Position, 9); // bottom-left
                mp.SetMarqueeInt(VideoMarqueeOption.Size, fontSize);
                mp.SetMarqueeInt(VideoMarqueeOption.Timeout, 0);
            }
            catch { }
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
                    var vv = container.Children.OfType<VideoView>().FirstOrDefault();
                    if (vv?.MediaPlayer != null)
                    {
                        // Video: use VLC marquee (WPF Z-index cannot overlay a native HWND).
                        // Call off the UI thread to avoid deadlock with VLC's video output thread.
                        // Read cell dims on UI thread; video dims are read on the Task thread (already playing).
                        var capturedMp = vv.MediaPlayer;
                        var cw = _calcWidth();
                        var ch = _calcHeight();
                        if (show && !string.IsNullOrEmpty(overlayText))
                            _ = Task.Run(() => { uint vw = 0, vh = 0; capturedMp.Size(0, ref vw, ref vh); ApplyVlcMarquee(capturedMp, overlayText, ComputeMarqueeFontSize(vw, vh, cw, ch)); });
                        else
                            _ = Task.Run(() => { try { capturedMp.SetMarqueeInt(VideoMarqueeOption.Enable, 0); } catch { } });
                    }
                    else
                    {
                        // Image: use WPF overlay
                        RemoveExistingOverlay(container);
                        if (show && !string.IsNullOrEmpty(overlayText))
                            container.Children.Add(BuildOverlay(overlayText, _calcWidth, _calcHeight));
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

        // Detach MediaPlayer on UI thread first, then stop/dispose on background thread
        // to avoid deadlock where VLC tries to marshal back to a blocked UI thread.
        private static void DisposeVideoView(VideoView vv)
        {
            if (vv == null) return;
            var mp = vv.MediaPlayer;
            vv.MediaPlayer = null;
            if (mp != null)
                _ = Task.Run(() => { try { mp.Stop(); } catch { } try { mp.Dispose(); } catch { } });
            try { vv.Dispose(); } catch { }
        }

        public void UpdateOverlaySizes(DependencyObject root)
        {
            if (root == null) return;
            WalkAndUpdateSizes(root);
        }

        private void WalkAndUpdateSizes(DependencyObject d)
        {
            int count = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var child = VisualTreeHelper.GetChild(d, i);
                    if (child is Grid container)
                    {
                        // Update VLC marquee size for any playing video.
                        // Call off the UI thread to avoid deadlock with VLC's video output thread.
                        var vv = container.Children.OfType<VideoView>().FirstOrDefault();
                        if (vv?.MediaPlayer != null)
                        {
                            var capturedMp = vv.MediaPlayer;
                            var cw = _calcWidth();
                            var ch = _calcHeight();
                            _ = Task.Run(() => { try { uint vw = 0, vh = 0; capturedMp.Size(0, ref vw, ref vh); capturedMp.SetMarqueeInt(VideoMarqueeOption.Size, ComputeMarqueeFontSize(vw, vh, cw, ch)); } catch { } });
                        }

                        // Rebuild WPF overlay with current geometry
                        var overlay = container.Children.OfType<Border>()
                            .FirstOrDefault(b => (b.Tag as string) == "CaptionOverlay");
                        if (overlay?.Child is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
                        {
                            var text = tb.Text;
                            container.Children.Remove(overlay);
                            container.Children.Add(BuildOverlay(text, _calcWidth, _calcHeight));
                        }
                    }
                    WalkAndUpdateSizes(child);
                }
                catch { }
            }
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

        public void RenderSync(Border border, indexableImage image, SMEngine.CSMEngine.ImageSet s, string overlayText, bool defaultMute, bool allowVideoToFinish = true)
        {
            if (border == null || s == null) return;

            border.Dispatcher.Invoke(() =>
            {
                var container = new Grid { ClipToBounds = true };
                if (s.IsVideo && !string.IsNullOrEmpty(s.VideoSource))
                {
                    if (!IsTrustedSmugMugUrl(s.VideoSource))
                    {
                        _log($"Blocked non-SmugMug video source: {s.VideoSource}");
                        return;
                    }

                    // If allowVideoToFinish is enabled, check if a video is currently playing
                    if (allowVideoToFinish && !HasContentExceededTimeout(border))
                    {
                        // Check for playing video in direct child
                        if (border.Child is VideoView existingVv && existingVv.MediaPlayer != null && existingVv.MediaPlayer.IsPlaying)
                        {
                            _log($"allowVideoToFinish: skipping video replacement, one is still playing");
                            _engine.ReturnImageToQueue(s);
                            return;
                        }
                        // Check for playing video in grid container
                        if (border.Child is Grid existingContainer)
                        {
                            var playingVideo = existingContainer.Children.OfType<VideoView>().FirstOrDefault(v => v.MediaPlayer != null && v.MediaPlayer.IsPlaying);
                            if (playingVideo != null)
                            {
                                _log($"allowVideoToFinish: skipping video replacement in grid, one is still playing");
                                _engine.ReturnImageToQueue(s);
                                return;
                            }
                        }
                    }
                    else if (allowVideoToFinish && HasContentExceededTimeout(border))
                    {
                        _log($"Content in cell exceeded 20-minute timeout, forcing replacement");
                    }

                    // Stop and dispose any previous video in either direct child or container
                    if (border.Child is VideoView oldVv)
                    {
                        DisposeVideoView(oldVv);
                        border.Child = null;
                    }
                    else if (border.Child is Grid g)
                    {
                        foreach (var child in g.Children)
                            if (child is VideoView vv) DisposeVideoView(vv);
                        border.Child = null;
                    }

                    var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_sharedLibVLC) { Mute = defaultMute };
                    var videoView = new VideoView
                    {
                        MediaPlayer = mediaPlayer,
                        Width = Math.Min(_calcWidth(), border.ActualWidth > 0 ? border.ActualWidth : _calcWidth()),
                        Height = Math.Min(border.ActualHeight > 0 ? border.ActualHeight : _calcHeight(), _calcHeight()),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    using (var media = new Media(_sharedLibVLC, s.VideoSource, FromType.FromLocation))
                        mediaPlayer.Play(media);
                    mediaPlayer.EncounteredError += (sender, e) =>
                    {
                        _log($"Error encountered playing video {s.VideoSource} {e.ToString()}");
                        try { mediaPlayer.Stop(); } catch { }
                    };
                    Panel.SetZIndex(videoView, 0);
                    container.Children.Add(videoView);

                    // initial indicator state (Mute defaults to true)
                    UpdateAudioIndicatorOnContainer(container, audioOn: !mediaPlayer.Mute);

                    // Defer marquee until Playing fires so we know the actual video dimensions.
                    // Font size is computed from the real rendered area, accounting for letterboxing.
                    if (!string.IsNullOrEmpty(overlayText))
                        ScheduleMarqueeOnPlay(mediaPlayer, overlayText, _calcWidth(), _calcHeight());
                }
                else
                {
                    // Guard: don't replace a playing video with a still image
                    if (allowVideoToFinish)
                    {
                        if (border.Child is Grid existingContainer)
                        {
                            var playingVideo = existingContainer.Children.OfType<VideoView>().FirstOrDefault(v => v.MediaPlayer != null && v.MediaPlayer.IsPlaying);
                            if (playingVideo != null)
                            {
                                _log($"allowVideoToFinish: skipping image replacement, video still playing");
                                _engine.ReturnImageToQueue(s);
                                return;
                            }
                        }
                        if (border.Child is VideoView existingVv && existingVv.MediaPlayer != null && existingVv.MediaPlayer.IsPlaying)
                        {
                            _log($"allowVideoToFinish: skipping image replacement, video still playing");
                            _engine.ReturnImageToQueue(s);
                            return;
                        }
                    }

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
                RecordDisplayStartTime(border);

                // Increment counter only after successful render
                _engine.IncrementImageCounter();
            });
        }

        public async Task RenderAsync(Border border, indexableImage image, SMEngine.CSMEngine.ImageSet s, string overlayText, bool defaultMute, bool allowVideoToFinish = true)
        {
            if (border == null || s == null) return;

            if (s.IsVideo && !string.IsNullOrEmpty(s.VideoSource))
            {
                if (!IsTrustedSmugMugUrl(s.VideoSource))
                {
                    _log($"Blocked non-SmugMug video source: {s.VideoSource}");
                    return;
                }
                await border.Dispatcher.InvokeAsync(() =>
                {
                    // If allowVideoToFinish is enabled, check if a video is already playing and skip replacement
                    if (allowVideoToFinish && !HasContentExceededTimeout(border))
                    {
                        // Check for playing video in grid container
                        if (border.Child is Grid existingContainer)
                        {
                            var playing = existingContainer.Children.OfType<VideoView>().FirstOrDefault(v => v.MediaPlayer != null && v.MediaPlayer.IsPlaying);
                            if (playing != null)
                            {
                                _log($"allowVideoToFinish: skipping video in grid, still playing");
                                _engine.ReturnImageToQueue(s);
                                return;
                            }
                        }
                        // Check for playing video in direct child
                        if (border.Child is VideoView existingVv && existingVv.MediaPlayer != null && existingVv.MediaPlayer.IsPlaying)
                        {
                            _log($"allowVideoToFinish: skipping video, still playing");
                            _engine.ReturnImageToQueue(s);
                            return;
                        }
                    }
                    else if (allowVideoToFinish && HasContentExceededTimeout(border))
                    {
                        _log($"Content in cell exceeded 20-minute timeout, forcing replacement");
                    }

                    // Stop and dispose any old video
                    if (border.Child is VideoView oldVv)
                    {
                        DisposeVideoView(oldVv);
                        border.Child = null;
                    }
                    else if (border.Child is Grid g)
                    {
                        foreach (var child in g.Children)
                            if (child is VideoView vv) DisposeVideoView(vv);
                        border.Child = null;
                    }

                    // Create container and video
                    var container = new Grid { ClipToBounds = true };
                    var mediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_sharedLibVLC) { Mute = defaultMute };
                    var videoView = new VideoView
                    {
                        MediaPlayer = mediaPlayer,
                        Width = Math.Min(_calcWidth(), border.ActualWidth > 0 ? border.ActualWidth : _calcWidth()),
                        Height = Math.Min(border.ActualHeight > 0 ? border.ActualHeight : _calcHeight(), _calcHeight()),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    using (var media = new Media(_sharedLibVLC, s.VideoSource, FromType.FromLocation))
                        mediaPlayer.Play(media);
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
                    RecordDisplayStartTime(border);

                    // Increment counter only after successful render
                    _engine.IncrementImageCounter();

                    // Defer marquee until Playing fires so we know the actual video dimensions.
                    // Font size is computed from the real rendered area, accounting for letterboxing.
                    if (!string.IsNullOrEmpty(overlayText))
                        ScheduleMarqueeOnPlay(mediaPlayer, overlayText, _calcWidth(), _calcHeight());
                });
                return;
            }

            // Render photo
            await border.Dispatcher.InvokeAsync(() =>
            {
                // Guard: don't replace a playing video with a still image
                if (allowVideoToFinish && !HasContentExceededTimeout(border))
                {
                    if (border.Child is Grid existingContainer)
                    {
                        var playingVideo = existingContainer.Children.OfType<VideoView>().FirstOrDefault(v => v.MediaPlayer != null && v.MediaPlayer.IsPlaying);
                        if (playingVideo != null)
                        {
                            _log($"allowVideoToFinish: skipping image replacement, video still playing");
                            _engine.ReturnImageToQueue(s);
                            return;
                        }
                    }
                    if (border.Child is VideoView existingVv && existingVv.MediaPlayer != null && existingVv.MediaPlayer.IsPlaying)
                    {
                        _log($"allowVideoToFinish: skipping image replacement, video still playing");
                        _engine.ReturnImageToQueue(s);
                        return;
                    }
                }
                else if (allowVideoToFinish && HasContentExceededTimeout(border))
                {
                    _log($"Content in cell exceeded 20-minute timeout, forcing image replacement");
                }

                // Dispose any existing video
                if (border.Child is VideoView oldVv)
                    DisposeVideoView(oldVv);
                else if (border.Child is Grid g)
                    foreach (var child in g.Children)
                        if (child is VideoView vv) DisposeVideoView(vv);
                border.Child = null;

                var targetImg = image ?? new indexableImage();
                targetImg.MaxHeight = _calcHeight();
                targetImg.Width = _calcWidth();
                targetImg.Source = ImageUtils.BitmapToBitmapImage(s.Bitmap);
                s.Bitmap?.Dispose();
                s.Bitmap = null;

                var container = new Grid { ClipToBounds = true };
                container.Children.Add(targetImg);
                if (!string.IsNullOrEmpty(overlayText))
                    container.Children.Add(BuildOverlay(overlayText, _calcWidth, _calcHeight));
                border.Child = container;
                RecordDisplayStartTime(border);

                // Increment counter only after successful render
                _engine.IncrementImageCounter();
            });
        }
    }
}
