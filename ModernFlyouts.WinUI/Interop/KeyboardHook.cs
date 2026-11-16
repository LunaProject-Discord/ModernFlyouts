using System;
using System.Runtime.InteropServices;

namespace ModernFlyouts.WinUI.Interop
{
    /// <summary>
    /// Global low-level keyboard hook. Must be created on a thread with a message loop
    /// (the WinUI UI thread). Only reports key-down; never blocks the key.
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        private readonly Native.LowLevelKeyboardProc _proc;
        private IntPtr _hook;

        public event Action<int>? KeyDown;

        public KeyboardHook()
        {
            _proc = HookProc;
            _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
            }
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == Native.WM_KEYDOWN || wParam == Native.WM_SYSKEYDOWN))
            {
                var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                KeyDown?.Invoke((int)data.vkCode);
            }

            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
