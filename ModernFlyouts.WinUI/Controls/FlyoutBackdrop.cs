using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ModernFlyouts.WinUI.Controls
{
    /// <summary>
    /// Desktop acrylic whose tint can be made more transparent ("background opacity" setting).
    /// Always rendered as active: the flyout never takes focus, and an inactive window would
    /// fall back to a solid color.
    /// </summary>
    internal sealed class FlyoutBackdrop : SystemBackdrop
    {
        private DesktopAcrylicController? _controller;
        private readonly SystemBackdropConfiguration _configuration = new() { IsInputActive = true };
        private double _opacity = 1.0;
        private SystemBackdropTheme _theme = SystemBackdropTheme.Dark;

        /// <summary>
        /// Light or dark, set by the flyout (it resolves "follow the Windows mode" itself).
        /// Asking XAML for the default configuration instead throws when the theme changes.
        /// </summary>
        public ElementTheme Theme
        {
            get => _theme == SystemBackdropTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
            set
            {
                _theme = value == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
                Apply();
            }
        }

        /// <summary>0 (blur only) to 1 (standard acrylic).</summary>
        public double BackgroundOpacity
        {
            get => _opacity;
            set
            {
                _opacity = System.Math.Clamp(value, 0, 1);
                Apply();
            }
        }

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);

            _controller = new DesktopAcrylicController();
            Apply();
            _controller.AddSystemBackdropTarget(connectedTarget);
            _controller.SetSystemBackdropConfiguration(_configuration);
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            base.OnTargetDisconnected(disconnectedTarget);

            if (_controller != null)
            {
                _controller.RemoveSystemBackdropTarget(disconnectedTarget);
                _controller.Dispose();
                _controller = null;
            }
        }

        protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
        {
            // Nothing to do: the theme comes from the Theme property. (The base implementation
            // and GetDefaultSystemBackdropConfiguration are not used; the latter throws
            // ArgumentException for this target when the theme changes.)
        }

        private void Apply()
        {
            _configuration.Theme = _theme;
            if (_controller == null)
            {
                return;
            }

            // Default desktop acrylic values for each theme, scaled by the opacity setting.
            bool dark = _configuration.Theme == SystemBackdropTheme.Dark;
            var tint = dark ? Color.FromArgb(255, 0x2C, 0x2C, 0x2C) : Color.FromArgb(255, 0xFC, 0xFC, 0xFC);
            _controller.TintColor = tint;
            _controller.TintOpacity = (float)((dark ? 0.15 : 0.0) * _opacity);
            _controller.LuminosityOpacity = (float)((dark ? 0.96 : 0.85) * _opacity);
            _controller.FallbackColor = tint;
        }
    }
}
