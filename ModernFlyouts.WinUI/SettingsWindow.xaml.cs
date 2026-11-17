using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using ModernFlyouts.WinUI.Pages;
using ModernFlyouts.WinUI.Settings;
using Windows.Graphics;

namespace ModernFlyouts.WinUI
{
    /// <summary>
    /// Settings window (Mica, NavigationView). Closing only hides it so it opens instantly next time.
    /// </summary>
    public sealed partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            Title = Properties.Strings.SettingsItem;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            // Caption buttons as tall as the 48 px title bar, so they line up with its content.
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.ico"));
            // 1040 x 720 DIP: wide enough that expander headers keep their switch or list beside the
            // title (CommunityToolkit moves it underneath below 476 px).
            double scale = Interop.Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            AppWindow.Resize(new SizeInt32((int)(1040 * scale), (int)(720 * scale)));
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 500;
                presenter.PreferredMinimumHeight = 500;
            }

            // Keep the window around: hide on close instead of destroying it.
            AppWindow.Closing += (_, e) =>
            {
                e.Cancel = true;
                AppWindow.Hide();
            };

            ApplyTheme();
            AppSettings.Instance.PropertyChanged += Settings_PropertyChanged;
            RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtons();

            // x:Bind on a Window isn't evaluated until activation; apply it now so the
            // menu item texts (used for the page header) are available for the first page.
            Bindings.Update();
            NavView.SelectedItem = NavView.MenuItems.OfType<NavigationViewItem>().First();
        }

        public void ShowAndActivate()
        {
            AppWindow.Show();
            Activate();
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.AppTheme))
            {
                DispatcherQueue.TryEnqueue(ApplyTheme);
            }
        }

        private void ApplyTheme()
        {
            RootGrid.RequestedTheme = AppSettings.Instance.AppTheme;
            UpdateCaptionButtons();
        }

        private void UpdateCaptionButtons()
        {
            // Caption buttons are drawn by the system; match them to the content theme.
            bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        private bool _syncingSelection;

        private static (Type Page, object? Parameter) GetPage(string? tag) => tag switch
        {
            "personalization" => (typeof(PersonalizationPage), null),
            "layout" => (typeof(LayoutPage), null),
            "audio" => (typeof(AudioPage), null),
            "brightness" => (typeof(ModuleTogglePage), ToggleModule.Brightness),
            "airplane" => (typeof(ModuleTogglePage), ToggleModule.AirplaneMode),
            "lockkeys" => (typeof(LockKeysPage), null),
            "virtualdesktop" => (typeof(ModuleTogglePage), ToggleModule.VirtualDesktop),
            "about" => (typeof(AboutPage), null),
            _ => (typeof(GeneralPage), null),
        };

        private IEnumerable<NavigationViewItem> AllItems =>
            NavView.MenuItems.Concat(NavView.FooterMenuItems).OfType<NavigationViewItem>();

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_syncingSelection || args.SelectedItem is not NavigationViewItem item)
            {
                return;
            }

            var (page, parameter) = GetPage(item.Tag as string);
            if (ContentFrame.CurrentSourcePageType == page && Equals(ContentFrame.Content is Page { Tag: var current } ? current : null, parameter))
            {
                return;
            }

            ContentFrame.Navigate(page, parameter, new EntranceNavigationTransitionInfo());
        }

        /// <summary>After any navigation: header and pane selection.</summary>
        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            if (e.Content is Page page)
            {
                page.Tag = e.Parameter; // remembers which module a shared page shows
            }

            var item = AllItems.FirstOrDefault(i =>
            {
                var (type, parameter) = GetPage(i.Tag as string);
                return type == e.SourcePageType && Equals(parameter, e.Parameter);
            });

            if (item != null)
            {
                HeaderText.Text = item.Content?.ToString();
                _syncingSelection = true;
                NavView.SelectedItem = item;
                _syncingSelection = false;
            }
        }

        private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
        }
    }
}
