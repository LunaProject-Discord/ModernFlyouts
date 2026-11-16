using System;
using System.IO;
using Microsoft.UI.Xaml;
using ModernFlyouts.WinUI.Audio;
using ModernFlyouts.WinUI.Interop;

namespace ModernFlyouts.WinUI
{
    public partial class App : Application
    {
        private const int VK_VOLUME_MUTE = 0xAD;
        private const int VK_VOLUME_DOWN = 0xAE;
        private const int VK_VOLUME_UP = 0xAF;

        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "ModernFlyouts.WinUI.log");

        private VolumeService? _volume;
        private FlyoutWindow? _flyout;
        private KeyboardHook? _keyboardHook;
        private NativeOsdSuppressor? _osd;

        public App()
        {
            InitializeComponent();
            UnhandledException += (_, e) => Log($"Unhandled: {e.Exception}");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Log("launch");

            _volume = new VolumeService();
            _flyout = new FlyoutWindow(_volume);

            _keyboardHook = new KeyboardHook();
            _keyboardHook.KeyDown += vk =>
            {
                if (vk is VK_VOLUME_MUTE or VK_VOLUME_DOWN or VK_VOLUME_UP)
                {
                    _flyout.ShowFlyout();
                }
            };

            // Covers volume changes that don't come through the keyboard (touchpad, headset buttons).
            _osd = new NativeOsdSuppressor();
            _osd.NativeOsdShown += () => _flyout.DispatcherQueue.TryEnqueue(() => _flyout.ShowFlyout());

            Log("ready");
        }

        internal static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
