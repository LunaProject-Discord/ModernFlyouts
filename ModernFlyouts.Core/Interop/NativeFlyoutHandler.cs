using System;
using System.Diagnostics;
using System.Threading;
using ModernFlyouts.Core.Threading;
using static ModernFlyouts.Core.Interop.NativeMethods;

namespace ModernFlyouts.Core.Interop
{
    public class NativeFlyoutHandler : IWndProcObject
    {
        #region Constants

        private const uint WINEVENT_OUTOFCONTEXT = 0x0000; // Events are ASYNC

        private const uint WINEVENT_SKIPOWNTHREAD = 0x0001; // Don't call back for events on installer's thread

        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002; // Don't call back for events on installer's process

        private const uint WINEVENT_INCONTEXT = 0x0004; // Events are SYNC, this causes your dll to be injected into every process

        private const uint EVENT_MIN = 0x00000001;

        private const uint EVENT_MAX = 0x7FFFFFFF;

        private const uint EVENT_SYSTEM_SOUND = 0x0001;

        private const uint EVENT_SYSTEM_ALERT = 0x0002;

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

        private const uint EVENT_SYSTEM_MENUSTART = 0x0004;

        private const uint EVENT_SYSTEM_MENUEND = 0x0005;

        private const uint EVENT_SYSTEM_MENUPOPUPSTART = 0x0006;

        private const uint EVENT_SYSTEM_MENUPOPUPEND = 0x0007;

        private const uint EVENT_SYSTEM_CAPTURESTART = 0x0008;

        private const uint EVENT_SYSTEM_CAPTUREEND = 0x0009;

        private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;

        private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;

        private const uint EVENT_SYSTEM_CONTEXTHELPSTART = 0x000C;

        private const uint EVENT_SYSTEM_CONTEXTHELPEND = 0x000D;

        private const uint EVENT_SYSTEM_DRAGDROPSTART = 0x000E;

        private const uint EVENT_SYSTEM_DRAGDROPEND = 0x000F;

        private const uint EVENT_SYSTEM_DIALOGSTART = 0x0010;

        private const uint EVENT_SYSTEM_DIALOGEND = 0x0011;

        private const uint EVENT_SYSTEM_SCROLLINGSTART = 0x0012;

        private const uint EVENT_SYSTEM_SCROLLINGEND = 0x0013;

        private const uint EVENT_SYSTEM_SWITCHSTART = 0x0014;

        private const uint EVENT_SYSTEM_SWITCHEND = 0x0015;

        private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;

        private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;

        private const uint EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;

        private const uint EVENT_SYSTEM_END = 0x00FF;

        private const uint EVENT_OEM_DEFINED_START = 0x0101;

        private const uint EVENT_OEM_DEFINED_END = 0x01FF;

        private const uint EVENT_UIA_EVENTID_START = 0x4E00;

        private const uint EVENT_UIA_EVENTID_END = 0x4EFF;

        private const uint EVENT_UIA_PROPID_START = 0x7500;

        private const uint EVENT_UIA_PROPID_END = 0x75FF;

        private const uint EVENT_CONSOLE_CARET = 0x4001;

        private const uint EVENT_CONSOLE_UPDATE_REGION = 0x4002;

        private const uint EVENT_CONSOLE_UPDATE_SIMPLE = 0x4003;

        private const uint EVENT_CONSOLE_UPDATE_SCROLL = 0x4004;

        private const uint EVENT_CONSOLE_LAYOUT = 0x4005;

        private const uint EVENT_CONSOLE_START_APPLICATION = 0x4006;

        private const uint EVENT_CONSOLE_END_APPLICATION = 0x4007;

        private const uint EVENT_CONSOLE_END = 0x40FF;

        private const uint EVENT_OBJECT_CREATE = 0x8000; // hwnd ID idChild is created item

        private const uint EVENT_OBJECT_DESTROY = 0x8001; // hwnd ID idChild is destroyed item

        private const uint EVENT_OBJECT_SHOW = 0x8002; // hwnd ID idChild is shown item

