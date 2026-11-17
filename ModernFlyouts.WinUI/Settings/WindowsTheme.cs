using Microsoft.UI.Xaml;

namespace ModernFlyouts.WinUI.Settings
{
    /// <summary>
    /// The Windows mode (taskbar, Start, notifications: "Choose your default Windows mode"),
    /// which can differ from the app mode that XAML follows by default.
    /// </summary>
    internal static class WindowsTheme
    {
        public static bool IsLight
        {
            get
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
                }
                catch
                {
                    return false; // Windows mode is dark unless set otherwise
                }
            }
        }

        public static ElementTheme Current => IsLight ? ElementTheme.Light : ElementTheme.Dark;
    }
}
