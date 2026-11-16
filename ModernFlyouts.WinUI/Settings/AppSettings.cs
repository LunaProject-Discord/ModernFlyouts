using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using ModernFlyouts.Core.Media.Control;
using ModernFlyouts.Core.UI;

namespace ModernFlyouts.WinUI.Settings
{
    /// <summary>Top bar of the flyout (settings / reset position / drag handle / close).</summary>
    public enum TopBarVisibility
    {
        Visible,
        AutoHide,
        Collapsed,
    }

    public enum SessionsOrientation
    {
        Horizontal,
        Vertical,
    }

    public enum StackingDirection
    {
        Ascending,
        Descending,
    }

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
        private readonly object _saveLock = new();

        public static AppSettings Instance { get; } = Load();

        #region General

        private bool runAtStartup;
        public bool RunAtStartup { get => runAtStartup; set => Set(ref runAtStartup, value); }

        private string language = string.Empty;
        /// <summary>UI culture name (e.g. "ja"); empty = Windows display language. Applied at startup.</summary>
        public string Language { get => language; set => Set(ref language, value ?? string.Empty); }

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

        private bool flyoutAnimationEnabled = true;
        public bool FlyoutAnimationEnabled { get => flyoutAnimationEnabled; set => Set(ref flyoutAnimationEnabled, value); }

        private TopBarVisibility topBarVisibility = TopBarVisibility.Visible;
        public TopBarVisibility TopBarVisibility { get => topBarVisibility; set => Set(ref topBarVisibility, value); }

        private double flyoutBackgroundOpacity = 100;
        /// <summary>Opacity of the flyout background in percent (content stays opaque).</summary>
        public double FlyoutBackgroundOpacity { get => flyoutBackgroundOpacity; set => Set(ref flyoutBackgroundOpacity, Math.Clamp(value, 0, 100)); }

        private bool useColoredTrayIcon = true;
        public bool UseColoredTrayIcon { get => useColoredTrayIcon; set => Set(ref useColoredTrayIcon, value); }

        #endregion

        #region Layout

        private FlyoutWindowPlacementMode placementMode = FlyoutWindowPlacementMode.Auto;
        /// <summary>Auto: <see cref="FlyoutAlignment"/> + <see cref="FlyoutMargin"/>; Manual: dragged position.</summary>
        public FlyoutWindowPlacementMode PlacementMode { get => placementMode; set => Set(ref placementMode, value); }

        private FlyoutWindowAlignments flyoutAlignment = FlyoutWindowAlignments.Bottom;
        /// <summary>Flags: Top/Bottom and Left/Right; Center (0) on an axis means centered.</summary>
        public FlyoutWindowAlignments FlyoutAlignment { get => flyoutAlignment; set => Set(ref flyoutAlignment, value); }

        private int flyoutMargin = 48;
        /// <summary>Distance from the work area edges in DIP (automatic placement).</summary>
        public int FlyoutMargin { get => flyoutMargin; set => Set(ref flyoutMargin, Math.Clamp(value, 0, 400)); }

        private string flyoutMonitorId = string.Empty;
        /// <summary>Device id of the display to use; empty = primary display.</summary>
        public string FlyoutMonitorId { get => flyoutMonitorId; set => Set(ref flyoutMonitorId, value ?? string.Empty); }

        // Manual position of the flyout (top-left of its content, physical pixels).
        private int? flyoutPositionX;
        public int? FlyoutPositionX { get => flyoutPositionX; set => Set(ref flyoutPositionX, value); }

        private int? flyoutPositionY;
        public int? FlyoutPositionY { get => flyoutPositionY; set => Set(ref flyoutPositionY, value); }

        // Bottom edge of the dragged flyout (physical pixels), kept for growing upwards.
        private int? flyoutPositionBottom;
        public int? FlyoutPositionBottom { get => flyoutPositionBottom; set => Set(ref flyoutPositionBottom, value); }

        private FlyoutWindowExpandDirection expandDirection = FlyoutWindowExpandDirection.Auto;
        /// <summary>Manual placement: which edge stays put when the flyout grows (Auto: the one nearer the screen edge).</summary>
        public FlyoutWindowExpandDirection ExpandDirection { get => expandDirection; set => Set(ref expandDirection, value); }

        private StackingDirection contentStackingDirection = StackingDirection.Ascending;
        /// <summary>Ascending: volume above media; Descending: media above volume.</summary>
        public StackingDirection ContentStackingDirection { get => contentStackingDirection; set => Set(ref contentStackingDirection, value); }

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

        private bool showVolumeControlInMediaFlyout = true;
        /// <summary>Show the volume row when the flyout is opened by a media key.</summary>
        public bool ShowVolumeControlInMediaFlyout { get => showVolumeControlInMediaFlyout; set => Set(ref showVolumeControlInMediaFlyout, value); }

        private bool showMediaShuffle = true;
        public bool ShowMediaShuffle { get => showMediaShuffle; set => Set(ref showMediaShuffle, value); }

        private bool showMediaRepeat = true;
        public bool ShowMediaRepeat { get => showMediaRepeat; set => Set(ref showMediaRepeat, value); }

        private bool useThumbnailAsBackground = true;
        public bool UseThumbnailAsBackground { get => useThumbnailAsBackground; set => Set(ref useThumbnailAsBackground, value); }

        private bool alignThumbnailToRight = true;
        public bool AlignThumbnailToRight { get => alignThumbnailToRight; set => Set(ref alignThumbnailToRight, value); }

        private SessionsOrientation sessionsPanelOrientation = SessionsOrientation.Horizontal;
        /// <summary>How several media sessions are laid out (side by side, or stacked).</summary>
        public SessionsOrientation SessionsPanelOrientation { get => sessionsPanelOrientation; set => Set(ref sessionsPanelOrientation, value); }

        private int maxVerticalSessionControlsCount = 1;
        /// <summary>Stacked layout: how many sessions are visible before scrolling.</summary>
        public int MaxVerticalSessionControlsCount { get => maxVerticalSessionControlsCount; set => Set(ref maxVerticalSessionControlsCount, Math.Clamp(value, 1, 5)); }

        private string pinnedMediaAppUserModelId = string.Empty;
        public string PinnedMediaAppUserModelId { get => pinnedMediaAppUserModelId; set => Set(ref pinnedMediaAppUserModelId, value ?? string.Empty); }

        private PinnedAppPriorityMode pinnedAppPriorityMode = PinnedAppPriorityMode.PreferPinnedOnlyWhenPlaying;
        public PinnedAppPriorityMode PinnedAppPriorityMode { get => pinnedAppPriorityMode; set => Set(ref pinnedAppPriorityMode, value); }

        private MediaAppFilteringMode mediaAppFilteringMode = MediaAppFilteringMode.Disabled;
        public MediaAppFilteringMode MediaAppFilteringMode { get => mediaAppFilteringMode; set => Set(ref mediaAppFilteringMode, value); }

        private string mediaAppFilterList = string.Empty;
        /// <summary>App IDs separated by commas, semicolons or new lines.</summary>
        public string MediaAppFilterList { get => mediaAppFilterList; set => Set(ref mediaAppFilterList, value ?? string.Empty); }

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

        /// <summary>Writes a pending change now (used on exit).</summary>
        public void Flush()
        {
            if (_saveTimer != null)
            {
                _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
                Save();
            }
        }

        private void Save()
        {
            lock (_saveLock) // the timer and Flush() may both save
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
