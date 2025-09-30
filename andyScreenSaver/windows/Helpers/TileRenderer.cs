using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

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

                    // If an old video exists, stop and dispose
                    if (border.Child is VideoView oldVv && oldVv.MediaPlayer != null)
                    {
                        try { oldVv.MediaPlayer.Stop(); } catch { }
                        try { oldVv.MediaPlayer.Dispose(); } catch { }
                        try { oldVv.Dispose(); } catch { }
                        border.Child = null;
                    }

                    // Create and play new video
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
