using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ModernFlyouts.WinUI.Interop
{
    /// <summary>
    /// Notification area icon using Shell_NotifyIcon directly (no extra dependency).
    /// Messages arrive through the flyout window's <see cref="WindowMessageHook"/>.
    /// Left click opens the settings; right click shows a native context menu.
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
        private const uint WM_NULL = 0x0000;
        private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
        private const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002, TPM_BOTTOMALIGN = 0x0020;
        private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
        private const int SM_CXSMICON = 49;
        private const uint IconId = 1;
        private const int CmdSettings = 1, CmdExit = 2;

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
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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

        public event Action? ExitRequested;

        public string SettingsText { get; set; } = "Settings";

        public string ExitText { get; set; } = "Exit";

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

            if (_icon == IntPtr.Zero)
            {
                int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
                _icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.ico"),
                    IMAGE_ICON, size, size, LR_LOADFROMFILE);
            }

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
                case WM_LBUTTONUP:
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    OpenSettingsRequested?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    ShowContextMenu();
                    break;
            }
        }

        private void ShowContextMenu()
        {
            IntPtr menu = CreatePopupMenu();
            try
            {
                AppendMenu(menu, MF_STRING, (UIntPtr)CmdSettings, SettingsText);
                AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                AppendMenu(menu, MF_STRING, (UIntPtr)CmdExit, ExitText);

                GetCursorPos(out var pt);
                // Required so the menu closes when clicking elsewhere (KB135788).
                SetForegroundWindow(_hook.Handle);
                int command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.X, pt.Y, _hook.Handle, IntPtr.Zero);
                PostMessage(_hook.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

                if (command == CmdSettings) OpenSettingsRequested?.Invoke();
                else if (command == CmdExit) ExitRequested?.Invoke();
            }
            finally
            {
                DestroyMenu(menu);
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
