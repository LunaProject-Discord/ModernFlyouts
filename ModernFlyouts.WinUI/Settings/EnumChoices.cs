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

            return Strings.ResourceManager.GetString($"Enums.{value.GetType().Name}.{value}", Strings.Culture)
                ?? value.ToString()!;
        }

        public static void Bind<T>(ComboBox comboBox, Func<T> get, Action<T> set) where T : struct, Enum
        {
            var values = Enum.GetValues<T>();
            comboBox.ItemsSource = values.Select(v => GetDisplayName(v)).ToList();
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
