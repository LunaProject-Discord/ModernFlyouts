using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using ModernFlyouts.Core.Media;
using ModernFlyouts.Core.Media.Control;
using ModernFlyouts.Properties;
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
        private bool _showPlayerInfo = true;
        private bool _showSeekbar = true;
        private bool _showShuffle = true;
        private bool _showRepeat = true;
        private bool _useThumbnailAsBackground = true;
        private bool _alignThumbnailToRight = true;

        public MediaPanel()
        {
            InitializeComponent();

            TimelineSlider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => BeginScrub()), true);
            TimelineSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => EndScrub()), true);
            TimelineSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => EndScrub()), true);

            ToolTipService.SetToolTip(PreviousButton, Strings.SessionControl_Previous);
            ToolTipService.SetToolTip(NextButton, Strings.SessionControl_Next);
            ToolTipService.SetToolTip(StopButton, Strings.SessionControl_Stop);
            ToolTipService.SetToolTip(MoreButton, Strings.SessionControl_MoreControls);
            ToolTipService.SetToolTip(TimelineButton, Strings.SessionControl_TimelineInfo);

            ShuffleButton.Click += (_, _) => { Execute(_session?.ChangeShuffleActiveCommand); UpdateAll(); };
            RepeatButton.Click += (_, _) => { Execute(_session?.ChangeAutoRepeatModeCommand); UpdateAll(); };
        }

        /// <summary>Raised whenever the user interacts, so the flyout can postpone hiding.</summary>
        public event EventHandler? Interacted;

        /// <summary>Raised when the panel's height changes (seek bar / controls toggled).</summary>
        public event EventHandler? LayoutChanged;

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

        public bool ShowShuffle
        {
            get => _showShuffle;
            set { _showShuffle = value; UpdateAll(); }
        }

        public bool ShowRepeat
        {
            get => _showRepeat;
            set { _showRepeat = value; UpdateAll(); }
        }

        public bool UseThumbnailAsBackground
        {
            get => _useThumbnailAsBackground;
            set { _useThumbnailAsBackground = value; UpdateAll(); }
        }

        /// <summary>Artwork on the right (default) or on the left of the title.</summary>
        public bool AlignThumbnailToRight
        {
            get => _alignThumbnailToRight;
            set
            {
                _alignThumbnailToRight = value;
                InfoColumn.Width = value ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
                ArtworkColumn.Width = value ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
                Grid.SetColumn(InfoGrid, value ? 0 : 1);
                Grid.SetColumn(ArtworkBorder, value ? 1 : 0);
                UpdateAll();
            }
        }

        private void ContentPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            BackgroundArtwork.Width = e.NewSize.Width;
            BackgroundArtwork.Height = e.NewSize.Height;
        }

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

                // A different player starts with the default controls.
                MoreButton.IsChecked = false;
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
            SourceIcon.Visibility = SourceIcon.Source != null ? Visibility.Visible : Visibility.Collapsed;
            SourcePanel.Visibility = _showPlayerInfo && !string.IsNullOrEmpty(SourceName.Text) ? Visibility.Visible : Visibility.Collapsed;

            TitleText.Text = s.Title ?? string.Empty;
            ArtistText.Text = s.Artist ?? string.Empty;
            ArtistText.Visibility = string.IsNullOrEmpty(ArtistText.Text) ? Visibility.Collapsed : Visibility.Visible;
            Artwork.Source = ArtworkImages.ToImageSource(s.Thumbnail);
            ArtworkPlaceholder.Visibility = Artwork.Source == null ? Visibility.Visible : Visibility.Collapsed;
            BackgroundArtwork.Source = _useThumbnailAsBackground
                ? ArtworkImages.ToFadedBackground(s.Thumbnail, _alignThumbnailToRight) : null;

            PreviousButton.Command = s.PreviousTrackCommand;
            PlayPauseButton.Command = s.PlayOrPauseCommand;
            NextButton.Command = s.NextTrackCommand;
            StopButton.Command = s.StopCommand;
            PreviousButton.IsEnabled = s.IsPreviousEnabled;
            PlayPauseButton.IsEnabled = s.IsPlayOrPauseEnabled;
            NextButton.IsEnabled = s.IsNextEnabled;
            PlayPauseGlyph.Glyph = s.IsPlaying ? "" : "";
            ToolTipService.SetToolTip(PlayPauseButton, s.IsPlaying ? Strings.SessionControl_Pause : Strings.SessionControl_Play);

            // "More" holds shuffle / repeat / stop.
            bool hasMore = (_showShuffle && s.IsShuffleEnabled) || (_showRepeat && s.IsRepeatEnabled) || s.IsStopEnabled;
            MoreButton.Visibility = hasMore ? Visibility.Visible : Visibility.Collapsed;
            if (!hasMore)
            {
                MoreButton.IsChecked = false;
            }

            ShuffleButton.Visibility = _showShuffle && s.IsShuffleEnabled ? Visibility.Visible : Visibility.Collapsed;
            ShuffleButton.IsChecked = s.IsShuffleActive == true;
            RepeatButton.Visibility = _showRepeat && s.IsRepeatEnabled ? Visibility.Visible : Visibility.Collapsed;
            RepeatButton.IsChecked = s.AutoRepeatMode != MediaPlaybackAutoRepeatMode.None;
            RepeatGlyph.Glyph = s.AutoRepeatMode switch
            {
                MediaPlaybackAutoRepeatMode.Track => "", // repeat one
                MediaPlaybackAutoRepeatMode.List => "",  // repeat all
                _ => "",                                 // repeat off
            };
            StopButton.Visibility = s.IsStopEnabled ? Visibility.Visible : Visibility.Collapsed;
            UpdateControlsMode();

            UpdateTimeline();
        }

        private void UpdateControlsMode()
        {
            bool more = MoreButton.IsChecked == true;
            MainControls.Visibility = more ? Visibility.Collapsed : Visibility.Visible;
            MoreControls.Visibility = more ? Visibility.Visible : Visibility.Collapsed;
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
            bool hasTimeline = _showSeekbar && s.IsTimelinePropertiesEnabled && end > start;
            bool expanded = hasTimeline && TimelineButton.IsChecked == true;

            TimelineButton.Visibility = hasTimeline ? Visibility.Visible : Visibility.Collapsed;
            TimelineGlyph.Glyph = expanded ? "" : ""; // chevron down / up
            ProgressPanel.Visibility = hasTimeline && !expanded ? Visibility.Visible : Visibility.Collapsed;
            SeekPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

            if (!hasTimeline)
            {
                return;
            }

            double position = Math.Clamp(s.PlaybackPosition.TotalSeconds, start, end);
            TimelineProgress.Minimum = start;
            TimelineProgress.Maximum = end;
            TimelineProgress.Value = position;

            TimelineSlider.IsEnabled = s.IsPlaybackPositionEnabled;
            DurationText.Text = Format(s.TimelineEndTime);

            if (_scrubbing)
            {
                return; // don't fight the user's thumb
            }

            _updatingTimeline = true;
            TimelineSlider.Minimum = start;
            TimelineSlider.Maximum = end;
            TimelineSlider.Value = position;
            _updatingTimeline = false;
            PositionText.Text = Format(s.PlaybackPosition);
        }

        // Checked/Unchecked (not Click) so keyboard and UI Automation toggles work too.
        private void MoreButton_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateControlsMode();
            Interacted?.Invoke(this, EventArgs.Empty);
        }

        private void TimelineButton_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateTimeline();
            Interacted?.Invoke(this, EventArgs.Empty);
            LayoutChanged?.Invoke(this, EventArgs.Empty);
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

        private void Execute(System.Windows.Input.ICommand? command)
        {
            if (command?.CanExecute(null) == true)
            {
                command.Execute(null);
            }
            Interacted?.Invoke(this, EventArgs.Empty);
        }

        private static string Format(TimeSpan time) =>
            time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
