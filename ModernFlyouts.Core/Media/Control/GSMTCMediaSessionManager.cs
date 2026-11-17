using System;
using System.Collections.Generic;
using ModernFlyouts.Core.Threading;
using Windows.Media.Control;

namespace ModernFlyouts.Core.Media.Control
{
    public class GSMTCMediaSessionManager : MediaSessionManager
    {
        private GlobalSystemMediaTransportControlsSessionManager GSMTCSessionManager;
        private readonly Dictionary<string, GSMTCMediaSession> sessionsByAppId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Func<bool> isTimelineDisplayActive;

        /// <param name="isTimelineDisplayActive">Whether a timeline is on screen (null = always).</param>
        public GSMTCMediaSessionManager(Func<bool> isTimelineDisplayActive = null)
        {
            this.isTimelineDisplayActive = isTimelineDisplayActive;
        }

        /// <summary>The timeline was shown or hidden: sessions start/stop their position ticker.</summary>
        public void RefreshTimelineActivity()
        {
            foreach (var session in sessionsByAppId.Values)
            {
                session.RefreshTimelineActivity();
            }
        }

        public override async void OnEnabled()
        {
            try
            {
                GSMTCSessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                GSMTCSessionManager.SessionsChanged += GSMTCSessionsChanged;
                GSMTCSessionManager.CurrentSessionChanged += GSMTCCurrentSessionChanged;

                LoadSessions();
            }
            catch { }
        }

        private void GSMTCSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            UiDispatcher.Invoke(LoadSessions);
        }

        private void GSMTCCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            UiDispatcher.Invoke(UpdateCurrentSession);
        }

        private void UpdateCurrentSession()
        {
            string currentAppId = GSMTCSessionManager?.GetCurrentSession()?.SourceAppUserModelId;
            MediaSession current = null;

            foreach (var pair in sessionsByAppId)
            {
                bool isCurrent = string.Equals(pair.Key, currentAppId, StringComparison.OrdinalIgnoreCase);
                pair.Value.IsCurrent = isCurrent;
                if (isCurrent)
                {
                    current = pair.Value;
                }
            }

            CurrentMediaSession = current;
        }

        private void ClearSessions()
        {
            foreach (var session in MediaSessions)
            {
                session.Disconnect();
            }

            MediaSessions.Clear();
            sessionsByAppId.Clear();
            CurrentMediaSession = null;
        }

        private void LoadSessions()
        {
            ClearSessions();

            if (GSMTCSessionManager != null)
            {
                var sessions = GSMTCSessionManager.GetSessions();

                foreach (var session in sessions)
                {
                    var mediaSession = new GSMTCMediaSession(session, isTimelineDisplayActive);
                    MediaSessions.Add(mediaSession);
                    sessionsByAppId[session.SourceAppUserModelId ?? string.Empty] = mediaSession;
                }
            }

            UpdateCurrentSession();
            RaiseMediaSessionsChanged();
        }

        public override void OnDisabled()
        {
            try
            {
                if (GSMTCSessionManager != null)
                {
                    GSMTCSessionManager.SessionsChanged -= GSMTCSessionsChanged;
                    GSMTCSessionManager.CurrentSessionChanged -= GSMTCCurrentSessionChanged;
                    GSMTCSessionManager = null;
                }

                ClearSessions();
                RaiseMediaSessionsChanged();
            }
            catch { }
        }
    }
}
