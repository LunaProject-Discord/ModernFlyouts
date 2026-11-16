using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using ModernFlyouts.Core.Display;
using ModernFlyouts.Core.Interop;
using ModernFlyouts.Core.Threading;
using ModernFlyouts.Core.Utilities;
using ModernFlyouts.Properties;
using ModernFlyouts.WinUI.Audio;
using ModernFlyouts.WinUI.Interop;
using ModernFlyouts.WinUI.Media;
using ModernFlyouts.WinUI.Settings;

namespace ModernFlyouts.WinUI
{
    public partial class App : Application
    {
        private const string InstanceKey = "ModernFlyouts.WinUI";

        private const int VK_INSERT = 0x2D;
        private const int VK_CAPITAL = 0x14;
        private const int VK_NUMLOCK = 0x90;
        private const int VK_SCROLL = 0x91;

        private static readonly TimeSpan FallbackVolumeChangeWindow = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan FallbackVolumeChangeGrace = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan ShellTriggerWindow = TimeSpan.FromMilliseconds(1000);

        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "ModernFlyouts.WinUI.log");

        private VolumeService? _volume;
        private FlyoutWindow? _flyout;
        private SettingsWindow? _settingsWindow;
        private KeyboardHook? _keyboardHook;
        private MediaController? _media;
        private WindowMessageHook? _messageHook;
        private ShellHook? _shellHook;
        private AirplaneModeWatcher? _airplaneModeWatcher;
        private int? _pressedLockKey;
        private long _lastShellTriggerTick;
        private Guid? _lastVirtualDesktopId;

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

        public App()
        {
            InitializeComponent();
            UnhandledException += (_, e) => Log($"Unhandled: {e.Exception}");
        }

        private static AppSettings S => AppSettings.Instance;

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Single instance: a second launch asks the running instance to open its settings.
            var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (!mainInstance.IsCurrent)
            {
                await mainInstance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
                Exit();
                return;
            }

            Log("launch");

            // Core marshals media/brightness updates to the UI thread through this.
            UiDispatcher.Initialize(SynchronizationContext.Current
                ?? throw new InvalidOperationException("No UI SynchronizationContext"));

            mainInstance.Activated += (_, _) => UiDispatcher.Post(ShowSettings);

            DisplayManager.Initialize();
            TryRun("brightness", BrightnessManager.Initialize);

            _volume = new VolumeService();
            _flyout = new FlyoutWindow(_volume);

            _media = new MediaController(() => _flyout.IsTimelineDisplayed);
            _flyout.AttachMedia(_media);
            StartMediaAsync();

            // Shell hook: volume/media/brightness keys from any device; display changes for DisplayManager.
            _messageHook = new WindowMessageHook(_flyout.Handle);
            _messageHook.MessageReceived += (msg, w, l) => DisplayManager.Instance?.OnWndProc(_flyout.Handle, msg, w, l);
            _shellHook = new ShellHook(_messageHook);
            _shellHook.VolumeKey += () => ShowFromShell(S.AudioModuleEnabled, _flyout.ShowVolume);
            _shellHook.MediaKey += () => ShowFromShell(S.AudioModuleEnabled, _flyout.ShowVolume);
            _shellHook.BrightnessKey += () => ShowFromShell(S.BrightnessModuleEnabled, _flyout.ShowBrightness);

            // Lock keys (the shell doesn't report them).
            _keyboardHook = new KeyboardHook();
            _keyboardHook.KeyDown += OnKeyDown;
            _keyboardHook.KeyUp += vk =>
            {
                if (_pressedLockKey == vk) _pressedLockKey = null;
            };

            _airplaneModeWatcher = new AirplaneModeWatcher();
            _airplaneModeWatcher.Changed += (_, e) => UiDispatcher.Post(() =>
            {
                if (CanShowOurFlyouts && S.AirplaneModeModuleEnabled)
                {
                    _flyout.ShowInfo(
                        e.IsEnabled ? "" : "", // Airplane / SignalBars
                        e.IsEnabled ? Strings.AirplaneModeOn : Strings.AirplaneModeOff);
                }
            });
            TryRun("airplane mode watcher", _airplaneModeWatcher.Start);

            // Hides the shell OSD; also covers triggers the shell hook doesn't report
            // (virtual desktop switcher, touchpad/headset volume). Re-hooks after explorer restarts.
            NativeFlyoutHandler.Instance = new NativeFlyoutHandler();
            NativeFlyoutHandler.Instance.Initialize();
            NativeFlyoutHandler.Instance.NativeFlyoutShown += (_, _) => OnNativeFlyoutShown();
            _lastVirtualDesktopId = VirtualDesktopManager.Instance.TryGetCurrentDesktop()?.Id;

            if (Environment.GetCommandLineArgs() is { Length: > 1 } cmd && cmd[1] == "--settings")
            {
                ShowSettings();
            }

