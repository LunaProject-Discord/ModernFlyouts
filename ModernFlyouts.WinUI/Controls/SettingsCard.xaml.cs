using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ModernFlyouts.WinUI.Controls
{
    /// <summary>
    /// Small stand-in for CommunityToolkit's SettingsCard (which targets Windows App SDK 1.x):
    /// icon glyph, header, optional description and an action control on the right.
    /// </summary>
    [ContentProperty(Name = nameof(Action))]
    public sealed partial class SettingsCard : UserControl
    {
        // CommunityToolkit SettingsCardWrapThreshold: narrower cards put the action under the header.
        private const double WrapThreshold = 476;

        public SettingsCard()
        {
            InitializeComponent();
            SizeChanged += (_, e) => UpdateWrap(e.NewSize.Width < WrapThreshold);
        }

        private bool _wrapped;

        private void UpdateWrap(bool wrap)
        {
            if (wrap == _wrapped)
            {
                return;
            }

            _wrapped = wrap;
            Grid.SetRow(ActionPresenter, wrap ? 1 : 0);
            Grid.SetColumn(ActionPresenter, wrap ? 1 : 2);
            ActionPresenter.HorizontalAlignment = wrap ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            ActionPresenter.Margin = wrap ? new Thickness(0, 8, 0, 0) : new Thickness(16, 0, 0, 0);
        }

        public string Glyph
        {
            get => IconElement.Glyph;
            set => IconElement.Glyph = value;
        }

        public string Header
        {
            get => HeaderText.Text;
            set => HeaderText.Text = value;
        }

        public string Description
        {
            get => DescriptionText.Text;
            set
            {
                DescriptionText.Text = value;
                DescriptionText.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object? Action
        {
            get => _action;
            set
            {
                _action = value;
                ActionPresenter.Content = value is ToggleSwitch toggle ? WrapToggleSwitch(toggle) : value;
            }
        }

        private object? _action;

        /// <summary>
        /// Windows Settings layout for switches: the state text ("Off") on the left and the switch
        /// itself flush with the right edge. WinUI draws the text to the right of the switch and
        /// reserves a wide minimum width, so the text is shown separately and the switch is cut
        /// down to its track.
        /// </summary>
        private static FrameworkElement WrapToggleSwitch(ToggleSwitch toggle)
        {
            var state = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };

            // OnContent/OffContent may be set later (x:Bind), so read them when updating.
            void Update() => state.Text = toggle.IsOn
                ? toggle.OnContent as string ?? Properties.Strings.Settings_On
                : toggle.OffContent as string ?? Properties.Strings.Settings_Off;

            toggle.MinWidth = 0;
            toggle.Width = 40; // the track; WinUI's own state text (to its right) is clipped away
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.Margin = new Thickness(0, -4, 0, -4); // 40 px tall template; rows stay 68 px like other cards
            toggle.Toggled += (_, _) => Update();
            toggle.RegisterPropertyChangedCallback(ToggleSwitch.OnContentProperty, (_, _) => Update());
            toggle.RegisterPropertyChangedCallback(ToggleSwitch.OffContentProperty, (_, _) => Update());
            toggle.RegisterPropertyChangedCallback(ToggleSwitch.IsOnProperty, (_, _) => Update());
            Update();

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(state);
            panel.Children.Add(toggle);
            return panel;
        }
    }
}
