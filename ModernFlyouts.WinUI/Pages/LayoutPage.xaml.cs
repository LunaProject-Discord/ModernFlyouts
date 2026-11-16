using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using ModernFlyouts.Core.Display;
using ModernFlyouts.Core.UI;
using ModernFlyouts.Properties;
using ModernFlyouts.WinUI.Settings;

namespace ModernFlyouts.WinUI.Pages
{
    public sealed partial class LayoutPage : Page
    {
        private static readonly FlyoutWindowAlignments[] Cells =
        {
            FlyoutWindowAlignments.Top | FlyoutWindowAlignments.Left,
            FlyoutWindowAlignments.Top,
            FlyoutWindowAlignments.Top | FlyoutWindowAlignments.Right,
            FlyoutWindowAlignments.Left,
            FlyoutWindowAlignments.Center,
            FlyoutWindowAlignments.Right,
            FlyoutWindowAlignments.Bottom | FlyoutWindowAlignments.Left,
            FlyoutWindowAlignments.Bottom,
            FlyoutWindowAlignments.Bottom | FlyoutWindowAlignments.Right,
        };

        private readonly List<ToggleButton> _cells = new();
        private readonly List<string> _displayIds = new();
        private bool _updating;

        public LayoutPage()
        {
            InitializeComponent();

            EnumChoices.Bind(ModeCombo, () => S.PlacementMode, v => S.PlacementMode = v);

            for (int i = 0; i < Cells.Length; i++)
            {
                var alignment = Cells[i];
                var cell = new ToggleButton { Style = (Style)Resources["PositionCellStyle"] };
                ToolTipService.SetToolTip(cell, DescribeAlignment(alignment));
                // Checked/Unchecked so keyboard and UI Automation work; the cells act as radio buttons.
                cell.Checked += (_, _) =>
                {
                    if (!_updating) S.FlyoutAlignment = alignment;
                    UpdateState();
                };
                cell.Unchecked += (_, _) => { if (!_updating) UpdateState(); };
                Grid.SetRow(cell, i / 3);
                Grid.SetColumn(cell, i % 3);
                PositionGrid.Children.Add(cell);
                _cells.Add(cell);
            }

            MarginBox.Value = S.FlyoutMargin;
            MarginBox.ValueChanged += (_, e) =>
            {
                if (!double.IsNaN(e.NewValue)) S.FlyoutMargin = (int)e.NewValue;
            };

            FillDisplays();
            DisplayCombo.SelectionChanged += (_, _) =>
            {
                if (!_updating && DisplayCombo.SelectedIndex >= 0)
                {
                    S.FlyoutMonitorId = _displayIds[DisplayCombo.SelectedIndex];
                }
            };

            UpdateState();
        }

        public AppSettings S => AppSettings.Instance;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            S.PropertyChanged += Settings_PropertyChanged;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            S.PropertyChanged -= Settings_PropertyChanged;
        }

        // Dragging the flyout or "reset position" change the mode while this page is open.
        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (e.PropertyName == nameof(AppSettings.PlacementMode))
                {
                    var modes = System.Enum.GetValues<FlyoutWindowPlacementMode>();
                    ModeCombo.SelectedIndex = System.Array.IndexOf(modes, S.PlacementMode);
                }
                UpdateState();
            });
        }

        private void UpdateState()
        {
            bool manual = S.PlacementMode == FlyoutWindowPlacementMode.Manual;
            ModeCard.Description = manual ? Strings.Settings_ManualPlacementDescription : string.Empty;
            AutoPanel.Opacity = manual ? 0.5 : 1;
            AutoPanel.IsHitTestVisible = !manual;

            bool wasUpdating = _updating;
            _updating = true;
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i].IsChecked = Cells[i] == S.FlyoutAlignment;
            }
            _updating = wasUpdating;
        }

        private void FillDisplays()
        {
            _updating = true;
            var names = new List<string> { Strings.Settings_PrimaryDisplay };
            _displayIds.Clear();
            _displayIds.Add(string.Empty);

            var monitors = DisplayManager.Instance?.DisplayMonitors.ToList() ?? new();
            foreach (var monitor in monitors)
            {
                names.Add($"{monitor.Index}. {monitor.DisplayName}");
                _displayIds.Add(monitor.DeviceId);
            }

            DisplayCombo.ItemsSource = names;
            int selected = _displayIds.IndexOf(S.FlyoutMonitorId);
            DisplayCombo.SelectedIndex = selected >= 0 ? selected : 0;
            _updating = false;
        }

        private static string DescribeAlignment(FlyoutWindowAlignments alignment)
        {
            if (alignment == FlyoutWindowAlignments.Center)
            {
                return EnumChoices.GetDisplayName(FlyoutWindowAlignments.Center);
            }

            var parts = new List<string>();
            foreach (var flag in new[] { FlyoutWindowAlignments.Top, FlyoutWindowAlignments.Bottom, FlyoutWindowAlignments.Left, FlyoutWindowAlignments.Right })
            {
                if (alignment.HasFlag(flag)) parts.Add(EnumChoices.GetDisplayName(flag));
            }
            return string.Join(" / ", parts);
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            // Return to the automatic position chosen above and show the flyout there.
            S.PlacementMode = FlyoutWindowPlacementMode.Auto;
            ((App)Application.Current).PreviewFlyout();
        }
    }
}
