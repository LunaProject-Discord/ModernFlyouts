using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModernFlyouts.WinUI.Settings;

namespace ModernFlyouts.WinUI.Pages
{
    public sealed partial class GeneralPage : Page
    {
        public GeneralPage()
        {
            InitializeComponent();

            EnumChoices.Bind(DefaultFlyoutCombo, () => S.DefaultFlyout, v => S.DefaultFlyout = v);
            UpdateWarning();
            DefaultFlyoutCombo.SelectionChanged += (_, _) => UpdateWarning();
        }

        public AppSettings S => AppSettings.Instance;

        private void UpdateWarning()
        {
            // The original warns that switching away from ModernFlyouts needs an app restart to fully apply.
            DefaultFlyoutWarning.Visibility = S.DefaultFlyout == DefaultFlyout.ModernFlyouts ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            S.ResetToDefaults();
            Frame.Navigate(typeof(GeneralPage)); // refresh the controls that were set up in code
        }
    }
}
