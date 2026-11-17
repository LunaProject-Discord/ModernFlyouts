using System;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using ModernFlyouts.Core.Display;

namespace ModernFlyouts.WinUI.Controls
{
    /// <summary>
    /// One brightness row (icon | slider | value) for one display, laid out like the volume row.
    /// With several displays the icon carries the display number, as in the WPF version.
    /// </summary>
    internal sealed class BrightnessRowControl : Grid
    {
        private readonly BrightnessIcon _glyph;
        private readonly TextBlock _index;
        private readonly Slider _slider;
        private readonly TextBlock _value;
        private bool _updating;

        public BrightnessRowControl(BrightnessController controller, bool showIndex)
        {
            Controller = controller;
            Height = 48;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

            var icon = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _glyph = new BrightnessIcon();
            _index = new TextBlock
            {
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Margin = new Thickness(0, 0, -10, -10),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = showIndex ? Visibility.Visible : Visibility.Collapsed,
            };
            icon.Children.Add(_glyph);
            icon.Children.Add(_index);
            if (controller.AssociatedDisplayMonitor is { } monitor)
            {
                _index.Text = monitor.Index.ToString();
                ToolTipService.SetToolTip(icon, monitor.DisplayName);
            }
            Children.Add(icon);

            _slider = new Slider
            {
                StepFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                IsThumbToolTipEnabled = false,
            };
            SetColumn(_slider, 1);
            _slider.ValueChanged += Slider_ValueChanged;
            _slider.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(Slider_PointerWheelChanged), true);
            Children.Add(_slider);

            _value = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["FlyoutValueTextStyle"],
            };
            SetColumn(_value, 2);
            Children.Add(_value);

            controller.PropertyChanged += Controller_PropertyChanged;
            Unloaded += (_, _) => controller.PropertyChanged -= Controller_PropertyChanged;
            Update();
        }

        public BrightnessController Controller { get; }

#if DEBUG
        /// <summary>Shows the icon at another level without touching the display (--demo-brightness).</summary>
        internal void PreviewIconLevel(double level) => _glyph.Level = level;
#endif

        /// <summary>Raised when the user changes the value (keeps the flyout open).</summary>
        public event EventHandler? Interacted;

        private void Controller_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            DispatcherQueue?.TryEnqueue(Update);
        }

        private void Update()
        {
            _updating = true;
            _slider.Minimum = Controller.Minimum;
            _slider.Maximum = Controller.Maximum;
            _slider.Value = Controller.Brightness;
            _updating = false;

            _value.Text = Controller.Brightness.ToString("0");
            // The sun's rays follow the level.
            double range = Controller.Maximum - Controller.Minimum;
            _glyph.Level = range > 0 ? (Controller.Brightness - Controller.Minimum) / range : 1;
        }

        private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            Controller.Brightness = e.NewValue;
            Interacted?.Invoke(this, EventArgs.Empty);
        }

        private void Slider_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            int delta = e.GetCurrentPoint(_slider).Properties.MouseWheelDelta;
            _slider.Value = Math.Clamp(_slider.Value + delta / 120.0, _slider.Minimum, _slider.Maximum);
            e.Handled = true;
        }
    }
}
