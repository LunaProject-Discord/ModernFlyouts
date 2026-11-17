using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ModernFlyouts.Properties;
using ModernFlyouts.WinUI.Settings;

namespace ModernFlyouts.WinUI.Pages
{
    public enum ToggleModule
    {
        Brightness,
        AirplaneMode,
        VirtualDesktop,
    }

    public sealed partial class ModuleTogglePage : Page
    {
        public ModuleTogglePage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var s = AppSettings.Instance;
            (string glyph, string header, Func<bool> get, Action<bool> set) = (ToggleModule)e.Parameter switch
            {
                ToggleModule.Brightness => ("", Strings.Settings_EnableModule_Brightness,
                    (Func<bool>)(() => s.BrightnessModuleEnabled), (Action<bool>)(v => s.BrightnessModuleEnabled = v)),
                ToggleModule.AirplaneMode => ("", Strings.Settings_EnableModule_Airplane,
                    () => s.AirplaneModeModuleEnabled, v => s.AirplaneModeModuleEnabled = v),
                _ => ("", Strings.Settings_EnableModule_VirtualDesktop,
                    () => s.VirtualDesktopModuleEnabled, v => s.VirtualDesktopModuleEnabled = v),
            };

            EnableIcon.Glyph = glyph;
            EnableCard.Header = header;
            EnableSwitch.IsOn = get();
            EnableSwitch.Toggled += (_, _) => set(EnableSwitch.IsOn);
        }
    }
}
