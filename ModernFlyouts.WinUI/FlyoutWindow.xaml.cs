using System;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ModernFlyouts.Core.Display;
using ModernFlyouts.WinUI.Audio;
using ModernFlyouts.WinUI.Interop;
using ModernFlyouts.WinUI.Media;
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
        private const double BottomMarginDip = 48;

        private readonly IntPtr _hwnd;
        private readonly DispatcherQueueTimer _hideTimer;
        private readonly VolumeService _volume;
        private MediaController? _media;
        private BrightnessController? _brightness;
        private FlyoutKind _kind = FlyoutKind.Volume;
        private bool _updatingFromSystem;
        private bool _pointerOver;

        public FlyoutWindow(VolumeService volume)
        {
            InitializeComponent();
            _volume = volume;
            _hwnd = WindowNative.GetWindowHandle(this);

            ConfigureWindow();

            _hideTimer = DispatcherQueue.CreateTimer();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(2500);
            _hideTimer.IsRepeating = false;
            _hideTimer.Tick += (_, _) => HideFlyout();

            _volume.VolumeChanged += (v, muted) => DispatcherQueue.TryEnqueue(() => UpdateVolume(v, muted));
            UpdateVolume(_volume.Volume, _volume.IsMuted);

            MediaPanel.Interacted += (_, _) => RestartHideTimer();
        }

        public IntPtr Handle => _hwnd;

        public bool IsShown { get; private set; }

        public FlyoutKind Kind => _kind;

        /// <summary>True while the media timeline is actually on screen (drives the enhanced backend's ticker).</summary>
        public bool IsTimelineDisplayed => IsShown && MediaSection.Visibility == Visibility.Visible;

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

            PlaceAtBottomCenter();

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
            AppWindow.Hide();
            IsShown = false;
            _media?.RefreshTimelineActivity();
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
                PlaceAtBottomCenter();
            }

            _media?.RefreshTimelineActivity();
        }

        private void UpdateMediaVisibility()
        {
            bool show = _kind == FlyoutKind.Volume && MediaPanel.Session != null;
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
            exStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(exStyle));

            int corner = Native.DWMWCP_ROUND;
            Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            // XAML content is only realized once the window has been activated, so activate it
            // once off-screen at startup (WS_EX_NOACTIVATE keeps focus where it is) and hide it.
            AppWindow.Move(new PointInt32(-32000, -32000));
            Activate();
            AppWindow.Hide();
        }

        private void PlaceAtBottomCenter()
        {
            double scale = Native.GetDpiForWindow(_hwnd) / 96.0;
            var workArea = DisplayArea.Primary.WorkArea;

            // Size the window to its content.
            RootPanel.Measure(new Size(WidthDip, double.PositiveInfinity));
            double heightDip = Math.Ceiling(RootPanel.DesiredSize.Height);

            int width = (int)Math.Round(WidthDip * scale);
            int height = (int)Math.Round(heightDip * scale);
            int x = workArea.X + (workArea.Width - width) / 2;
            int y = workArea.Y + workArea.Height - height - (int)Math.Round(BottomMarginDip * scale);

            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
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
        }

        private void RootPanel_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            _pointerOver = false;
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
