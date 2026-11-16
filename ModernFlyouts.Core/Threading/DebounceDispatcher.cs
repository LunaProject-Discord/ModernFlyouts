using System;
using System.Threading;

namespace ModernFlyouts.Core.Threading
{
    /// <summary>
    /// Runs the last action passed to <see cref="Debounce"/> on the UI thread once
    /// <c>interval</c> has elapsed without another call.
    /// </summary>
    public class DebounceDispatcher : IDisposable
    {
        private readonly Timer timer;

        private Action _action;

        public DebounceDispatcher()
        {
            timer = new Timer(_ => UiDispatcher.Post(() => _action?.Invoke()), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Debounce(TimeSpan interval, Action action)
        {
            _action = action;
            timer.Change(interval, Timeout.InfiniteTimeSpan);
        }

        public void Dispose() => timer.Dispose();
    }
}
