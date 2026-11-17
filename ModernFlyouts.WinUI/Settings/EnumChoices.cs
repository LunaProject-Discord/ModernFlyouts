using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModernFlyouts.Properties;

namespace ModernFlyouts.WinUI.Settings
{
    /// <summary>
    /// Fills a ComboBox with the values of an enum (localized like the WPF version:
    /// "Enums.{Type}.{Value}" resources) and keeps it in sync with a setting.
    /// </summary>
    internal static class EnumChoices
    {
        public static string GetDisplayName(object value)
        {
            if (value is ElementTheme.Default)
            {
                return Strings.Settings_SystemDefault;
            }

            // SessionsOrientation reuses the WPF version's "Orientation" translations.
            string typeName = value is SessionsOrientation ? "Orientation" : value.GetType().Name;
            return Strings.ResourceManager.GetString($"Enums.{typeName}.{value}", Strings.Culture)
                ?? value.ToString()!;
        }

        public static void Bind<T>(ComboBox comboBox, Func<T> get, Action<T> set) where T : struct, Enum
            => Bind(comboBox, Enum.GetValues<T>(), get, set);

        /// <summary>Only the given <paramref name="values"/> are offered.</summary>
        public static void Bind<T>(ComboBox comboBox, T[] values, Func<T> get, Action<T> set) where T : struct, Enum
            => Bind(comboBox, values, get, set, v => GetDisplayName(v));

        /// <summary>With custom texts (e.g. what "default" means for this setting).</summary>
        public static void Bind<T>(ComboBox comboBox, T[] values, Func<T> get, Action<T> set, Func<T, string> displayName) where T : struct, Enum
        {
            comboBox.ItemsSource = values.Select(displayName).ToList();
            comboBox.SelectedIndex = Array.IndexOf(values, get());
            comboBox.SelectionChanged += (_, _) =>
            {
                if (comboBox.SelectedIndex >= 0)
                {
                    set(values[comboBox.SelectedIndex]);
                }
            };
        }
    }
}
