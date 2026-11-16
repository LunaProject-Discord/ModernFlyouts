using Microsoft.UI.Xaml.Controls;
using ModernFlyouts.WinUI.Settings;

namespace ModernFlyouts.WinUI.Pages
{
    public sealed partial class PersonalizationPage : Page
    {
        public PersonalizationPage()
        {
            InitializeComponent();

            EnumChoices.Bind(FlyoutThemeCombo, () => S.FlyoutTheme, v => S.FlyoutTheme = v);
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
    }
}