        private const uint EVENT_OBJECT_HIDE = 0x8003; // hwnd ID idChild is hidden item

        private const uint EVENT_OBJECT_REORDER = 0x8004; // hwnd ID idChild is parent of zordering children

        private const uint EVENT_OBJECT_FOCUS = 0x8005; // hwnd ID idChild is focused item

        private const uint EVENT_OBJECT_SELECTION = 0x8006; // hwnd ID idChild is selected item (if only one), or idChild is OBJID_WINDOW if complex

        private const uint EVENT_OBJECT_SELECTIONADD = 0x8007; // hwnd ID idChild is item added

        private const uint EVENT_OBJECT_SELECTIONREMOVE = 0x8008; // hwnd ID idChild is item removed

        private const uint EVENT_OBJECT_SELECTIONWITHIN = 0x8009; // hwnd ID idChild is parent of changed selected items

        private const uint EVENT_OBJECT_STATECHANGE = 0x800A; // hwnd ID idChild is item w/ state change

        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B; // hwnd ID idChild is moved/sized item

        private const uint EVENT_OBJECT_NAMECHANGE = 0x800C; // hwnd ID idChild is item w/ name change

        private const uint EVENT_OBJECT_DESCRIPTIONCHANGE = 0x800D; // hwnd ID idChild is item w/ desc change

        private const uint EVENT_OBJECT_VALUECHANGE = 0x800E; // hwnd ID idChild is item w/ value change

        private const uint EVENT_OBJECT_PARENTCHANGE = 0x800F; // hwnd ID idChild is item w/ new parent

        private const uint EVENT_OBJECT_HELPCHANGE = 0x8010; // hwnd ID idChild is item w/ help change

        private const uint EVENT_OBJECT_DEFACTIONCHANGE = 0x8011; // hwnd ID idChild is item w/ def action change

        private const uint EVENT_OBJECT_ACCELERATORCHANGE = 0x8012; // hwnd ID idChild is item w/ keybd accel change

        private const uint EVENT_OBJECT_INVOKED = 0x8013; // hwnd ID idChild is item invoked

        private const uint EVENT_OBJECT_TEXTSELECTIONCHANGED = 0x8014; // hwnd ID idChild is item w? test selection change

        private const uint EVENT_OBJECT_CONTENTSCROLLED = 0x8015;

        private const uint EVENT_SYSTEM_ARRANGMENTPREVIEW = 0x8016;

        private const uint EVENT_OBJECT_END = 0x80FF;

        private const uint EVENT_AIA_START = 0xA000;

        private const uint EVENT_AIA_END = 0xAFFF;

        #endregion

        #region Properties

        public static NativeFlyoutHandler Instance { get; set; }

        public IntPtr HWndHost { get; private set; } = IntPtr.Zero;

        public IntPtr HWndDUI { get; private set; } = IntPtr.Zero;

        public uint ShellProcessId { get; private set; }

        #endregion

        // Windows 11 22H2 moved the OSD into a XAML island host.
        private static readonly bool IsXamlOsd = Environment.OSVersion.Version.Build >= 22620;
        private static readonly string HostClass = IsXamlOsd ? "XamlExplorerHostIslandWindow" : "NativeHWNDHost";
        private static readonly string HostName = IsXamlOsd ? null : "";
        private static readonly string DuiClass = IsXamlOsd ? "Windows.UI.Composition.DesktopWindowContentBridge" : "DirectUIHWND";
        private static readonly string DuiName = IsXamlOsd ? "DesktopWindowXamlSource" : "";

        private static readonly TimeSpan ShellCheckInterval = TimeSpan.FromSeconds(5);

        private bool _hasNativeFlyoutCreated;

        private WndProcHookManager hookManager;
        private WinEventDelegate _procDelegate;
        private IntPtr HHookID = IntPtr.Zero;

        // Periodic check (on the UI thread) that the hook still targets the running shell and
        // the OSD host still exists: explorer can restart or recreate the OSD without the hook
        // seeing a destroy event, and the OSD host is only created the first time it is needed.
        private Timer shellCheckTimer;

        public event EventHandler NativeFlyoutShown;

