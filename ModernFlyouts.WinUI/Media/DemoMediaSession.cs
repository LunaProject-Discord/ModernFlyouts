#if DEBUG
using System;
using ModernFlyouts.Core.Media;
using ModernFlyouts.Core.Media.Control;

namespace ModernFlyouts.WinUI.Media
{
    /// <summary>
    /// Debug-only session with sample data so the media panel can be designed without a player
    /// (start the app with --demo-media).
    /// </summary>
    internal sealed class DemoMediaSession : MediaSession
    {
        /// <param name="canSeek">false mimics players like Apple Music that don't allow seeking.</param>
        public DemoMediaSession(bool canSeek = true)
        {
            Title = "Design check track";
            Artist = "ModernFlyouts";
            MediaSourceName = "Demo player";
            Thumbnail = CreateArtwork(256);
            MediaSourceIcon = CreateArtwork(32);
            IsPlaying = true;
            IsPlayEnabled = IsPauseEnabled = IsPlayOrPauseEnabled = true;
            IsPreviousEnabled = IsNextEnabled = true;
            IsShuffleEnabled = IsRepeatEnabled = IsStopEnabled = true;
            IsShuffleActive = false;
            AutoRepeatMode = MediaPlaybackAutoRepeatMode.List;
            IsTimelinePropertiesEnabled = true;
            IsPlaybackPositionEnabled = canSeek;
            TimelineStartTime = TimeSpan.Zero;
            TimelineEndTime = TimeSpan.FromSeconds(225);
            PlaybackPosition = TimeSpan.FromSeconds(83);
        }

        /// <summary>Diagonal accent gradient so the artwork area is obviously filled.</summary>
        private static MediaArtwork CreateArtwork(int size)
        {
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = (y * size + x) * 4;
                    double t = (x + y) / (2.0 * size);
                    pixels[i] = (byte)(200 - 120 * t);  // B
                    pixels[i + 1] = (byte)(90 + 60 * t); // G
                    pixels[i + 2] = (byte)(40 + 180 * t); // R
                    pixels[i + 3] = 255;
                }
            }
            return new MediaArtwork(pixels, size, size);
        }

        protected override void Play() => IsPlaying = true;
        protected override void Pause() => IsPlaying = false;
        protected override void PreviousTrack() => PlaybackPosition = TimeSpan.Zero;
        protected override void NextTrack() => PlaybackPosition = TimeSpan.Zero;
        protected override void ChangeShuffleActive() => IsShuffleActive = !(IsShuffleActive ?? false);
        protected override void ChangeAutoRepeatMode() => AutoRepeatMode = AutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None,
        };
        protected override void Stop() => IsPlaying = false;
        protected override void PlaybackPositionChanged(TimeSpan playbackPosition) { }
    }
}
#endif
