using System;
using System.Runtime.InteropServices;

namespace ModernFlyouts.WinUI.Interop
{
    /// <summary>
    /// Subclasses a window (comctl32 SetWindowSubclass) and reports every message it receives.
    /// WinUI windows don't expose a WndProc, so this is how shell hook and display change
    /// messages reach the app. Handlers run on the window's (UI) thread.
    /// </summary>
    internal sealed class WindowMessageHook : IDisposable
    {
        private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr refData);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, IntPtr refData);

        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private static readonly UIntPtr SubclassId = new(0x4D46); // "MF"

        private readonly IntPtr _hwnd;
        private readonly SubclassProc _proc; // keep the delegate alive

        public WindowMessageHook(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _proc = WndProc;
            if (!SetWindowSubclass(hwnd, _proc, SubclassId, IntPtr.Zero))
            {
                throw new InvalidOperationException("SetWindowSubclass failed");
            }
        }

        public IntPtr Handle => _hwnd;

        public event Action<uint, IntPtr, IntPtr>? MessageReceived;

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr refData)
        {
            try
            {
                MessageReceived?.Invoke(msg, wParam, lParam);
            }
            catch (Exception ex)
            {
                App.Log($"WndProc handler failed: {ex}");
            }

            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose() => RemoveWindowSubclass(_hwnd, _proc, SubclassId);
    }
}
