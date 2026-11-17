using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModernFlyouts.Core.Media.Control;
using ModernFlyouts.WinUI.Settings;

namespace ModernFlyouts.WinUI.Media
{
    /// <summary>
    /// Picks and runs the media session backend, mirroring the WPF AudioFlyoutHelper:
    /// the enhanced GSMTC backend when its preflight passes, otherwise NowPlaying (NPSM).
    /// All members are used on the UI thread.
    /// </summary>
    public sealed class MediaController
    {
        private readonly Func<bool> _isTimelineDisplayed;
        private MediaSessionManager? _manager;

        public MediaController(Func<bool> isTimelineDisplayed)
        {
            _isTimelineDisplayed = isTimelineDisplayed;
            AppSettings.Instance.PropertyChanged += Settings_PropertyChanged;
        }

        /// <summary>
        /// Every session to show: the selected one first, then the others. The enhanced backend
        /// selects a single session (pinned app / filter rules); GSMTC and NowPlaying list all.
        /// </summary>
        public IReadOnlyList<MediaSession> Sessions
        {
            get
            {
                if (DemoSession != null)
                {
                    return new[] { DemoSession };
                }

                if (_manager == null)
                {
                    return Array.Empty<MediaSession>();
                }

                var current = CurrentSession;
                var list = new List<MediaSession>();
                if (current != null)
                {
                    list.Add(current);
                }
                list.AddRange(_manager.MediaSessions.Where(m => !ReferenceEquals(m, current)));
                return list;
            }
        }

        /// <summary>Media apps seen so far (app id → name), offered as choices for pinning/filtering.</summary>
        public IReadOnlyDictionary<string, string> KnownSources
        {
            get
            {
                if (_manager is EnhancedGSMTCMediaSessionManager enhanced)
                {
                    foreach (var snapshot in enhanced.GetAllSessions())
                    {
                        if (!string.IsNullOrEmpty(snapshot.SourceAppUserModelId))
                        {
                            _knownSources[snapshot.SourceAppUserModelId] = string.IsNullOrEmpty(snapshot.DisplayAppName)
                                ? snapshot.SourceAppUserModelId : snapshot.DisplayAppName;
                        }
                    }
                }
                return _knownSources;
            }
        }

        private readonly Dictionary<string, string> _knownSources = new(StringComparer.OrdinalIgnoreCase);

        private static MediaSessionSelectionOptions CreateSelectionOptions()
        {
            var s = AppSettings.Instance;
            return MediaSessionSelectionOptions.FromDelimitedList(
                s.PinnedMediaAppUserModelId, s.PinnedAppPriorityMode, s.MediaAppFilteringMode, s.MediaAppFilterList);
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.PinnedMediaAppUserModelId):
                case nameof(AppSettings.PinnedAppPriorityMode):
                case nameof(AppSettings.MediaAppFilteringMode):
                case nameof(AppSettings.MediaAppFilterList):
                    (_manager as EnhancedGSMTCMediaSessionManager)?.RefreshSelectionOptions();
                    break;
            }
        }

        /// <summary>Session to display, or null when nothing is playing.</summary>
        public MediaSession? CurrentSession =>
            DemoSession ?? _manager?.CurrentMediaSession ?? (_manager?.MediaSessions.Count > 0 ? _manager.MediaSessions[0] : null);

        /// <summary>Debug builds: a sample session shown instead of the real one (--demo-media).</summary>
        public MediaSession? DemoSession { get; set; }

        public string BackendName { get; private set; } = "none";

        public event EventHandler? CurrentSessionChanged;

        public async Task StartAsync(EnhancedMediaBackendMode mode)
        {
            MediaSessionManager manager;
            if (mode == EnhancedMediaBackendMode.Disabled)
            {
                manager = new GSMTCMediaSessionManager(_isTimelineDisplayed);
                BackendName = "GSMTC (enhanced backend disabled)";
                Attach(manager);
                return;
            }

            var preflight = await MediaControlPreflight.RunAsync();
            if (preflight.IsAvailable)
            {
                manager = new EnhancedGSMTCMediaSessionManager(
                    CreateSelectionOptions,
                    _isTimelineDisplayed);
                BackendName = "enhanced GSMTC";
            }
            else
            {
                manager = new NowPlayingMediaSessionManager();
                BackendName = $"NowPlaying ({preflight.DiagnosticMessage})";
            }

            Attach(manager);
        }

        private void Attach(MediaSessionManager manager)
        {
            _manager = manager;
            manager.CurrentMediaSessionChanged += (_, _) => RaiseChanged();
            manager.MediaSessionsChanged += (_, _) => RaiseChanged();
            manager.MediaSessions.CollectionChanged += (_, _) => RaiseChanged();
            manager.OnEnabled();
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            foreach (var session in Sessions)
            {
                string id = (session as EnhancedGSMTCMediaSession)?.SourceAppUserModelId ?? session.MediaSourceName;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(session.MediaSourceName))
                {
                    _knownSources[id] = session.MediaSourceName;
                }
            }

            CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Lets the backend start/stop its timeline ticker (only while a timeline is shown).</summary>
        public void RefreshTimelineActivity()
        {
            (_manager as EnhancedGSMTCMediaSessionManager)?.RefreshTimelineActivity();
            (_manager as GSMTCMediaSessionManager)?.RefreshTimelineActivity();
        }
    }
}
