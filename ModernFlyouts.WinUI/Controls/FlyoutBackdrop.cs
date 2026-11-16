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

            _configuration.Theme = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot).Theme;
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
            // The flyout theme (or the system theme) changed.
            _configuration.Theme = GetDefaultSystemBackdropConfiguration(target, xamlRoot).Theme;
            Apply();
        }

        private void Apply()
        {
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
