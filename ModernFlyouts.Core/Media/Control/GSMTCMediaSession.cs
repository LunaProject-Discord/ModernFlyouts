using CommunityToolkit.Mvvm.Input;
using ModernFlyouts.Core.AppInformation;
using ModernFlyouts.Core.Threading;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace ModernFlyouts.Core.Media.Control
{
    public class GSMTCMediaSession : MediaSession
    {
        private GlobalSystemMediaTransportControlsSession GSMTCSession;
        private SourceAppInfo sourceAppInfo;
        private int currentTrackNumber;

        // Players (e.g. browsers) report the position rarely, often only when playback starts, so
        // the shown position is advanced from the last report while playing.
        private readonly UiTimer timelineTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private TimeSpan reportedPosition;
        private DateTimeOffset reportedAtUtc;
        private double playbackRate = 1.0;
        private bool playbackStateKnown;

        public GSMTCMediaSession(GlobalSystemMediaTransportControlsSession globalSystemMediaTransportControlsSession)
        {
            GSMTCSession = globalSystemMediaTransportControlsSession;
            Initialize();
        }

        private void Initialize()
        {
            sourceAppInfo = SourceAppInfo.FromAppUserModelId(GSMTCSession.SourceAppUserModelId);
            if (sourceAppInfo != null)
            {
                sourceAppInfo.InfoFetched += SourceAppInfo_InfoFetched;
                sourceAppInfo.FetchInfosAsync();
                ActivateMediaSourceCommand = new RelayCommand(sourceAppInfo.Activate, () => sourceAppInfo != null);
            }

            GSMTCSession.MediaPropertiesChanged += GSMTCSession_MediaPropertiesChanged;
            GSMTCSession.PlaybackInfoChanged += GSMTCSession_PlaybackInfoChanged;
            GSMTCSession.TimelinePropertiesChanged += GSMTCSession_TimelinePropertiesChanged;
            timelineTimer.Tick += (_, _) => AdvanceTimeline();

            UpdateSessionInfo(GSMTCSession);
        }

        public override void Disconnect()
        {
            timelineTimer.Stop();

            if (GSMTCSession != null)
            {
                GSMTCSession.MediaPropertiesChanged -= GSMTCSession_MediaPropertiesChanged;
                GSMTCSession.PlaybackInfoChanged -= GSMTCSession_PlaybackInfoChanged;
                GSMTCSession.TimelinePropertiesChanged -= GSMTCSession_TimelinePropertiesChanged;
            }
            GSMTCSession = null;

            sourceAppInfo.Dispose();
            sourceAppInfo = null;
        }

        #region Hooking-up Events

        private void GSMTCSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession session, MediaPropertiesChangedEventArgs args)
        {
            UiDispatcher.Invoke(() =>
            {
                UpdateSessionInfo(session);
            });
        }

        private void GSMTCSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession session, PlaybackInfoChangedEventArgs args)
        {
            UiDispatcher.Invoke(() =>
            {
                UpdatePlaybackInfo(session);
            });
        }

        private void GSMTCSession_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession session, TimelinePropertiesChangedEventArgs args)
        {
            UiDispatcher.Invoke(() =>
            {
                UpdateTimelineInfo(session);
            });
        }

        #endregion

        private void SourceAppInfo_InfoFetched(object sender, EventArgs e)
        {
            sourceAppInfo.InfoFetched -= SourceAppInfo_InfoFetched;
            MediaSourceName = sourceAppInfo.DisplayName;

            SetMediaSourceIconFromStream(sourceAppInfo.LogoStream);
        }

        #region Updating Properties

        private void UpdatePlaybackInfo(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                if (session != null)
                {
                    var playback = session.GetPlaybackInfo();
                    if (playback != null)
                    {
                        bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                        if (playbackStateKnown && playing != IsPlaying)
                        {
                            // Players don't always report the position on pause/resume: keep the
                            // shown position and count elapsed time from now on.
                            reportedPosition = CalculatePosition();
                            reportedAtUtc = DateTimeOffset.UtcNow;
                        }

                        IsPlaying = playing;
                        playbackStateKnown = true;
                        playbackRate = playback.PlaybackRate is double rate && rate > 0 ? rate : 1.0;
                        IsShuffleActive = playback.IsShuffleActive;
                        AutoRepeatMode = playback.AutoRepeatMode switch
                        {
                            Windows.Media.MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.None,
                            Windows.Media.MediaPlaybackAutoRepeatMode.Track => MediaPlaybackAutoRepeatMode.Track,
                            Windows.Media.MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.List,
                            _ => throw new NotImplementedException()
                        };
                    }
                }
            }
            catch { }

            UpdateTimelineTimer();
        }

        private void UpdateTimelineInfo(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                var timeline = session.GetTimelineProperties();

                // Shown (read-only) even when the player doesn't allow seeking.
                if (timeline != null && timeline.EndTime > timeline.StartTime)
                {
                    TimelineStartTime = timeline.StartTime;
                    TimelineEndTime = timeline.EndTime;
                    reportedPosition = timeline.Position;
                    reportedAtUtc = GetReportTime(timeline);
                    SetPlaybackPosition(CalculatePosition());

                    IsTimelinePropertiesEnabled = true;
                }
                else
                {
                    TimelineStartTime = TimeSpan.Zero;
                    TimelineEndTime = TimeSpan.Zero;
                    PlaybackPosition = TimeSpan.Zero;

                    IsTimelinePropertiesEnabled = false;
                }
            }
            catch { }

            UpdateTimelineTimer();
        }

        /// <summary>When the position was reported (LastUpdatedTime), or now if that is unusable.</summary>
        private static DateTimeOffset GetReportTime(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline)
        {
            var now = DateTimeOffset.UtcNow;
            var updated = timeline.LastUpdatedTime;
            if (updated.Year < 2000 || updated > now || now - updated > TimeSpan.FromHours(12))
            {
                return now;
            }

            return updated.ToUniversalTime();
        }

        private TimeSpan CalculatePosition()
        {
            var position = reportedPosition;
            if (IsPlaying)
            {
                var elapsed = DateTimeOffset.UtcNow - reportedAtUtc;
                if (elapsed > TimeSpan.Zero)
                {
                    position += TimeSpan.FromTicks((long)(elapsed.Ticks * playbackRate));
                }
            }

            if (position < TimelineStartTime) position = TimelineStartTime;
            if (position > TimelineEndTime) position = TimelineEndTime;
            return position;
        }

        private void AdvanceTimeline()
        {
            if (!IsPlaying || !IsTimelinePropertiesEnabled)
            {
                timelineTimer.Stop();
                return;
            }

            SetPlaybackPosition(CalculatePosition());
        }

        private void UpdateTimelineTimer()
        {
            if (IsPlaying && IsTimelinePropertiesEnabled)
            {
                if (!timelineTimer.IsEnabled)
                {
                    timelineTimer.Start();
                }
            }
            else
            {
                timelineTimer.Stop();
            }
        }

        private async void UpdateSessionInfo(GlobalSystemMediaTransportControlsSession session)
        {
            RaiseMediaPropertiesChanging();

            try
            {
                var mediaInfo = await session.TryGetMediaPropertiesAsync();

                if (mediaInfo != null)
                {
                    Title = mediaInfo.Title;
                    Artist = mediaInfo.Artist;
                    if (currentTrackNumber != mediaInfo.TrackNumber)
                    {
                        TrackChangeDirection = (mediaInfo.TrackNumber - currentTrackNumber) switch
                        {
                            > 0 => MediaPlaybackTrackChangeDirection.Forward,
                            < 0 => MediaPlaybackTrackChangeDirection.Backward,
                            _ => MediaPlaybackTrackChangeDirection.Unknown
                        };
                    }

                    currentTrackNumber = mediaInfo.TrackNumber;

                    Debug.WriteLine(nameof(mediaInfo.Subtitle) + ": " + mediaInfo.Subtitle);
                }

                var playback = session.GetPlaybackInfo();

                if (playback != null)
                {
                    var playbackControls = playback.Controls;
                    IsPlayEnabled = playbackControls.IsPlayEnabled;
                    IsPauseEnabled = playbackControls.IsPauseEnabled;
                    IsPlayOrPauseEnabled = IsPlayEnabled || IsPauseEnabled;
                    IsPreviousEnabled = playbackControls.IsPreviousEnabled;
                    IsNextEnabled = playbackControls.IsNextEnabled;
                    IsShuffleEnabled = playbackControls.IsShuffleEnabled;
                    IsRepeatEnabled = playbackControls.IsRepeatEnabled;
                    IsStopEnabled = playbackControls.IsStopEnabled;
                    IsPlaybackPositionEnabled = playbackControls.IsPlaybackPositionEnabled;

                    PlaybackType = playback.PlaybackType switch
                    {
                        Windows.Media.MediaPlaybackType.Unknown => MediaPlaybackType.Unknown,
                        Windows.Media.MediaPlaybackType.Music => MediaPlaybackType.Music,
                        Windows.Media.MediaPlaybackType.Video => MediaPlaybackType.Video,
                        Windows.Media.MediaPlaybackType.Image => MediaPlaybackType.Image,
                        _ => throw new NotImplementedException()
                    };
                }

                // Playback state first: the timeline uses it to advance the reported position.
                UpdatePlaybackInfo(session);

                UpdateTimelineInfo(session);

                Thumbnail = await GetThumbnailImageSourceAsync(mediaInfo?.Thumbnail);
            }
            catch { }

            RaiseMediaPropertiesChanged();
        }

        #region Thumbnail fetching

        private const string SpotifyPackagedAUMID = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify";
        private const string SpotifyUnpackagedAUMID = "Spotify.exe";

        private bool IsSourceAppSpotify() => string.Equals(GSMTCSession.SourceAppUserModelId, SpotifyPackagedAUMID)
            || string.Equals(GSMTCSession.SourceAppUserModelId, SpotifyUnpackagedAUMID);

        private async Task<MediaArtwork> GetThumbnailImageSourceAsync(IRandomAccessStreamReference thumbnail)
        {
            return GetModifiedThumbnail(await MediaArtwork.DecodeAsync(thumbnail));
        }

        private MediaArtwork GetModifiedThumbnail(MediaArtwork artwork)
        {
            if (artwork != null && IsSourceAppSpotify())
            {
                return GetModifiedThumbnailForSpotify(artwork);
            }

            return artwork;
        }

        /// <summary>
        /// Gets a modified version of the original thumbnail provided by Spotify.
        /// It crops and removes extra padding and Spotify branding from the original thumbnail provided by Spotify.
        /// </summary>
        /// <param name="artwork">The original thumbnail provided by the source.</param>
        /// <returns>A cropped variant of the original thumbnail with extra padding and Spotify branding removed.</returns>
        /// <remarks>F*** you Spotify!</remarks>
        private static MediaArtwork GetModifiedThumbnailForSpotify(MediaArtwork artwork)
        {
            return artwork.Crop(33, 0, 234, 234);
        }

        #endregion

        #endregion

        #region Media Controlling

        protected override async void ChangeAutoRepeatMode()
        {
            try
            {
                Windows.Media.MediaPlaybackAutoRepeatMode autoRepeatMode = AutoRepeatMode switch
                {
                    MediaPlaybackAutoRepeatMode.None => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                    MediaPlaybackAutoRepeatMode.List => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                    MediaPlaybackAutoRepeatMode.Track => Windows.Media.MediaPlaybackAutoRepeatMode.None,
                    _ => throw new NotImplementedException()
                };

                await GSMTCSession?.TryChangeAutoRepeatModeAsync(autoRepeatMode);
            }
            catch { }
        }

        protected override async void ChangeShuffleActive()
        {
            try
            {
                if (IsShuffleActive.HasValue)
                {
                    await GSMTCSession?.TryChangeShuffleActiveAsync(!IsShuffleActive.Value);
                }
            }
            catch { }
        }

        protected override async void NextTrack()
        {
            try
            {
                await GSMTCSession?.TrySkipNextAsync();
            }
            catch { }
        }

        protected override async void Pause()
        {
            try
            {
                await GSMTCSession?.TryPauseAsync();
            }
            catch { }
        }

        protected override async void Play()
        {
            try
            {
                await GSMTCSession?.TryPlayAsync();
            }
            catch { }
        }

        protected override async void PlaybackPositionChanged(TimeSpan playbackPosition)
        {
            try
            {
                await GSMTCSession?.TryChangePlaybackPositionAsync(playbackPosition.Ticks);
            }
            catch { }
        }

        protected override async void PreviousTrack()
        {
            try
            {
                await GSMTCSession?.TrySkipPreviousAsync();
            }
            catch { }
        }

        protected override async void Stop()
        {
            try
            {
                await GSMTCSession?.TryStopAsync();
            }
            catch { }
        }

        #endregion
    }
}
