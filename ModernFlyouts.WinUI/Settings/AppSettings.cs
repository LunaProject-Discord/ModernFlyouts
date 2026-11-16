using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using ModernFlyouts.Core.Media.Control;

namespace ModernFlyouts.WinUI.Settings
{
    /// <summary>Which flyout is shown for volume/brightness/... events.</summary>
    public enum DefaultFlyout
    {
        ModernFlyouts,
        WindowsDefault,
        None,
    }

    /// <summary>
    /// User settings, persisted as JSON in %LOCALAPPDATA%\ModernFlyouts\settings.json.
    /// Defaults match the WPF version's DefaultValuesStore. Every change is saved shortly after.
    /// </summary>
    public sealed class AppSettings : ObservableObject
    {
        public const int RecommendedFlyoutTimeout = 2750;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModernFlyouts", "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private Timer? _saveTimer;

        public static AppSettings Instance { get; } = Load();

        #region General

        private bool runAtStartup;
        public bool RunAtStartup { get => runAtStartup; set => Set(ref runAtStartup, value); }

        private DefaultFlyout defaultFlyout = DefaultFlyout.ModernFlyouts;
        public DefaultFlyout DefaultFlyout { get => defaultFlyout; set => Set(ref defaultFlyout, value); }

        #endregion

        #region Personalization

        private ElementTheme appTheme = ElementTheme.Default;
        public ElementTheme AppTheme { get => appTheme; set => Set(ref appTheme, value); }

        private ElementTheme flyoutTheme = ElementTheme.Default;
        public ElementTheme FlyoutTheme { get => flyoutTheme; set => Set(ref flyoutTheme, value); }

        private int flyoutTimeout = RecommendedFlyoutTimeout;
        public int FlyoutTimeout { get => flyoutTimeout; set => Set(ref flyoutTimeout, Math.Clamp(value, 500, 60000)); }

        private bool trayIconEnabled = true;
        public bool TrayIconEnabled { get => trayIconEnabled; set => Set(ref trayIconEnabled, value); }

        #endregion

        #region Modules

        private bool audioModuleEnabled = true;
        public bool AudioModuleEnabled { get => audioModuleEnabled; set => Set(ref audioModuleEnabled, value); }

        private bool showMediaInVolumeFlyout = true;
        public bool ShowMediaInVolumeFlyout { get => showMediaInVolumeFlyout; set => Set(ref showMediaInVolumeFlyout, value); }

        private bool showMediaPlayerInfo = true;
        public bool ShowMediaPlayerInfo { get => showMediaPlayerInfo; set => Set(ref showMediaPlayerInfo, value); }

        private bool showMediaSeekbar = true;
        public bool ShowMediaSeekbar { get => showMediaSeekbar; set => Set(ref showMediaSeekbar, value); }

        private EnhancedMediaBackendMode enhancedMediaBackendMode = EnhancedMediaBackendMode.Auto;
        public EnhancedMediaBackendMode EnhancedMediaBackendMode { get => enhancedMediaBackendMode; set => Set(ref enhancedMediaBackendMode, value); }

        private bool brightnessModuleEnabled = true;
        public bool BrightnessModuleEnabled { get => brightnessModuleEnabled; set => Set(ref brightnessModuleEnabled, value); }

        private bool airplaneModeModuleEnabled = true;
        public bool AirplaneModeModuleEnabled { get => airplaneModeModuleEnabled; set => Set(ref airplaneModeModuleEnabled, value); }

        private bool lockKeysModuleEnabled = true;
        public bool LockKeysModuleEnabled { get => lockKeysModuleEnabled; set => Set(ref lockKeysModuleEnabled, value); }

        private bool capsLockEnabled = true;
        public bool CapsLockEnabled { get => capsLockEnabled; set => Set(ref capsLockEnabled, value); }

        private bool numLockEnabled = true;
        public bool NumLockEnabled { get => numLockEnabled; set => Set(ref numLockEnabled, value); }

        private bool scrollLockEnabled = true;
        public bool ScrollLockEnabled { get => scrollLockEnabled; set => Set(ref scrollLockEnabled, value); }

        private bool insertEnabled = true;
        public bool InsertEnabled { get => insertEnabled; set => Set(ref insertEnabled, value); }

        private bool virtualDesktopModuleEnabled = true;
        public bool VirtualDesktopModuleEnabled { get => virtualDesktopModuleEnabled; set => Set(ref virtualDesktopModuleEnabled, value); }

        #endregion

        public void ResetToDefaults()
        {
            var defaults = new AppSettings();
            foreach (var property in typeof(AppSettings).GetProperties())
            {
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                {
                    property.SetValue(this, property.GetValue(defaults));
                }
            }
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (SetProperty(ref field, value, propertyName))
            {
                ScheduleSave();
            }
        }

        private void ScheduleSave()
        {
            _saveTimer ??= new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(500, Timeout.Infinite);
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                App.Log($"settings save failed: {ex.Message}");
            }
        }

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    // Deserialization goes through the setters, which may schedule one redundant save.
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log($"settings load failed, using defaults: {ex.Message}");
            }

            return new AppSettings();
        }
    }
}
