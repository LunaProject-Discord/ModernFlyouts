using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using ModernFlyouts.Core.Media.Control;
using ModernFlyouts.WinUI.Media;

namespace ModernFlyouts.WinUI.Controls
{
    /// <summary>
    /// Shows one <see cref="MediaSession"/>. Updates come from INotifyPropertyChanged;
    /// Core raises them on the UI thread (UiDispatcher), so no marshalling is needed here.
    /// </summary>
    public sealed partial class MediaPanel : UserControl
    {
        private MediaSession? _session;
        private bool _updatingTimeline;
        private bool _scrubbing;

        public MediaPanel()
        {
            InitializeComponent();

            TimelineSlider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => BeginScrub()), true);
            TimelineSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => EndScrub()), true);
            TimelineSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => EndScrub()), true);
        }

        private bool _showPlayerInfo = true;
        private bool _showSeekbar = true;

        public bool ShowPlayerInfo
        {
            get => _showPlayerInfo;
            set { _showPlayerInfo = value; UpdateAll(); }
        }

        public bool ShowSeekbar
        {
            get => _showSeekbar;
            set { _showSeekbar = value; UpdateTimeline(); }
        }

        /// <summary>Raised whenever the user interacts, so the flyout can postpone hiding.</summary>
        public event EventHandler? Interacted;

        public MediaSession? Session
        {
            get => _session;
            set
            {
                if (ReferenceEquals(_session, value))
                {
                    return;
                }

                if (_session != null)
                {
                    _session.PropertyChanged -= Session_PropertyChanged;
                }

                _session = value;

                if (_session != null)
                {
                    _session.PropertyChanged += Session_PropertyChanged;
                }

                UpdateAll();
            }
        }

        private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MediaSession.TimelineStartTime):
                case nameof(MediaSession.TimelineEndTime):
                case nameof(MediaSession.PlaybackPosition):
                case nameof(MediaSession.IsPlaybackPositionEnabled):
                case nameof(MediaSession.IsTimelinePropertiesEnabled):
                    UpdateTimeline();
                    break;
                default:
                    UpdateAll();
                    break;
            }
        }

        private void UpdateAll()
        {
            var s = _session;
            if (s == null)
            {
                return;
            }

            SourceName.Text = s.MediaSourceName ?? string.Empty;
            SourceIcon.Source = ArtworkImages.ToImageSource(s.MediaSourceIcon);
            SourcePanel.Visibility = _showPlayerInfo && !string.IsNullOrEmpty(SourceName.Text) ? Visibility.Visible : Visibility.Collapsed;

            TitleText.Text = s.Title ?? string.Empty;
            ArtistText.Text = s.Artist ?? string.Empty;
            ArtistText.Visibility = string.IsNullOrEmpty(ArtistText.Text) ? Visibility.Collapsed : Visibility.Visible;
            Artwork.Source = ArtworkImages.ToImageSource(s.Thumbnail);

            PreviousButton.Command = s.PreviousTrackCommand;
            PlayPauseButton.Command = s.PlayOrPauseCommand;
            NextButton.Command = s.NextTrackCommand;
            PreviousButton.IsEnabled = s.IsPreviousEnabled;
            PlayPauseButton.IsEnabled = s.IsPlayOrPauseEnabled;
            NextButton.IsEnabled = s.IsNextEnabled;
            PlayPauseGlyph.Glyph = s.IsPlaying ? "" : "";
            ToolTipService.SetToolTip(PlayPauseButton, s.IsPlaying ? "Pause" : "Play");

            UpdateTimeline();
        }

        private void UpdateTimeline()
        {
            var s = _session;
            if (s == null)
            {
                return;
            }

            double start = s.TimelineStartTime.TotalSeconds;
            double end = s.TimelineEndTime.TotalSeconds;
            bool visible = _showSeekbar && s.IsTimelinePropertiesEnabled && end > start;
            TimelinePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                return;
            }

            TimelineSlider.IsEnabled = s.IsPlaybackPositionEnabled;
            DurationText.Text = Format(s.TimelineEndTime);

            if (_scrubbing)
            {
                return; // don't fight the user's thumb
            }

            _updatingTimeline = true;
            TimelineSlider.Minimum = start;
            TimelineSlider.Maximum = end;
            TimelineSlider.Value = Math.Clamp(s.PlaybackPosition.TotalSeconds, start, end);
            _updatingTimeline = false;
            PositionText.Text = Format(s.PlaybackPosition);
        }

        private void TimelineSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updatingTimeline || _session == null)
            {
                return;
            }

            var position = TimeSpan.FromSeconds(e.NewValue);
            PositionText.Text = Format(position);
            _session.PlaybackPosition = position; // enhanced sessions defer the seek until EndTimelineScrub
            Interacted?.Invoke(this, EventArgs.Empty);
        }

        private void BeginScrub()
        {
            _scrubbing = true;
            (_session as EnhancedGSMTCMediaSession)?.BeginTimelineScrub();
            Interacted?.Invoke(this, EventArgs.Empty);
        }

        private void EndScrub()
        {
            if (!_scrubbing)
            {
                return;
            }

            _scrubbing = false;
            (_session as EnhancedGSMTCMediaSession)?.EndTimelineScrub();
        }

        private static string Format(TimeSpan time) =>
            time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
