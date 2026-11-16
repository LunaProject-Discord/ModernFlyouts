using System;
using System.Threading.Tasks;
using ModernFlyouts.Core.Media.Control;

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
                manager = new GSMTCMediaSessionManager();
                BackendName = "GSMTC (enhanced backend disabled)";
                Attach(manager);
                return;
            }

            var preflight = await MediaControlPreflight.RunAsync();
            if (preflight.IsAvailable)
            {
                manager = new EnhancedGSMTCMediaSessionManager(
                    () => new MediaSessionSelectionOptions(),
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
            manager.CurrentMediaSessionChanged += (_, _) => CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
            manager.MediaSessionsChanged += (_, _) => CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
            manager.MediaSessions.CollectionChanged += (_, _) => CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
            manager.OnEnabled();
            CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Lets the enhanced backend start/stop its timeline ticker.</summary>
        public void RefreshTimelineActivity()
        {
            (_manager as EnhancedGSMTCMediaSessionManager)?.RefreshTimelineActivity();
        }
    }
}
