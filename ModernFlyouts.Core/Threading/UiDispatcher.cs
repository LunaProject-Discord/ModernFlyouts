using System;
using System.Threading;
using System.Threading.Tasks;

namespace ModernFlyouts.Core.Threading
{
    /// <summary>
    /// UI-framework independent access to the UI thread. The host app calls
    /// <see cref="Initialize"/> on its UI thread at startup (WinUI: after the
    /// DispatcherQueueSynchronizationContext is installed).
    /// Before initialization every call runs inline, which keeps unit tests simple.
    /// </summary>
    public static class UiDispatcher
    {
        private static SynchronizationContext? _context;
        private static int _uiThreadId;

        public static bool IsInitialized => _context != null;

        public static void Initialize(SynchronizationContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _uiThreadId = Environment.CurrentManagedThreadId;
        }

        public static bool CheckAccess() => _context == null || Environment.CurrentManagedThreadId == _uiThreadId;

        /// <summary>Queues <paramref name="action"/> on the UI thread (runs inline before initialization).</summary>
        public static void Post(Action action)
        {
            if (_context == null)
            {
                action();
                return;
            }

            _context.Post(static state => ((Action)state!)(), action);
        }

        /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it.</summary>
        public static void Invoke(Action action)
        {
            if (CheckAccess())
            {
                action();
                return;
            }

            // WinUI's DispatcherQueueSynchronizationContext does not support Send, so post and wait.
            Exception? error = null;
            using var done = new ManualResetEventSlim(false);
            Post(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait();

            if (error != null)
            {
                throw new AggregateException(error);
            }
        }

        public static T Invoke<T>(Func<T> func)
        {
            T result = default!;
            Invoke(() => result = func());
            return result;
        }

        public static Task InvokeAsync(Action action)
        {
            if (CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try { action(); tcs.SetResult(); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }
}
