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
        public SettingsCard()
        {
            InitializeComponent();
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
            get => ActionPresenter.Content;
            set => ActionPresenter.Content = value;
        }
    }
}
