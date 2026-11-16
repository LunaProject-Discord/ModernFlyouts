using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Xaml;
using ModernFlyouts.Core.Interop;
using ModernFlyouts.Core.Threading;
using ModernFlyouts.WinUI.Audio;
using ModernFlyouts.WinUI.Media;

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
        private MediaController? _media;

        public App()
        {
            InitializeComponent();
            UnhandledException += (_, e) => Log($"Unhandled: {e.Exception}");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Log("launch");

            // Core marshals media/brightness updates to the UI thread through this.
            UiDispatcher.Initialize(SynchronizationContext.Current
                ?? throw new InvalidOperationException("No UI SynchronizationContext"));

            _volume = new VolumeService();
            _flyout = new FlyoutWindow(_volume);

            _media = new MediaController(() => _flyout.IsTimelineDisplayed);
            _flyout.AttachMedia(_media);
            StartMediaAsync();

            _keyboardHook = new KeyboardHook();
            _keyboardHook.KeyDown += vk =>
            {
                if (vk is VK_VOLUME_MUTE or VK_VOLUME_DOWN or VK_VOLUME_UP)
                {
                    _flyout.ShowFlyout();
                }
            };

            // Hides the shell OSD and covers volume changes that don't come through the
            // keyboard (touchpad, headset buttons). Re-hooks after explorer restarts.
            NativeFlyoutHandler.Instance = new NativeFlyoutHandler();
            NativeFlyoutHandler.Instance.Initialize();
            NativeFlyoutHandler.Instance.NativeFlyoutShown += (_, _) =>
            {
                NativeFlyoutHandler.Instance.HideNativeFlyout();
                _flyout.ShowFlyout();
            };

            Log("ready");
        }

        private async void StartMediaAsync()
        {
            try
            {
                await _media!.StartAsync();
                Log($"media backend: {_media.BackendName}");
            }
            catch (Exception ex)
            {
                Log($"media backend failed: {ex}");
            }
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
