using ModernFlyouts.Core.Threading;
using System;

namespace ModernFlyouts.Core.Media.Control
{
    public sealed class EnhancedGSMTCMediaSessionManager : MediaSessionManager
    {
        private readonly Func<MediaSessionSelectionOptions> getSelectionOptions;
        private readonly Func<bool> isTimelineDisplayActive;
        private MediaSessionService service;
        private EnhancedGSMTCMediaSession displaySession;
        private bool enabled;

        public EnhancedGSMTCMediaSessionManager(
            Func<MediaSessionSelectionOptions> getSelectionOptions,
            Func<bool> isTimelineDisplayActive)
        {
            this.getSelectionOptions = getSelectionOptions;
            this.isTimelineDisplayActive = isTimelineDisplayActive ?? (() => false);
        }

        public void RefreshSelectionOptions()
        {
            service?.UpdateSelectionOptions();
        }

        /// <summary>Every session the service knows about, not only the selected one.</summary>
        public System.Collections.Generic.IReadOnlyList<MediaSessionSnapshot> GetAllSessions()
        {
            return service?.GetSessions() ?? Array.Empty<MediaSessionSnapshot>();
        }

        public void RefreshTimelineActivity()
        {
            displaySession?.RefreshTimelineActivity();
        }

        public override async void OnEnabled()
        {
            if (enabled)
            {
                return;
            }

            enabled = true;
            service = new MediaSessionService(getSelectionOptions);
            service.SelectionChanged += Service_SelectionChanged;
            service.SessionsChanged += Service_SessionsChanged;
            await service.InitializeAsync();
        }

        public override void OnDisabled()
        {
            enabled = false;

            if (service != null)
            {
                service.SelectionChanged -= Service_SelectionChanged;
                service.SessionsChanged -= Service_SessionsChanged;
                service.Dispose();
                service = null;
            }

            UiDispatcher.Invoke(ClearDisplaySession);
        }

        public override bool ContainsAnySession()
        {
            return CurrentMediaSession != null && MediaSessions.Count > 0;
        }

        private void Service_SelectionChanged(object sender, MediaSessionSelection selection)
        {
            if (UiDispatcher.CheckAccess())
            {
                ApplySelection(selection);
            }
            else
            {
                UiDispatcher.Post(() => ApplySelection(selection));
            }
        }

        private void Service_SessionsChanged(object sender, EventArgs e)
        {
            RaiseMediaSessionsChanged();
        }

        private void ApplySelection(MediaSessionSelection selection)
        {
            if (!enabled)
            {
                return;
            }

            if (selection?.Snapshot == null)
            {
                ClearDisplaySession();
                RaiseMediaSessionsChanged();
                return;
            }

            if (displaySession == null)
            {
                displaySession = new EnhancedGSMTCMediaSession(service, selection.Snapshot, isTimelineDisplayActive);
                MediaSessions.Add(displaySession);
                CurrentMediaSession = displaySession;
                displaySession.IsCurrent = true;
                RaiseMediaSessionsChanged();
                return;
            }

            displaySession.ApplySnapshot(selection.Snapshot);
            CurrentMediaSession = displaySession;
            displaySession.IsCurrent = true;
        }

        private void ClearDisplaySession()
        {
            if (displaySession != null)
            {
                displaySession.Disconnect();
                displaySession = null;
            }

            foreach (var session in MediaSessions)
            {
                session.Disconnect();
            }

            MediaSessions.Clear();
            CurrentMediaSession = null;
        }
    }
}