            Log("ready");
        }

        private static bool CanShowOurFlyouts => S.DefaultFlyout == DefaultFlyout.ModernFlyouts;

        public void ShowSettings()
        {
            _settingsWindow ??= new SettingsWindow();
            _settingsWindow.ShowAndActivate();
        }

        private void ShowFromShell(bool moduleEnabled, Action show)
        {
            Interlocked.Exchange(ref _lastShellTriggerTick, Environment.TickCount64);
            if (CanShowOurFlyouts && moduleEnabled)
            {
                show();
            }
        }

        #region Lock keys

        private void OnKeyDown(int vk)
        {
            if (vk is not (VK_CAPITAL or VK_NUMLOCK or VK_SCROLL or VK_INSERT))
            {
                return;
            }

            // Ignore auto-repeat of the same lock key, but not other held keys
            // (Shift + Caps Lock toggles Caps Lock on JIS keyboards).
            if (_pressedLockKey == vk)
            {
                return;
            }
            _pressedLockKey = vk;

            bool keyEnabled = vk switch
            {
                VK_CAPITAL => S.CapsLockEnabled,
                VK_NUMLOCK => S.NumLockEnabled,
                VK_SCROLL => S.ScrollLockEnabled,
                _ => S.InsertEnabled,
            };
            if (!CanShowOurFlyouts || !S.LockKeysModuleEnabled || !keyEnabled)
            {
                return;
            }

            if (vk == VK_INSERT && AnyModifierDown())
            {
                return;
            }

            // The hook runs before the toggle state changes, so the new state is the inverse.
            bool on = (GetKeyState(vk) & 1) == 0;

            if (vk == VK_INSERT)
            {
                _flyout!.ShowInfo(string.Empty, on ? Strings.LockKeysFlyoutHelper_OvertypeMode : Strings.LockKeysFlyoutHelper_InsertMode);
                return;
            }

            string keyName = vk switch
            {
                VK_CAPITAL => Strings.LockKeysFlyoutHelper_CapsLock,
                VK_NUMLOCK => Strings.LockKeysFlyoutHelper_NumLock,
                _ => Strings.LockKeysFlyoutHelper_ScrollLock,
            };
            _flyout!.ShowInfo(on ? "" : "", // Lock / Unlock
                string.Format(on ? Strings.LockKeysFlyoutHelper_KeyIsOn : Strings.LockKeysFlyoutHelper_KeyIsOff, keyName));
        }

        private static bool AnyModifierDown()
        {
            foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }) // Shift, Ctrl, Alt, LWin, RWin
            {
                if (GetKeyState(key) < 0) return true;
            }
            return false;
        }

        #endregion

        #region Native OSD fallback

        private async void OnNativeFlyoutShown()
        {
            if (S.DefaultFlyout == DefaultFlyout.WindowsDefault)
            {
                return; // leave the shell OSD alone
            }

            // Windows 11 shows the virtual desktop switcher in the same host window.
            // Keep the native switcher when our virtual desktop flyout is turned off.
            if (TryShowVirtualDesktop())
            {
                return;
            }

            NativeFlyoutHandler.Instance.HideNativeFlyout();

            if (!CanShowOurFlyouts || !S.AudioModuleEnabled)
            {
                return;
            }

            // Already handled through the shell hook (keyboard volume/brightness keys).
            if (Environment.TickCount64 - Interlocked.Read(ref _lastShellTriggerTick) <= ShellTriggerWindow.TotalMilliseconds)
            {
                return;
            }

            // Touchpad gestures / headset buttons: only show the volume flyout when the volume
            // actually changed. The notification may arrive after the OSD, so wait a moment.
            if (!_volume!.HasRecentChange(FallbackVolumeChangeWindow))
            {
                await Task.Delay(FallbackVolumeChangeGrace);
                if (!_volume.HasRecentChange(FallbackVolumeChangeWindow + FallbackVolumeChangeGrace))
                {
                    // The shell may update the current desktop after showing its switcher.
                    TryShowVirtualDesktop();
                    return;
                }
            }

            _flyout!.ShowVolume();
        }

        /// <summary>
        /// Returns true when the current virtual desktop changed (the OSD was the desktop switcher).
        /// Shows our flyout (and hides the native one) only when that module is enabled.
        /// </summary>
        private bool TryShowVirtualDesktop()
        {
            var desktop = VirtualDesktopManager.Instance.TryGetCurrentDesktop();
            if (desktop == null)
            {
                return false;
            }

            bool changed = _lastVirtualDesktopId.HasValue && _lastVirtualDesktopId != desktop.Id;
            _lastVirtualDesktopId = desktop.Id;
            if (!changed)
            {
                return false;
            }

            if (CanShowOurFlyouts && S.VirtualDesktopModuleEnabled)
            {
                NativeFlyoutHandler.Instance.HideNativeFlyout();
                string name = string.IsNullOrWhiteSpace(desktop.Name) && desktop.Index >= 0
                    ? string.Format(Strings.VirtualDesktopFlyoutHelper_DefaultName, desktop.Index + 1)
                    : desktop.Name;
                _flyout!.ShowVirtualDesktop(name, desktop.Index, desktop.Count);
            }

            return true;
        }

        #endregion

        private async void StartMediaAsync()
        {
            try
            {
                await _media!.StartAsync(S.EnhancedMediaBackendMode);
                Log($"media backend: {_media.BackendName}");
            }
            catch (Exception ex)
            {
                Log($"media backend failed: {ex}");
            }
        }

        private static void TryRun(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log($"{what} failed: {ex.Message}");
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