        public NativeFlyoutHandler()
        {
            _procDelegate = new WinEventDelegate(WinEventProc);
            hookManager = WndProcHookManager.RegisterForIWndProcObject(this);
        }

        public void Initialize()
        {
            _hasNativeFlyoutCreated = GetAllInfos();
            Debug.WriteLine($"{nameof(NativeFlyoutHandler)}: Native Flyout Host has been created - {_hasNativeFlyoutCreated}");
            if (!_hasNativeFlyoutCreated)
            {
                ShellProcessId = GetShellProcessId();
            }
            Hook();

            shellCheckTimer = new Timer(_ => UiDispatcher.Post(CheckShell), null, ShellCheckInterval, ShellCheckInterval);
        }

        #region Native Flyout Hooking

        private void Hook()
        {
            if (ShellProcessId == 0)
            {
                return; // explorer is not running yet; CheckShell hooks it once it is
            }

            HHookID = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_STATECHANGE, IntPtr.Zero, _procDelegate, ShellProcessId, 0, WINEVENT_OUTOFCONTEXT);
            Debug.WriteLine(nameof(NativeFlyoutHandler) + (HHookID == IntPtr.Zero ? ": Could not set Native Flyout hook" : ": Hooked!"));
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || idChild != 0)
            {
                return;
            }

            // A new OSD host (created on first use, or recreated by the shell): take it over.
            if (hWnd != HWndHost
                && (eventType == EVENT_OBJECT_CREATE || eventType == EVENT_OBJECT_SHOW)
                && GetWindowClassName(hWnd) == HostClass
                && TryGetFlyoutHost(hWnd, false, out IntPtr hWndDUI, out uint pid))
            {
                SetHost(hWnd, hWndDUI, pid);
                hookManager.OnHwndCreated(hWnd);
                Debug.WriteLine(nameof(NativeFlyoutHandler) + ": Native Flyout Host found!");
            }

