using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModernFlyouts.Properties;
using ModernFlyouts.WinUI.Settings;

namespace ModernFlyouts.WinUI.Pages
{
    public sealed partial class PersonalizationPage : Page
    {
        public PersonalizationPage()
        {
            InitializeComponent();

            // The flyout's default follows the Windows mode (taskbar), not the app mode.
            EnumChoices.Bind(FlyoutThemeCombo, Enum.GetValues<ElementTheme>(), () => S.FlyoutTheme, v => S.FlyoutTheme = v,
                v => v == ElementTheme.Default ? Strings.Settings_FollowWindowsMode : EnumChoices.GetDisplayName(v));
            EnumChoices.Bind(AppThemeCombo, () => S.AppTheme, v => S.AppTheme = v);
            EnumChoices.Bind(TopBarCombo, () => S.TopBarVisibility, v => S.TopBarVisibility = v);

            // ms, as in the WPF version; show the recommended value like the original star hint.
            TimeoutCard.Description = $"★ {AppSettings.RecommendedFlyoutTimeout} ms";
            TimeoutBox.Value = S.FlyoutTimeout;
            TimeoutBox.ValueChanged += (_, e) =>
            {
                if (!double.IsNaN(e.NewValue))
                {
                    S.FlyoutTimeout = (int)e.NewValue;
                }
            };
        }

        public AppSettings S => AppSettings.Instance;

        public string PercentText(double value) => $"{value:0}%";
    }
}
