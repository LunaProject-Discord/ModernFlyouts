using System;
using System.Threading;

namespace ModernFlyouts.Core.Threading
{
    /// <summary>
    /// Repeating timer whose <see cref="Tick"/> is raised on the UI thread
    /// (UI-framework independent replacement for WPF's DispatcherTimer).
    /// </summary>
    public sealed class UiTimer : IDisposable
    {
        private readonly Timer timer;
        private int generation;

        public UiTimer()
        {
            timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

        public bool IsEnabled { get; private set; }

        public event EventHandler Tick;

        public void Start()
        {
            IsEnabled = true;
            Interlocked.Increment(ref generation);
            timer.Change(Interval, Interval);
        }

        public void Stop()
        {
            IsEnabled = false;
            Interlocked.Increment(ref generation);
            timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void OnTimer(object state)
        {
            int current = Volatile.Read(ref generation);
            UiDispatcher.Post(() =>
            {
                // Drop ticks queued before a Stop()/restart.
                if (IsEnabled && current == Volatile.Read(ref generation))
                {
                    Tick?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        public void Dispose() => timer.Dispose();
    }
}
