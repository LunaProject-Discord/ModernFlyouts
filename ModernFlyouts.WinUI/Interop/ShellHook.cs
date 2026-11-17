using System;
using System.Runtime.InteropServices;

namespace ModernFlyouts.WinUI.Interop
{
    /// <summary>
    /// Receives the shell's system key notifications (same mechanism as the WPF
    /// ShellMessageHookHandler): volume/media keys arrive as HSHELL_APPCOMMAND and
    /// brightness keys as the undocumented code 55, from keyboards, headsets and remotes alike.
    /// </summary>
    internal sealed class ShellHook : IDisposable
    {
        private const int HSHELL_APPCOMMAND = 12;
        private const int HSHELL_BRIGHTNESS = 55;

        // lParam values (APPCOMMAND << 16) used by the original implementation.
        private const long APPCOMMAND_VOLUME_MUTE = 524288;
        private const long APPCOMMAND_VOLUME_DOWN = 589824;
        private const long APPCOMMAND_VOLUME_UP = 655360;
        private const long APPCOMMAND_MEDIA_NEXT = 720896;
        private const long APPCOMMAND_MEDIA_PREVIOUS = 786432;
        private const long APPCOMMAND_MEDIA_STOP = 851968;
        private const long APPCOMMAND_MEDIA_PLAY_PAUSE = 917504;

        [DllImport("user32.dll")]
        private static extern bool RegisterShellHookWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool DeregisterShellHookWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string message);

        private readonly WindowMessageHook _hook;
        private readonly uint _shellHookMessage;

        public ShellHook(WindowMessageHook hook)
        {
            _hook = hook;
            _shellHookMessage = RegisterWindowMessage("SHELLHOOK");
            RegisterShellHookWindow(hook.Handle);
            hook.MessageReceived += OnMessage;
        }

        public event Action? VolumeKey;

        public event Action? MediaKey;

        public event Action? BrightnessKey;

        private void OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg != _shellHookMessage)
            {
                return;
            }

            if ((long)wParam is HSHELL_BRIGHTNESS or HSHELL_APPCOMMAND)
            {
                App.Log($"shellhook w={(long)wParam} l={(long)lParam}");
            }

            switch ((long)wParam)
            {
                case HSHELL_BRIGHTNESS:
                    BrightnessKey?.Invoke();
                    break;

                case HSHELL_APPCOMMAND:
                    switch ((long)lParam)
                    {
                        case APPCOMMAND_VOLUME_MUTE:
                        case APPCOMMAND_VOLUME_DOWN:
                        case APPCOMMAND_VOLUME_UP:
                            VolumeKey?.Invoke();
                            break;
                        case APPCOMMAND_MEDIA_NEXT:
                        case APPCOMMAND_MEDIA_PREVIOUS:
                        case APPCOMMAND_MEDIA_STOP:
                        case APPCOMMAND_MEDIA_PLAY_PAUSE:
                            MediaKey?.Invoke();
                            break;
                    }
                    break;
            }
        }

        public void Dispose()
        {
            _hook.MessageReceived -= OnMessage;
            DeregisterShellHookWindow(_hook.Handle);
        }
    }
}
