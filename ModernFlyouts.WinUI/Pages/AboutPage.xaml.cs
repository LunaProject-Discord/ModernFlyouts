using System;
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
            AppCard.Description = $"{Strings.About_Version} {GetVersion()}";
        }

        /// <summary>The package version when installed (what Windows shows), else the assembly's.</summary>
        private static string GetVersion()
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch (InvalidOperationException)
            {
                return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
            }
        }

        private async void LinkCard_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.FrameworkElement { Tag: string url })
            {
                await Windows.System.Launcher.LaunchUriAsync(new System.Uri(url));
            }
        }
    }
}
