using System;

namespace ModernFlyouts.WinUI.Interop
{
    /// <summary>
    /// Hides the shell's own volume/brightness OSD (Windows 11 22H2+ host window).
    /// Same detection as ModernFlyouts.Core's NativeFlyoutHandler: an explorer-owned
    /// XamlExplorerHostIslandWindow in the AboveLockUX band.
    /// </summary>
    internal sealed class NativeOsdSuppressor : IDisposable
    {
        private const string HostClass = "XamlExplorerHostIslandWindow";
        private const string ContentClass = "Windows.UI.Composition.DesktopWindowContentBridge";
        private const string ContentName = "DesktopWindowXamlSource";

        private readonly Native.WinEventProc _proc;
        private IntPtr _hook;
        private IntPtr _host;

        /// <summary>Raised when the shell showed its OSD (after it has been hidden).</summary>
        public event Action? NativeOsdShown;

        public bool IsSuppressing { get; set; } = true;

        public NativeOsdSuppressor()
        {
            _proc = OnWinEvent;
            Native.GetWindowThreadProcessId(Native.GetShellWindow(), out int shellPid);
            _hook = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, IntPtr.Zero, _proc,
                (uint)shellPid, 0, Native.WINEVENT_OUTOFCONTEXT);
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero)
            {
                return;
            }

            if (hwnd != _host && !IsOsdHost(hwnd))
            {
                return;
            }

            _host = hwnd;
            if (IsSuppressing)
            {
                Hide();
            }

            NativeOsdShown?.Invoke();
        }

        public void Hide()
        {
            if (_host == IntPtr.Zero)
            {
                return;
            }

            var content = Native.FindWindowEx(_host, IntPtr.Zero, ContentClass, ContentName);
            if (content != IntPtr.Zero)
            {
                Native.ShowWindowAsync(content, Native.SW_MINIMIZE);
            }

            Native.ShowWindowAsync(_host, Native.SW_HIDE);
        }

        private static bool IsOsdHost(IntPtr hwnd)
        {
            if (Native.GetClassName(hwnd) != HostClass)
            {
                return false;
            }

            if (Native.FindWindowEx(hwnd, IntPtr.Zero, ContentClass, ContentName) == IntPtr.Zero)
            {
                return false;
            }

            return Native.GetWindowBand(hwnd, out uint band) && band == Native.ZBID_ABOVELOCK_UX;
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
