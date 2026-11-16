using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using ModernFlyouts.Properties;

namespace ModernFlyouts.WinUI.Pages
{
    public sealed partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
            VersionText.Text = $"{Strings.About_Version} {Assembly.GetExecutingAssembly().GetName().Version}";
        }
    }
}
