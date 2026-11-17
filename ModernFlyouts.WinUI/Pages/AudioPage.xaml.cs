using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

            EnumChoices.Bind(OrientationCombo, () => S.SessionsPanelOrientation, v => S.SessionsPanelOrientation = v);

            // Left / right is a choice of position, not on/off.
            ThumbnailAlignmentCombo.ItemsSource = new[] { Strings.Settings_Left, Strings.Settings_Right };
            ThumbnailAlignmentCombo.SelectedIndex = S.AlignThumbnailToRight ? 1 : 0;
            ThumbnailAlignmentCombo.SelectionChanged += (_, _) => S.AlignThumbnailToRight = ThumbnailAlignmentCombo.SelectedIndex == 1;
            EnumChoices.Bind(PinnedPriorityCombo, () => S.PinnedAppPriorityMode, v => S.PinnedAppPriorityMode = v);
            EnumChoices.Bind(FilteringModeCombo, () => S.MediaAppFilteringMode, v => S.MediaAppFilteringMode = v);

            UpdateMaxSessionsCard();
            OrientationCombo.SelectionChanged += (_, _) => UpdateMaxSessionsCard();

            ToolTipService.SetToolTip(PinnedAppPicker, Strings.Settings_ChooseFromMediaApps);
            ToolTipService.SetToolTip(FilterAppPicker, Strings.Settings_ChooseFromMediaApps);
        }

        public AppSettings S => AppSettings.Instance;

        public double OpacityFor(bool enabled) => enabled ? 1.0 : 0.5;

        private void UpdateMaxSessionsCard()
        {
            // The count only matters when sessions are stacked vertically.
            MaxSessionsCard.Visibility = S.SessionsPanelOrientation == SessionsOrientation.Vertical
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Menu of the media apps seen so far: sets the pinned app or adds to the filter list.</summary>
        private void AppPicker_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            bool pinned = (string)button.Tag == "pinned";
            var menu = new MenuFlyout();

            var apps = App.Media?.KnownSources.OrderBy(a => a.Value).ToList() ?? new();
            if (apps.Count == 0)
            {
                menu.Items.Add(new MenuFlyoutItem { Text = Strings.Settings_NoMediaAppsYet, IsEnabled = false });
            }

            foreach (var (id, name) in apps)
            {
                var item = new MenuFlyoutItem { Text = name == id ? id : $"{name}  ({id})" };
                item.Click += (_, _) =>
                {
                    if (pinned)
                    {
                        S.PinnedMediaAppUserModelId = id;
                        return;
                    }

                    var entries = S.MediaAppFilterList
                        .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                    if (!entries.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        entries.Add(id);
                    }
                    S.MediaAppFilterList = string.Join("\r", entries);
                };
                menu.Items.Add(item);
            }

            menu.ShowAt(button, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
        }
    }
}
