using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ModernFlyouts.Properties;

namespace ModernFlyouts.WinUI.Settings
{
    /// <summary>
    /// Applies the UI language at startup: the language chosen in settings, otherwise the first
    /// Windows display language. Covers both our Strings (resx) and WinUI's own control strings.
    /// </summary>
    internal static class Localization
    {
        /// <summary>Cultures with a Strings.*.resx translation (English is the neutral resource).</summary>
        public static IReadOnlyList<string> SupportedCultures { get; } = new[]
        {
            "en-US", "en-GB", "af", "ar", "da-DK", "de", "el", "es", "fa", "fr", "he", "hi", "hu", "id", "is",
            "it", "ja", "ko", "ms", "nb", "nl", "pl", "pt", "ru", "sv", "tr", "uk", "vi", "zh-CN", "zh-TW",
        };

        public static CultureInfo Current { get; private set; } = CultureInfo.CurrentUICulture;

        /// <summary>The language setting in effect (applied at startup; changes need a restart).</summary>
        public static string AppliedSetting { get; private set; } = string.Empty;

        public static void Apply()
        {
            AppliedSetting = AppSettings.Instance.Language;
            Current = Resolve(AppliedSetting);

            CultureInfo.DefaultThreadCurrentUICulture = Current;
            CultureInfo.CurrentUICulture = Current;
            Strings.Culture = Current;

            try
            {
                // WinUI controls (ToggleSwitch "On/Off", NumberBox, ...) follow the app language.
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = Current.Name;
            }
            catch (Exception ex)
            {
                App.Log($"language override failed: {ex.Message}");
            }
        }

        private static CultureInfo Resolve(string setting)
        {
            if (!string.IsNullOrEmpty(setting))
            {
                try { return CultureInfo.GetCultureInfo(setting); }
                catch (CultureNotFoundException) { }
            }

            // Packaged apps get a CurrentUICulture limited to the manifest languages, so read the
            // user's preference directly.
            foreach (string tag in Windows.System.UserProfile.GlobalizationPreferences.Languages)
            {
                try { return CultureInfo.GetCultureInfo(tag); }
                catch (CultureNotFoundException) { }
            }

            return CultureInfo.CurrentUICulture;
        }

        /// <summary>"System default" plus every supported culture, shown in its own language.</summary>
        public static IReadOnlyList<(string Name, string Display)> GetChoices()
        {
            var choices = new List<(string, string)> { (string.Empty, Strings.Settings_SystemDefault) };
            choices.AddRange(SupportedCultures
                .Select(name => (name, CultureInfo.GetCultureInfo(name).NativeName))
                .OrderBy(c => c.NativeName, StringComparer.CurrentCulture));
            return choices;
        }
    }
}
