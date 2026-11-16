using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            SetTitleBar(TitleBar);
            AppWindow.Resize(new SizeInt32(1100, 760));

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

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item)
            {
                return;
            }

            HeaderText.Text = item.Content?.ToString();

            (Type page, object? parameter) = (item.Tag as string) switch
            {
                "personalization" => (typeof(PersonalizationPage), null),
                "layout" => (typeof(LayoutPage), null),
                "audio" => (typeof(AudioPage), null),
                "brightness" => (typeof(ModuleTogglePage), ToggleModule.Brightness),
                "airplane" => (typeof(ModuleTogglePage), ToggleModule.AirplaneMode),
                "lockkeys" => (typeof(LockKeysPage), null),
                "virtualdesktop" => (typeof(ModuleTogglePage), ToggleModule.VirtualDesktop),
                "about" => (typeof(AboutPage), null),
                _ => (typeof(GeneralPage), (object?)null),
            };

            ContentFrame.Navigate(page, parameter);
        }
    }
}