            if (hWnd == HWndHost)
            {
                switch (eventType)
                {
                    case EVENT_OBJECT_SHOW:
                        OnNativeFlyoutShown();
                        break;

                    case EVENT_OBJECT_DESTROY:
                        OnNativeFlyoutDestroyed();
                        break;

                    case EVENT_OBJECT_HIDE:
                        OnNativeFlyoutHidden();
                        break;

                    default:
                        break;
                }
            }
        }

        private IntPtr wndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            var result = hookManager.TryHandleWindowMessage(hWnd, msg, wParam, lParam, out bool handled);

            if (handled)
            {
                return result;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void OnNativeFlyoutShown()
        {
            Debug.WriteLine(nameof(NativeFlyoutHandler) + ": Native Flyout Shown!");
            NativeFlyoutShown?.Invoke(this, null);
        }

        private void OnNativeFlyoutHidden()
        {
            Debug.WriteLine(nameof(NativeFlyoutHandler) + ": Native Flyout Hidden!");
        }

        private void OnNativeFlyoutDestroyed()
        {
            Debug.WriteLine(nameof(NativeFlyoutHandler) + ": Native Flyout Destroyed!");

            // The hook stays: the shell creates a new host, which WinEventProc picks up.
            ClearHost();
        }

        private void Rehook()
        {
            Unhook();
            Hook();

            Debug.WriteLine(nameof(NativeFlyoutHandler) + ": Rehooked!");
        }

        private void Unhook()
        {
            if (HHookID != IntPtr.Zero)
            {
                UnhookWinEvent(HHookID);
                HHookID = IntPtr.Zero;
                Debug.WriteLine(nameof(NativeFlyoutHandler) + ": Unhooked!");
            }
        }

        private void CheckShell()
        {
            uint shellPid = GetShellProcessId();
            if (shellPid == 0)
            {
                return; // explorer is (re)starting
            }

            if (shellPid != ShellProcessId || HHookID == IntPtr.Zero)
            {
                // explorer restarted: the hook and the OSD host went away with the old process.
                Debug.WriteLine(nameof(NativeFlyoutHandler) + ": Shell process changed");
                ClearHost();
                ShellProcessId = shellPid;
                Rehook();
                _hasNativeFlyoutCreated = GetAllInfos();
                return;
            }

            if (_hasNativeFlyoutCreated && !IsWindow(HWndHost))
            {
                ClearHost();
                _hasNativeFlyoutCreated = GetAllInfos();
            }
        }

        #endregion

        #region Da agjhtual mnadive phlyout ghantling

        private static uint GetShellProcessId()
        {
            IntPtr hWndShell = GetShellWindow();
            if (hWndShell == IntPtr.Zero)
            {
                return 0;
            }

            GetWindowThreadProcessId(hWndShell, out int pid);
            return (uint)pid;
        }

        private static bool IsExplorerProcess(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false; // the process has exited
            }
        }

        /// <summary>Whether <paramref name="hWndHost"/> is the shell's OSD host (in the band above the lock screen).</summary>
        private static bool TryGetFlyoutHost(IntPtr hWndHost, bool checkProcessName, out IntPtr hWndDUI, out uint processId)
        {
            processId = 0;
            hWndDUI = FindWindowEx(hWndHost, IntPtr.Zero, DuiClass, DuiName);
            if (hWndDUI == IntPtr.Zero)
            {
                return false;
            }

            GetWindowThreadProcessId(hWndHost, out int pid);
            if (checkProcessName && !IsExplorerProcess(pid))
            {
                return false;
            }

            GetWindowBand(hWndHost, out UIntPtr pdwBand);
            if (pdwBand != (UIntPtr)ZBandID.AboveLockUX)
            {
                return false;
            }

            processId = (uint)pid;
            return true;
        }

        private void SetHost(IntPtr hWndHost, IntPtr hWndDUI, uint processId)
        {
            HWndHost = hWndHost;
            HWndDUI = hWndDUI;
            ShellProcessId = processId;
            _hasNativeFlyoutCreated = true;
        }

        private void ClearHost()
        {
            HWndHost = IntPtr.Zero;
            HWndDUI = IntPtr.Zero;
            _hasNativeFlyoutCreated = false;
        }

        private bool GetAllInfos()
        {
            IntPtr hWndHost = IntPtr.Zero;
            while ((hWndHost = FindWindowEx(IntPtr.Zero, hWndHost, HostClass, HostName)) != IntPtr.Zero)
            {
                if (TryGetFlyoutHost(hWndHost, true, out IntPtr hWndDUI, out uint pid))
                {
                    SetHost(hWndHost, hWndDUI, pid);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Keeps the known host while it is valid (it is the one just shown), else searches again.</summary>
        private bool RefreshHost()
        {
            if (HWndHost != IntPtr.Zero && IsWindow(HWndHost)
                && TryGetFlyoutHost(HWndHost, false, out IntPtr hWndDUI, out uint pid))
            {
                SetHost(HWndHost, hWndDUI, pid);
                return true;
            }

            ClearHost();
            return GetAllInfos();
        }

        public void HideNativeFlyout()
        {
            if (RefreshHost())
            {
                ShowWindowAsync(HWndDUI, ShowWindowCommands.Minimize);
                ShowWindowAsync(HWndHost, ShowWindowCommands.Hide);
            }
        }

        public void PermanentlyHideNativeFlyout()
        {
            if (_hasNativeFlyoutCreated)
            {
                ShowWindowAsync(HWndDUI, ShowWindowCommands.Minimize);
                ShowWindowAsync(HWndHost, ShowWindowCommands.Minimize);
                ShowWindowAsync(HWndHost, ShowWindowCommands.ForceMinimize);
            }
        }

        public void ShowNativeFlyout()
        {
            if (_hasNativeFlyoutCreated)
            {
                ShowWindowAsync(HWndDUI, ShowWindowCommands.Restore);
            }
        }

        public void VerifyNativeFlyoutCreated()
        {
            CheckShell();
            if (!_hasNativeFlyoutCreated)
            {
                _hasNativeFlyoutCreated = GetAllInfos();
            }
        }

        #endregion

        ~NativeFlyoutHandler()
        {
            Unhook();
        }
    }
}
