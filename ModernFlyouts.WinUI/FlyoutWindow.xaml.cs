using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
using ModernFlyouts.WinUI.Controls;
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
    /// sessions are added next to the volume row while something is playing.
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
        private readonly List<MediaPanel> _mediaPanels = new();
        private MediaController? _media;
        private FlyoutKind _kind = FlyoutKind.Volume;
        private bool _mediaKeyMode;
        private bool _updatingFromSystem;
        private bool _pointerOver;

        // Current placement of the client area (physical pixels).
        private PointInt32 _clientPosition;
        private SizeInt32 _clientSize;

        private readonly Stopwatch _animationClock = new();
        private bool _animatingOut;
        private int _animationFromY;

        // Top bar (WPF sizes and timing): 32 px; auto-hide collapses it to nothing and leaves a
        // 10 px strip to bring it back; it collapses again once the pointer is 10 px below it.
        private const double TopBarHeightDip = 32;
        private const double TopBarHotZoneDip = 10;
        private const double TopBarAnimationMs = 167;
        private readonly DispatcherQueueTimer _topBarTimer;
        private readonly Stopwatch _topBarClock = new();
        private double _topBarFrom;
        private double _topBarTo = TopBarHeightDip;
        private bool _topBarExpanded = true;

        // After a pointer-exit caused by a resize (pointer still inside), WinUI won't report the
        // real exit; watch the cursor until it actually leaves.
        private readonly DispatcherQueueTimer _pointerWatchTimer;

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

            _topBarTimer = DispatcherQueue.CreateTimer();
            _topBarTimer.Interval = TimeSpan.FromMilliseconds(8);
            _topBarTimer.IsRepeating = true;
            _topBarTimer.Tick += (_, _) => TopBarAnimationStep();

            _pointerWatchTimer = DispatcherQueue.CreateTimer();
            _pointerWatchTimer.Interval = TimeSpan.FromMilliseconds(100);
            _pointerWatchTimer.IsRepeating = true;
            _pointerWatchTimer.Tick += (_, _) =>
            {
                if (!IsShown || !IsCursorOverWindow())
                {
                    OnPointerLeft();
                }
            };

            _volume.VolumeChanged += (v, muted) => DispatcherQueue.TryEnqueue(() => UpdateVolume(v, muted));
            UpdateVolume(_volume.Volume, _volume.IsMuted);
            VolumeSlider.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(VolumeSlider_PointerWheelChanged), true);

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
            ApplyTheme();
            Backdrop.BackgroundOpacity = s.FlyoutBackgroundOpacity / 100.0;
            foreach (var panel in _mediaPanels)
            {
                ApplyPanelSettings(panel);
            }
            UpdateTopBar();
            AlignButton.IsEnabled = s.PlacementMode == FlyoutWindowPlacementMode.Manual;
            ApplyStackingDirection();
            UpdateRows();

            if (IsShown)
            {
                PlaceWindow(animate: false);
            }
        }

        /// <summary>
        /// Light/dark as chosen; the default follows the Windows mode (like the shell's own
        /// flyouts), not the app mode. Set on the window's root so the acrylic matches too.
        /// </summary>
        public void ApplyTheme()
        {
            var theme = AppSettings.Instance.FlyoutTheme;
            RootGrid.RequestedTheme = theme == ElementTheme.Default ? WindowsTheme.Current : theme;
        }

        #region Showing

        public void ShowVolume()
        {
            _mediaKeyMode = false;
            ShowKind(FlyoutKind.Volume);
        }

        /// <summary>Media key: the media sessions, with the volume row only if enabled in settings.</summary>
        public void ShowMedia()
        {
            _mediaKeyMode = true;
            ShowKind(FlyoutKind.Volume);
        }

        public void ShowBrightness()
        {
            if (!BuildBrightnessRows())
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
            if (kind != FlyoutKind.Volume)
            {
                _mediaKeyMode = false;
            }

            _kind = kind;
            UpdateRows();

            bool appearing = !IsShown || _animatingOut;
            if (!IsShown)
            {
                // A new appearance starts with an auto-hidden top bar tucked away.
                _topBarExpanded = false;
                UpdateTopBar(animate: false);
            }
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

        /// <summary>Which rows are visible for the current kind (and media sessions).</summary>
        private void UpdateRows()
        {
            var s = AppSettings.Instance;
            bool hasMedia = _mediaPanels.Count > 0;
            bool showMedia = _kind == FlyoutKind.Volume && hasMedia && (_mediaKeyMode || s.ShowMediaInVolumeFlyout);
            bool showVolume = _kind == FlyoutKind.Volume && (!_mediaKeyMode || !showMedia || s.ShowVolumeControlInMediaFlyout);

            VolumeRow.Visibility = showVolume ? Visibility.Visible : Visibility.Collapsed;
            BrightnessRow.Visibility = _kind == FlyoutKind.Brightness ? Visibility.Visible : Visibility.Collapsed;
            InfoRow.Visibility = _kind == FlyoutKind.Info ? Visibility.Visible : Visibility.Collapsed;
            DesktopRow.Visibility = _kind == FlyoutKind.VirtualDesktop ? Visibility.Visible : Visibility.Collapsed;
            MediaSection.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
            MediaDivider.Visibility = showVolume ? Visibility.Visible : Visibility.Collapsed;
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
            // Closed with the pointer on it (close button): no exit event will follow.
            _pointerWatchTimer.Stop();
            _pointerOver = false;
            _media?.RefreshTimelineActivity();
        }

        #endregion

        #region Placement and animation

        /// <summary>
        /// Sizes the client area to the content (exactly <see cref="WidthDip"/> wide; the frame
        /// is added outside) and places it at the saved position or the aligned position.
        /// </summary>
        private void PlaceWindow(bool animate)
        {
            double scale = Native.GetDpiForWindow(_hwnd) / 96.0;

            UpdateSessionsLayout();
            RootPanel.Measure(new Size(WidthDip, double.PositiveInfinity));
            double heightDip = Math.Ceiling(RootPanel.DesiredSize.Height);
            _clientSize = new SizeInt32((int)Math.Round(WidthDip * scale), (int)Math.Round(heightDip * scale));

            var s = AppSettings.Instance;
            PointInt32 position;
            if (s.PlacementMode == FlyoutWindowPlacementMode.Manual
                && s.FlyoutPositionX is int savedX && s.FlyoutPositionY is int savedY)
            {
                var area = DisplayArea.GetFromPoint(new PointInt32(savedX, savedY), DisplayAreaFallback.Nearest).WorkArea;

                // Grow upwards (keep the bottom edge) when asked to, or automatically when the
                // flyout sits in the lower half of the screen.
                int y = savedY;
                if (s.FlyoutPositionBottom is int savedBottom)
                {
                    bool up = s.ExpandDirection switch
                    {
                        FlyoutWindowExpandDirection.Up => true,
                        FlyoutWindowExpandDirection.Down => false,
                        _ => (savedY + savedBottom) / 2 > area.Y + area.Height / 2,
                    };
                    if (up)
                    {
                        y = savedBottom - _clientSize.Height;
                    }
                }

                // Keep a saved position on screen (monitor layout may have changed).
                position = new PointInt32(
                    Math.Clamp(savedX, area.X, Math.Max(area.X, area.X + area.Width - _clientSize.Width)),
                    Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - _clientSize.Height)));
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

        /// <summary>Volume/brightness/... rows and the media section, in the configured order.</summary>
        private void ApplyStackingDirection()
        {
            bool mediaFirst = AppSettings.Instance.ContentStackingDirection == StackingDirection.Descending;
            int mediaIndex = RootPanel.Children.IndexOf(MediaSection);
            int wanted = mediaFirst ? 1 : RootPanel.Children.Count - 1; // index 0 is the top bar
            if (mediaIndex != wanted)
            {
                RootPanel.Children.RemoveAt(mediaIndex);
                RootPanel.Children.Insert(wanted, MediaSection);
            }

            // The divider sits between the media and the volume row.
            int dividerIndex = MediaSection.Children.IndexOf(MediaDivider);
            int wantedDivider = mediaFirst ? MediaSection.Children.Count - 1 : 0;
            if (dividerIndex != wantedDivider)
            {
                MediaSection.Children.RemoveAt(dividerIndex);
                MediaSection.Children.Insert(wantedDivider, MediaDivider);
            }
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

        private void UpdateTopBar(bool animate = true)
        {
            var mode = AppSettings.Instance.TopBarVisibility;
            bool autoHide = mode == TopBarVisibility.AutoHide;
            if (mode == TopBarVisibility.Visible)
            {
                // Unpinning keeps the bar out while the pointer is still on it.
                _topBarExpanded = true;
            }

            TopBar.Visibility = mode == TopBarVisibility.Collapsed ? Visibility.Collapsed : Visibility.Visible;
            TopBarHotZone.Visibility = autoHide && !_topBarExpanded ? Visibility.Visible : Visibility.Collapsed;
            AnimateTopBar(autoHide && !_topBarExpanded ? 0 : TopBarHeightDip, animate);

            // Pinned = always visible; unpinned = only while pointing at the top of the flyout.
            PinGlyph.Glyph = autoHide ? "\uE718" : "\uE77A";
            ToolTipService.SetToolTip(PinButton, autoHide ? Strings.PinTopBar : Strings.UnpinTopBar);
        }

        private void SetTopBarExpanded(bool expanded)
        {
            if (_topBarExpanded == expanded || AppSettings.Instance.TopBarVisibility != TopBarVisibility.AutoHide)
            {
                return;
            }

            _topBarExpanded = expanded;
            UpdateTopBar();
        }

        /// <summary>Grows or shrinks the top bar; the window follows its height.</summary>
        private void AnimateTopBar(double target, bool animate)
        {
            if (target == _topBarTo && (_topBarTimer.IsRunning || TopBar.Height == target))
            {
                return;
            }

            _topBarFrom = TopBar.Height;
            _topBarTo = target;

            if (!animate || !IsShown || !AppSettings.Instance.FlyoutAnimationEnabled)
            {
                _topBarTimer.Stop();
                SetTopBarHeight(target);
                return;
            }

            _topBarClock.Restart();
            _topBarTimer.Start();
        }

        private void TopBarAnimationStep()
        {
            double p = Math.Min(1, _topBarClock.Elapsed.TotalMilliseconds / TopBarAnimationMs);
            double eased = Math.Sqrt(1 - (1 - p) * (1 - p)); // circle ease-out, as in the WPF flyout
            SetTopBarHeight(_topBarFrom + (_topBarTo - _topBarFrom) * eased);

            if (p >= 1)
            {
                _topBarTimer.Stop();
            }
        }

        private void SetTopBarHeight(double height)
        {
            TopBar.Height = height;
            TopBar.Opacity = height / TopBarHeightDip;
            if (IsShown)
            {
                PlaceWindow(animate: false);
            }
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

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            var s = AppSettings.Instance;
            s.TopBarVisibility = s.TopBarVisibility == TopBarVisibility.AutoHide ? TopBarVisibility.Visible : TopBarVisibility.AutoHide;
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
            var s = AppSettings.Instance;
            s.FlyoutPositionX = client.X;
            s.FlyoutPositionY = client.Y;
            s.FlyoutPositionBottom = client.Y + _clientSize.Height;
            s.PlacementMode = FlyoutWindowPlacementMode.Manual;
            RestartHideTimer();
        }

        #endregion

        #region Media

        public void AttachMedia(MediaController media)
        {
            _media = media;
            _media.CurrentSessionChanged += (_, _) => UpdateMediaSessions();
            UpdateMediaSessions();
        }

        /// <summary>One <see cref="MediaPanel"/> per session (the selected session first).</summary>
        private void UpdateMediaSessions()
        {
            var sessions = _media?.Sessions ?? Array.Empty<Core.Media.Control.MediaSession>();

            while (_mediaPanels.Count > sessions.Count)
            {
                var last = _mediaPanels[^1];
                last.Session = null;
                SessionsPanel.Children.Remove(last);
                _mediaPanels.RemoveAt(_mediaPanels.Count - 1);
            }

            while (_mediaPanels.Count < sessions.Count)
            {
                var panel = new MediaPanel { Width = WidthDip };
                panel.Interacted += (_, _) => RestartHideTimer();
                panel.LayoutChanged += (_, _) => { if (IsShown) PlaceWindow(animate: false); };
                ApplyPanelSettings(panel);
                _mediaPanels.Add(panel);
                SessionsPanel.Children.Add(panel);
            }

            for (int i = 0; i < sessions.Count; i++)
            {
                _mediaPanels[i].Session = sessions[i];
            }

            UpdateRows();

            if (IsShown)
            {
                PlaceWindow(animate: false);
            }

            _media?.RefreshTimelineActivity();
        }

        private static void ApplyPanelSettings(MediaPanel panel)
        {
            var s = AppSettings.Instance;
            panel.ShowPlayerInfo = s.ShowMediaPlayerInfo;
            panel.ShowSeekbar = s.ShowMediaSeekbar;
            panel.ShowShuffle = s.ShowMediaShuffle;
            panel.ShowRepeat = s.ShowMediaRepeat;
            panel.UseThumbnailAsBackground = s.UseThumbnailAsBackground;
            panel.AlignThumbnailToRight = s.AlignThumbnailToRight;
        }

        /// <summary>
        /// Several sessions: side by side, one page each (with dots), or stacked with at most
        /// <see cref="AppSettings.MaxVerticalSessionControlsCount"/> visible before scrolling.
        /// </summary>
        private void UpdateSessionsLayout()
        {
            var s = AppSettings.Instance;
            bool horizontal = s.SessionsPanelOrientation == SessionsOrientation.Horizontal;
            int count = _mediaPanels.Count;

            SessionsPanel.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
            SessionsScroller.HorizontalScrollMode = horizontal ? ScrollMode.Enabled : ScrollMode.Disabled;
            SessionsScroller.HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
            SessionsScroller.VerticalScrollMode = horizontal ? ScrollMode.Disabled : ScrollMode.Enabled;
            SessionsScroller.VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

            if (horizontal)
            {
                SessionsScroller.MaxHeight = double.PositiveInfinity;
            }
            else
            {
                double max = 0;
                foreach (var panel in _mediaPanels.Take(s.MaxVerticalSessionControlsCount))
                {
                    panel.Measure(new Size(WidthDip, double.PositiveInfinity));
                    max += panel.DesiredSize.Height;
                }
                SessionsScroller.MaxHeight = count > 0 ? Math.Ceiling(max) : double.PositiveInfinity;
            }

            // Page dots for horizontal paging.
            bool dots = horizontal && count > 1;
            SessionDots.Visibility = dots ? Visibility.Visible : Visibility.Collapsed;
            if (dots && SessionDots.Children.Count != count)
            {
                SessionDots.Children.Clear();
                for (int i = 0; i < count; i++)
                {
                    SessionDots.Children.Add(new Border
                    {
                        Height = 6,
                        CornerRadius = new CornerRadius(3),
                        Background = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                    });
                }
            }
            UpdateSessionDots();
        }

        private void SessionsScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateSessionDots();
            RestartHideTimer();
        }

        private void UpdateSessionDots()
        {
            if (SessionDots.Visibility != Visibility.Visible)
            {
                return;
            }

            int page = (int)Math.Round(SessionsScroller.HorizontalOffset / WidthDip);
            for (int i = 0; i < SessionDots.Children.Count; i++)
            {
                var dot = (Border)SessionDots.Children[i];
                bool current = i == page;
                dot.Width = current ? 16 : 6;
                dot.Opacity = current ? 1 : 0.35;
                dot.Background = (Brush)Application.Current.Resources[current ? "AccentFillColorDefaultBrush" : "TextFillColorPrimaryBrush"];
            }
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

        private void RootGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _pointerWatchTimer.Stop();
            _pointerOver = true;
            _hideTimer.Stop();
        }

        private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // Resizing the window under the pointer (top bar growing/shrinking) also raises this.
            if (IsCursorOverWindow())
            {
                _pointerWatchTimer.Start();
                return;
            }

            OnPointerLeft();
        }

        private void OnPointerLeft()
        {
            _pointerWatchTimer.Stop();
            _pointerOver = false;
            SetTopBarExpanded(false);
            RestartHideTimer();
        }

        private bool IsCursorOverWindow()
        {
            if (!Native.GetCursorPos(out var cursor))
            {
                return false;
            }

            var origin = new Native.POINT();
            Native.ClientToScreen(_hwnd, ref origin);
            return cursor.X >= origin.X && cursor.X < origin.X + _clientSize.Width
                && cursor.Y >= origin.Y && cursor.Y < origin.Y + _clientSize.Height;
        }

        private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            // Collapse once the pointer has moved clearly below the bar (not while it is still growing).
            if (_topBarExpanded && !_topBarTimer.IsRunning
                && e.GetCurrentPoint(RootPanel).Position.Y > TopBar.ActualHeight + TopBarHotZoneDip)
            {
                SetTopBarExpanded(false);
            }
        }

        private void TopBarHotZone_PointerEntered(object sender, PointerRoutedEventArgs e) => SetTopBarExpanded(true);

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

        /// <summary>Mouse wheel over the slider: 1 per notch, as in the WPF version.</summary>
        private void VolumeSlider_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            int delta = e.GetCurrentPoint(VolumeSlider).Properties.MouseWheelDelta;
            VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + delta / 120.0, 0, 100);
            e.Handled = true;
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            _volume.IsMuted = !_volume.IsMuted;
            RestartHideTimer();
        }

        #endregion

        #region Brightness

        /// <summary>One row per display that supports brightness; false when there is none.</summary>
        private bool BuildBrightnessRows()
        {
            var controllers = BrightnessManager.Instance?.BrightnessControllers.ToList() ?? new();
            if (controllers.Count == 0)
            {
                BrightnessRow.Children.Clear();
                return false;
            }

            bool same = BrightnessRow.Children.Count == controllers.Count
                && BrightnessRow.Children.Cast<BrightnessRowControl>().Select(r => r.Controller).SequenceEqual(controllers);
            if (!same)
            {
                BrightnessRow.Children.Clear();
                bool several = controllers.Count > 1;
                foreach (var controller in controllers)
                {
                    var row = new BrightnessRowControl(controller, several);
                    row.Interacted += (_, _) => RestartHideTimer();
                    BrightnessRow.Children.Add(row);
                }
            }

            return true;
        }

        #endregion
    }
}
