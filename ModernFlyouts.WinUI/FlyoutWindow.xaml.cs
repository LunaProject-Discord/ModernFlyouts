using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ModernFlyouts.Core.Display;
using ModernFlyouts.Core.UI;
using ModernFlyouts.Properties;
using ModernFlyouts.WinUI.Audio;
using ModernFlyouts.WinUI.Interop;
using ModernFlyouts.WinUI.Media;
using ModernFlyouts.WinUI.Settings;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace ModernFlyouts.WinUI
{
    public enum FlyoutKind
    {
        Volume,
        Brightness,
        Info,
        VirtualDesktop,
    }

    /// <summary>
    /// On-screen flyout: borderless acrylic window that stays on top, never takes focus,
    /// is hidden from the taskbar/Alt+Tab and hides itself after a timeout.
    /// Shows one row at a time (volume, brightness, info or virtual desktop); the media
    /// panel is added below the volume row while something is playing.
    /// </summary>
    public sealed partial class FlyoutWindow : Window
    {
        private const double WidthDip = 360;

        // Fluent motion: decelerate in (slide up + fade in), fade out quickly.
        private const double ShowDurationMs = 250;
        private const double HideDurationMs = 120;
        private const double ShowSlideDip = 16;

        private readonly IntPtr _hwnd;
        private readonly DispatcherQueueTimer _hideTimer;
        private readonly DispatcherQueueTimer _animationTimer;
        private readonly VolumeService _volume;
        private MediaController? _media;
        private BrightnessController? _brightness;
        private FlyoutKind _kind = FlyoutKind.Volume;
        private bool _updatingFromSystem;
        private bool _pointerOver;

        // Current placement of the client area (physical pixels).
        private PointInt32 _clientPosition;
        private SizeInt32 _clientSize;

        private readonly Stopwatch _animationClock = new();
        private bool _animatingOut;
        private int _animationFromY;

        public FlyoutWindow(VolumeService volume)
        {
            InitializeComponent();
            _volume = volume;
            _hwnd = WindowNative.GetWindowHandle(this);

            ConfigureWindow();

            _hideTimer = DispatcherQueue.CreateTimer();
            _hideTimer.IsRepeating = false;
            _hideTimer.Tick += (_, _) => HideFlyout();

            _animationTimer = DispatcherQueue.CreateTimer();
            _animationTimer.Interval = TimeSpan.FromMilliseconds(8);
            _animationTimer.IsRepeating = true;
            _animationTimer.Tick += (_, _) => AnimationStep();

            _volume.VolumeChanged += (v, muted) => DispatcherQueue.TryEnqueue(() => UpdateVolume(v, muted));
            UpdateVolume(_volume.Volume, _volume.IsMuted);

            MediaPanel.Interacted += (_, _) => RestartHideTimer();
            MediaPanel.LayoutChanged += (_, _) => { if (IsShown) PlaceWindow(animate: false); };

            ApplySettings();
            AppSettings.Instance.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplySettings);
        }

        public string SettingsText => Strings.SettingsItem;

        public string AlignText => Strings.Align;

        public string CloseText => Strings.Close;

        public IntPtr Handle => _hwnd;

        public bool IsShown { get; private set; }

        public FlyoutKind Kind => _kind;

        /// <summary>True while the media timeline is actually on screen (drives the enhanced backend's ticker).</summary>
        public bool IsTimelineDisplayed => IsShown && MediaSection.Visibility == Visibility.Visible;

        public event Action? SettingsRequested;

        private void ApplySettings()
        {
            var s = AppSettings.Instance;
            _hideTimer.Interval = TimeSpan.FromMilliseconds(s.FlyoutTimeout);
            RootPanel.RequestedTheme = s.FlyoutTheme;
            MediaPanel.ShowPlayerInfo = s.ShowMediaPlayerInfo;
            MediaPanel.ShowSeekbar = s.ShowMediaSeekbar;
            UpdateTopBar();
            AlignButton.IsEnabled = s.PlacementMode == FlyoutWindowPlacementMode.Manual;
            UpdateMediaVisibility();

            if (IsShown)
            {
                PlaceWindow(animate: false);
            }
        }

        #region Showing

        public void ShowVolume() => ShowKind(FlyoutKind.Volume);

        public void ShowBrightness()
        {
            AttachBrightness(BrightnessManager.Instance?.DefaultBrightnessController
                ?? (BrightnessManager.Instance?.BrightnessControllers.Count > 0 ? BrightnessManager.Instance.BrightnessControllers[0] : null));

            if (_brightness == null)
            {
                return; // nothing to control (e.g. desktop without DDC/CI)
            }

            ShowKind(FlyoutKind.Brightness);
        }

        public void ShowInfo(string glyph, string text)
        {
            InfoGlyph.Glyph = glyph;
            InfoText.Text = text;
            ShowKind(FlyoutKind.Info);
        }

        public void ShowVirtualDesktop(string name, int index, int count)
        {
            DesktopNameText.Text = name;
            DesktopDots.Children.Clear();
            for (int i = 0; i < count; i++)
            {
                bool current = i == index;
                DesktopDots.Children.Add(new Border
                {
                    Width = current ? 16 : 6,
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = (Brush)Application.Current.Resources[current ? "AccentFillColorDefaultBrush" : "TextFillColorPrimaryBrush"],
                    Opacity = current ? 1 : 0.35,
                });
            }

            ShowKind(FlyoutKind.VirtualDesktop);
        }

        private void ShowKind(FlyoutKind kind)
        {
            _kind = kind;
            VolumeRow.Visibility = kind == FlyoutKind.Volume ? Visibility.Visible : Visibility.Collapsed;
            BrightnessRow.Visibility = kind == FlyoutKind.Brightness ? Visibility.Visible : Visibility.Collapsed;
            InfoRow.Visibility = kind == FlyoutKind.Info ? Visibility.Visible : Visibility.Collapsed;
            DesktopRow.Visibility = kind == FlyoutKind.VirtualDesktop ? Visibility.Visible : Visibility.Collapsed;
            UpdateMediaVisibility();

            bool appearing = !IsShown || _animatingOut;
            PlaceWindow(animate: appearing);

            if (!IsShown)
            {
                AppWindow.Show(false); // do not activate
                IsShown = true;
            }

            // Re-assert topmost every time; other topmost windows may have been raised since.
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

            _media?.RefreshTimelineActivity();
            RestartHideTimer();
        }

        public void HideFlyout()
        {
            _hideTimer.Stop();
            if (!IsShown)
            {
                return;
            }

            if (AppSettings.Instance.FlyoutAnimationEnabled && !_animatingOut)
            {
                _animatingOut = true;
                _animationClock.Restart();
                _animationTimer.Start();
                return;
            }

            CompleteHide();
        }

        private void CompleteHide()
        {
            _animationTimer.Stop();
            _animatingOut = false;
            AppWindow.Hide();
            IsShown = false;
            _media?.RefreshTimelineActivity();
        }

        #endregion

        #region Placement and animation

        /// <summary>
        /// Sizes the client area to the content (exactly <see cref="WidthDip"/> wide; the frame
        /// is added outside) and places it at the saved position or bottom-center of the work area.
        /// </summary>
        private void PlaceWindow(bool animate)
        {
            double scale = Native.GetDpiForWindow(_hwnd) / 96.0;

            RootPanel.Measure(new Size(WidthDip, double.PositiveInfinity));
            double heightDip = Math.Ceiling(RootPanel.DesiredSize.Height);
            _clientSize = new SizeInt32((int)Math.Round(WidthDip * scale), (int)Math.Round(heightDip * scale));

            var s = AppSettings.Instance;
            PointInt32 position;
            if (s.PlacementMode == FlyoutWindowPlacementMode.Manual
                && s.FlyoutPositionX is int savedX && s.FlyoutPositionY is int savedY)
            {
                // Keep a saved position on screen (monitor layout may have changed).
                var area = DisplayArea.GetFromPoint(new PointInt32(savedX, savedY), DisplayAreaFallback.Nearest).WorkArea;
                position = new PointInt32(
                    Math.Clamp(savedX, area.X, Math.Max(area.X, area.X + area.Width - _clientSize.Width)),
                    Math.Clamp(savedY, area.Y, Math.Max(area.Y, area.Y + area.Height - _clientSize.Height)));
            }
            else
            {
                position = GetAlignedPosition(GetTargetDisplay().WorkArea, s.FlyoutAlignment,
                    (int)Math.Round(s.FlyoutMargin * scale));
            }

            _clientPosition = position;

            if (animate && s.FlyoutAnimationEnabled)
            {
                _animatingOut = false;
                _animationFromY = position.Y + (int)Math.Round(ShowSlideDip * scale);
                SetAlpha(0);
                MoveClientTo(position.X, _animationFromY);
                _animationClock.Restart();
                _animationTimer.Start();
            }
            else
            {
                if (!_animatingOut)
                {
                    _animationTimer.Stop();
                    SetAlpha(255);
                }
                MoveClientTo(position.X, position.Y);
            }
        }

        /// <summary>One of 9 positions in the work area, <paramref name="margin"/> away from the edges.</summary>
        private PointInt32 GetAlignedPosition(RectInt32 area, FlyoutWindowAlignments alignment, int margin)
        {
            int x = alignment.HasFlag(FlyoutWindowAlignments.Left) ? area.X + margin
                : alignment.HasFlag(FlyoutWindowAlignments.Right) ? area.X + area.Width - _clientSize.Width - margin
                : area.X + (area.Width - _clientSize.Width) / 2;
            int y = alignment.HasFlag(FlyoutWindowAlignments.Top) ? area.Y + margin
                : alignment.HasFlag(FlyoutWindowAlignments.Bottom) ? area.Y + area.Height - _clientSize.Height - margin
                : area.Y + (area.Height - _clientSize.Height) / 2;
            return new PointInt32(x, y);
        }

        /// <summary>The display chosen in settings, or the primary one (also when it was unplugged).</summary>
        private static DisplayArea GetTargetDisplay()
        {
            string id = AppSettings.Instance.FlyoutMonitorId;
            if (!string.IsNullOrEmpty(id) && DisplayManager.Instance != null)
            {
                foreach (var monitor in DisplayManager.Instance.DisplayMonitors)
                {
                    if (monitor.DeviceId == id)
                    {
                        var displayId = Microsoft.UI.Win32Interop.GetDisplayIdFromMonitor(monitor.HMonitor);
                        return DisplayArea.GetFromDisplayId(displayId) ?? DisplayArea.Primary;
                    }
                }
            }

            return DisplayArea.Primary;
        }

        private void AnimationStep()
        {
            double elapsed = _animationClock.Elapsed.TotalMilliseconds;

            if (_animatingOut)
            {
                double t = Math.Min(1, elapsed / HideDurationMs);
                SetAlpha((byte)Math.Round(255 * (1 - t * t))); // accelerate out
                if (t >= 1)
                {
                    CompleteHide();
                }
                return;
            }

            double p = Math.Min(1, elapsed / ShowDurationMs);
            double eased = 1 - Math.Pow(1 - p, 3); // decelerate (cubic ease-out)
            int y = (int)Math.Round(_animationFromY + (_clientPosition.Y - _animationFromY) * eased);
            MoveClientTo(_clientPosition.X, y);
            SetAlpha((byte)Math.Round(255 * Math.Min(1, p * 1.5)));

            if (p >= 1)
            {
                _animationTimer.Stop();
                SetAlpha(255);
            }
        }

        private void MoveClientTo(int clientX, int clientY)
        {
            // The window rect includes the (mostly invisible) resize frame, which is thinner at the
            // top than on the other sides. Measure the real insets and size the outer rect so the
            // client area is exactly as requested (ResizeClient would enforce a minimum height).
            Native.GetWindowRect(_hwnd, out var window);
            Native.GetClientRect(_hwnd, out var client);
            var origin = new Native.POINT();
            Native.ClientToScreen(_hwnd, ref origin);

            int left = origin.X - window.Left;
            int top = origin.Y - window.Top;
            int right = window.Right - origin.X - client.Right;
            int bottom = window.Bottom - origin.Y - client.Bottom;

            AppWindow.MoveAndResize(new RectInt32(
                clientX - left, clientY - top,
                _clientSize.Width + left + right, _clientSize.Height + top + bottom));
        }

        private void SetAlpha(byte alpha)
        {
            Native.SetLayeredWindowAttributes(_hwnd, 0, alpha, Native.LWA_ALPHA);
        }

        #endregion

        #region Top bar

        private void UpdateTopBar()
        {
            var mode = AppSettings.Instance.TopBarVisibility;
            TopBar.Visibility = mode == TopBarVisibility.Collapsed ? Visibility.Collapsed : Visibility.Visible;
            TopBar.Opacity = mode == TopBarVisibility.AutoHide && !_pointerOver ? 0 : 1;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout();
            SettingsRequested?.Invoke();
        }

        private void AlignButton_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.Instance.PlacementMode = FlyoutWindowPlacementMode.Auto;
            RestartHideTimer();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => HideFlyout();

        private void DragArea_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(DragArea).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _hideTimer.Stop();
            _animationTimer.Stop();
            SetAlpha(255);

            // Let Windows run the move loop (returns when the button is released).
            Native.ReleaseCapture();
            Native.SendMessage(_hwnd, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);

            var client = new Native.POINT();
            Native.ClientToScreen(_hwnd, ref client);
            _clientPosition = new PointInt32(client.X, client.Y);
            AppSettings.Instance.FlyoutPositionX = client.X;
            AppSettings.Instance.FlyoutPositionY = client.Y;
            AppSettings.Instance.PlacementMode = FlyoutWindowPlacementMode.Manual;
            RestartHideTimer();
        }

        #endregion

        #region Media

        public void AttachMedia(MediaController media)
        {
            _media = media;
            _media.CurrentSessionChanged += (_, _) => UpdateMediaSession();
            UpdateMediaSession();
        }

        private void UpdateMediaSession()
        {
            MediaPanel.Session = _media?.CurrentSession;
            UpdateMediaVisibility();

            if (IsShown)
            {
                PlaceWindow(animate: false);
            }

            _media?.RefreshTimelineActivity();
        }

        private void UpdateMediaVisibility()
        {
            bool show = _kind == FlyoutKind.Volume && MediaPanel.Session != null
                && AppSettings.Instance.ShowMediaInVolumeFlyout;
            MediaSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        #region Window

        private void ConfigureWindow()
        {
            var presenter = OverlappedPresenter.Create();
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            // Keep the border so DWM still draws the Windows 11 shadow and rounded corners.
            presenter.SetBorderAndTitleBar(true, false);
            AppWindow.SetPresenter(presenter);
            presenter.IsAlwaysOnTop = true; // must be set after SetPresenter to stick
            AppWindow.IsShownInSwitchers = false;

            long exStyle = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            exStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(exStyle));
            SetAlpha(255);

            int corner = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            // XAML content is only realized once the window has been activated, so activate it
            // once off-screen at startup (WS_EX_NOACTIVATE keeps focus where it is) and hide it.
            AppWindow.Move(new PointInt32(-32000, -32000));
            Activate();
            AppWindow.Hide();
        }

        private void RestartHideTimer()
        {
            _hideTimer.Stop();
            if (!_pointerOver)
            {
                _hideTimer.Start();
            }
        }

        private void RootPanel_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _pointerOver = true;
            _hideTimer.Stop();
            UpdateTopBar();
        }

        private void RootPanel_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _pointerOver = false;
            UpdateTopBar();
            RestartHideTimer();
        }

        #endregion

        #region Volume

        private void UpdateVolume(double volume, bool muted)
        {
            _updatingFromSystem = true;
            VolumeSlider.Value = volume;
            _updatingFromSystem = false;

            VolumeText.Text = volume.ToString("0");
            VolumeShadowGlyph.Visibility = muted ? Visibility.Collapsed : Visibility.Visible;
            VolumeGlyph.Glyph = muted ? "" : volume switch
            {
                0 => "",
                < 34 => "",
                < 67 => "",
                _ => "",
            };
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updatingFromSystem) return;
            _volume.Volume = e.NewValue;
            RestartHideTimer();
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            _volume.IsMuted = !_volume.IsMuted;
            RestartHideTimer();
        }

        #endregion

        #region Brightness

        private void AttachBrightness(BrightnessController? controller)
        {
            if (ReferenceEquals(_brightness, controller))
            {
                UpdateBrightness();
                return;
            }

            if (_brightness != null)
            {
                _brightness.PropertyChanged -= Brightness_PropertyChanged;
            }

            _brightness = controller;

            if (_brightness != null)
            {
                _brightness.PropertyChanged += Brightness_PropertyChanged;
            }

            UpdateBrightness();
        }

        private void Brightness_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(UpdateBrightness);
        }

        private void UpdateBrightness()
        {
            if (_brightness == null)
            {
                return;
            }

            _updatingFromSystem = true;
            BrightnessSlider.Minimum = _brightness.Minimum;
            BrightnessSlider.Maximum = _brightness.Maximum;
            BrightnessSlider.Value = _brightness.Brightness;
            _updatingFromSystem = false;

            BrightnessText.Text = _brightness.Brightness.ToString("0");
            // Brightness / LowerBrightness glyphs, as in the WPF version.
            BrightnessGlyph.Glyph = _brightness.Brightness > 50 ? "" : "";
        }

        private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updatingFromSystem || _brightness == null) return;
            _brightness.Brightness = e.NewValue;
            RestartHideTimer();
        }

        #endregion
    }
}
