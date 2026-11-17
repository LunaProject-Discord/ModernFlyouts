using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ModernFlyouts.WinUI.Settings;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace ModernFlyouts.WinUI.Interop
{
    /// <summary>
    /// The notification area icon's context menu as a WinUI <see cref="MenuFlyout"/>.
    /// A MenuFlyout needs a XAML root, so it is opened from an invisible 1x1 window placed at the
    /// cursor; the menu itself is a windowed popup and may extend past that window.
    /// Like the shell's own menus it follows the Windows mode (taskbar) theme.
    /// </summary>
    internal sealed class TrayMenu
    {
        private readonly Window _window;
        private readonly Grid _root;
        private readonly MenuFlyout _menu;
        private readonly IntPtr _hwnd;
        private PointInt32 _pendingPosition;
        private bool _loaded;
        private bool _showWhenLoaded;

        public TrayMenu(string settingsText, string exitText)
        {
            _root = new Grid();
            _window = new Window { Content = _root, Title = "ModernFlyouts tray menu" };
            _hwnd = WindowNative.GetWindowHandle(_window);

            _window.AppWindow.SetPresenter(OverlappedPresenter.CreateForContextMenu());
            _window.AppWindow.IsShownInSwitchers = false;

            // Invisible host: tool window (no taskbar button) drawn fully transparent.
            long exStyle = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(exStyle | Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED));
            Native.SetLayeredWindowAttributes(_hwnd, 0, 0, Native.LWA_ALPHA);

            var settings = new MenuFlyoutItem { Text = settingsText, Icon = new FontIcon { Glyph = "" } };
            settings.Click += (_, _) => SettingsRequested?.Invoke();
            var exit = new MenuFlyoutItem { Text = exitText, Icon = new FontIcon { Glyph = "" } };
            exit.Click += (_, _) => ExitRequested?.Invoke();

            _menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
            _menu.Items.Add(settings);
            _menu.Items.Add(new MenuFlyoutSeparator());
            _menu.Items.Add(exit);
            _menu.Closed += (_, _) => _window.AppWindow.Hide();

            _root.Loaded += (_, _) =>
            {
                _loaded = true;
                if (_showWhenLoaded)
                {
                    _showWhenLoaded = false;
                    OpenMenu();
                }
            };
        }

        public event Action? SettingsRequested;

        public event Action? ExitRequested;

        /// <summary>Opens the menu at a screen position (physical pixels), e.g. the cursor.</summary>
        public void Show(int x, int y)
        {
            _pendingPosition = new PointInt32(x, y);
            _root.RequestedTheme = WindowsTheme.Current;

            _window.AppWindow.MoveAndResize(new RectInt32(x, y, 1, 1));
            _window.AppWindow.Show();
            _window.Activate();
            // Foreground so that clicking anywhere else dismisses the menu.
            Native.SetForegroundWindow(_hwnd);

            if (_loaded)
            {
                OpenMenu();
            }
            else
            {
                _showWhenLoaded = true;
            }
        }

        private void OpenMenu()
        {
            // The host window sits at the cursor, so the menu opens at its origin; WinUI flips it
            // up/left when there is no room (e.g. next to the taskbar).
            _menu.ShowAt(_root, new FlyoutShowOptions
            {
                Position = new Point(0, 0),
                ShowMode = FlyoutShowMode.Standard,
            });
        }
    }
}
