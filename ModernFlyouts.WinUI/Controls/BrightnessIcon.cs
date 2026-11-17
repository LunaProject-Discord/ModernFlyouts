using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ModernFlyouts.WinUI.Controls
{
    /// <summary>
    /// Sun icon whose rays grow with the brightness (Segoe Fluent only has a "low" and a "high"
    /// brightness glyph). Like the volume icon, the full sun is drawn faintly behind the current
    /// level. 16 x 16 with 1 px strokes, matching the 16 px font icons next to it.
    /// </summary>
    internal sealed class BrightnessIcon : Grid
    {
        private const double Size = 16;
        private const double Center = Size / 2;
        private const double CoreRadius = 2.75;
        private const double RayStart = 4.75;  // distance from the center
        private const double RayLength = 3;    // at 100 % (ends 0.25 px inside the 16 px box)
        private const int RayCount = 8;

        private readonly Ellipse _core;
        private readonly Path _shadowRays;
        private readonly Path _rays;
        private double _level = 1;

        public BrightnessIcon()
        {
            Width = Size;
            Height = Size;

            _shadowRays = CreateRaysPath(1);
            _rays = CreateRaysPath(1);
            _core = new Ellipse
            {
                Width = CoreRadius * 2,
                Height = CoreRadius * 2,
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Children.Add(_shadowRays);
            Children.Add(_rays);
            Children.Add(_core);

            // Brushes follow the flyout theme (application brushes would follow the app mode).
            ActualThemeChanged += (_, _) => ApplyColors();
            Loaded += (_, _) => ApplyColors();
        }

        /// <summary>0 to 1.</summary>
        public double Level
        {
            get => _level;
            set
            {
                value = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1);
                if (Math.Abs(value - _level) < 0.005)
                {
                    return;
                }

                _level = value;
                _rays.Data = CreateRays(value);
            }
        }

        private static Path CreateRaysPath(double level) => new()
        {
            Data = CreateRays(level),
            StrokeThickness = 1,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };

        private static Geometry CreateRays(double level)
        {
            var geometry = new PathGeometry();
            double length = RayLength * level;
            if (length < 0.25)
            {
                return geometry; // only the core at the lowest brightness
            }

            for (int i = 0; i < RayCount; i++)
            {
                double angle = i * Math.PI * 2 / RayCount;
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                var figure = new PathFigure
                {
                    StartPoint = new Point(Center + cos * RayStart, Center + sin * RayStart),
                    IsClosed = false,
                };
                figure.Segments.Add(new LineSegment
                {
                    Point = new Point(Center + cos * (RayStart + length), Center + sin * (RayStart + length)),
                });
                geometry.Figures.Add(figure);
            }

            return geometry;
        }

        private void ApplyColors()
        {
            bool dark = ActualTheme == ElementTheme.Dark;
            // TextFillColorPrimary / TextFillColorDisabled of each theme.
            var primary = new SolidColorBrush(dark ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE4, 0, 0, 0));
            var faint = new SolidColorBrush(dark ? Color.FromArgb(0x5D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x5C, 0, 0, 0));
            _core.Stroke = primary;
            _rays.Stroke = primary;
            _shadowRays.Stroke = faint;
        }
    }
}
