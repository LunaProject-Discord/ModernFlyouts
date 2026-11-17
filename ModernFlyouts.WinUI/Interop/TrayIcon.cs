using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ModernFlyouts.WinUI.Interop
{
    /// <summary>
    /// Notification area icon using Shell_NotifyIcon directly (no extra dependency).
    /// Messages arrive through the flyout window's <see cref="WindowMessageHook"/>.
    /// Left click opens the settings; right click asks for the (WinUI) context menu.
    /// The icon is re-added when explorer restarts (TaskbarCreated).
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
        private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
        private const uint NOTIFYICON_VERSION_4 = 4;
        private const uint WM_APP_TRAY = 0x8000 + 0x4D; // WM_APP + n
        private const uint WM_CONTEXTMENU = 0x007B;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint NIN_SELECT = 0x0400;
        private const uint NIN_KEYSELECT = 0x0401;
        private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
        private const int SM_CXSMICON = 49;
        private const uint IconId = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATAW data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi(int index, uint dpi);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        private readonly WindowMessageHook _hook;
        private readonly uint _taskbarCreatedMessage;
        private readonly string _tooltip;
        private IntPtr _icon;
        private bool _added;

        public TrayIcon(WindowMessageHook hook, string tooltip)
        {
            _hook = hook;
            _tooltip = tooltip;
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            _hook.MessageReceived += OnMessage;
        }

        public event Action? OpenSettingsRequested;

        /// <summary>Right click (or Shift+F10 / menu key): screen position in physical pixels.</summary>
        public event Action<int, int>? ContextMenuRequested;

        public bool IsVisible
        {
            get => _added;
            set
            {
                if (value) Add();
                else Remove();
            }
        }

        private void Add()
        {
            if (_added)
            {
                return;
            }

            EnsureIcon();

            var data = CreateData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
            data.uCallbackMessage = WM_APP_TRAY;
            data.hIcon = _icon;
            data.szTip = _tooltip;
            _added = Shell_NotifyIcon(NIM_ADD, ref data);
            App.Log($"tray icon added: {_added}");

            if (_added)
            {
                data.uVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIcon(NIM_SETVERSION, ref data);
            }
        }

        private string _iconFile = "Logo.ico";

        /// <summary>File name in Assets\ (Logo.ico, or the monochrome Logo_Tray_Black/White.ico).</summary>
        public string IconFile
        {
            get => _iconFile;
            set
            {
                if (_iconFile == value)
                {
                    return;
                }

                _iconFile = value;
                if (_icon != IntPtr.Zero)
                {
                    DestroyIcon(_icon);
                    _icon = IntPtr.Zero;
                }

                if (_added)
                {
                    EnsureIcon();
                    var data = CreateData();
                    data.uFlags = NIF_ICON;
                    data.hIcon = _icon;
                    Shell_NotifyIcon(NIM_MODIFY, ref data);
                }
            }
        }

        private void EnsureIcon()
        {
            if (_icon == IntPtr.Zero)
            {
                int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
                _icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", _iconFile),
                    IMAGE_ICON, size, size, LR_LOADFROMFILE);
            }
        }

        private void Remove()
        {
            if (!_added)
            {
                return;
            }

            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        private NOTIFYICONDATAW CreateData() => new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hook.Handle,
            uID = IconId,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        private void OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == _taskbarCreatedMessage)
            {
                // Explorer restarted: the old icon is gone.
                bool wasVisible = _added;
                _added = false;
                if (wasVisible) Add();
                return;
            }

            if (msg != WM_APP_TRAY)
            {
                return;
            }

            // NOTIFYICON_VERSION_4: the event is in the low word of lParam.
            uint trayEvent = (uint)((long)lParam & 0xFFFF);
            switch (trayEvent)
            {
                // A left click sends WM_LBUTTONUP and then NIN_SELECT (version 4); react once.
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    OpenSettingsRequested?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    GetCursorPos(out var pt);
                    ContextMenuRequested?.Invoke(pt.X, pt.Y);
                    break;
            }
        }

        public void Dispose()
        {
            Remove();
            _hook.MessageReceived -= OnMessage;
            if (_icon != IntPtr.Zero)
            {
                DestroyIcon(_icon);
                _icon = IntPtr.Zero;
            }
        }
    }
}
