using Microsoft.UI.Xaml.Controls;
using ModernFlyouts.Properties;
using ModernFlyouts.WinUI.Settings;

namespace ModernFlyouts.WinUI.Pages
{
    public sealed partial class AudioPage : Page
    {
        public AudioPage()
        {
            InitializeComponent();

            EnumChoices.Bind(BackendCombo, () => S.EnhancedMediaBackendMode, v => S.EnhancedMediaBackendMode = v);
            BackendCard.Description = Strings.Settings_RestartRequired;
        }

        public AppSettings S => AppSettings.Instance;

        public double OpacityFor(bool enabled) => enabled ? 1.0 : 0.5;
    }
}
