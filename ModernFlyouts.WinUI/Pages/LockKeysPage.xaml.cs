using Microsoft.UI.Xaml.Controls;
using ModernFlyouts.WinUI.Settings;

namespace ModernFlyouts.WinUI.Pages
{
    public sealed partial class LockKeysPage : Page
    {
        public LockKeysPage()
        {
            InitializeComponent();
        }

        public AppSettings S => AppSettings.Instance;

        public double OpacityFor(bool enabled) => enabled ? 1.0 : 0.5;
    }
}
