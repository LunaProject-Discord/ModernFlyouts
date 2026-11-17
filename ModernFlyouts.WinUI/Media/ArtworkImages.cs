using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ModernFlyouts.Core.Media.Control;

namespace ModernFlyouts.WinUI.Media
{
    /// <summary>
    /// Converts Core's <see cref="MediaArtwork"/> (premultiplied BGRA8) to WinUI images.
    /// Results are cached per artwork instance, so re-displaying the same track is free.
    /// Must be called on the UI thread.
    /// </summary>
    internal static class ArtworkImages
    {
        private static readonly ConditionalWeakTable<MediaArtwork, WriteableBitmap> Cache = new();

        public static ImageSource? ToImageSource(MediaArtwork? artwork)
        {
            if (artwork == null)
            {
                return null;
            }

            if (Cache.TryGetValue(artwork, out var cached))
            {
                return cached;
            }

            var bitmap = new WriteableBitmap(artwork.PixelWidth, artwork.PixelHeight);
            using (Stream pixels = bitmap.PixelBuffer.AsStream())
            {
                pixels.Write(artwork.Pixels, 0, artwork.Pixels.Length);
            }
            bitmap.Invalidate();

            Cache.AddOrUpdate(artwork, bitmap);
            return bitmap;
        }

        private static readonly ConditionalWeakTable<MediaArtwork, Dictionary<bool, WriteableBitmap>> FadedCache = new();

        /// <summary>
        /// The artwork faded out radially from the left or right edge (the WPF version's
        /// "thumbnail as background" opacity mask, baked into the pixels since WinUI has no OpacityMask).
        /// </summary>
        public static ImageSource? ToFadedBackground(MediaArtwork? artwork, bool fromRight)
        {
            if (artwork == null || artwork.PixelWidth == 0 || artwork.PixelHeight == 0)
            {
                return null;
            }

            var variants = FadedCache.GetOrCreateValue(artwork);
            if (variants.TryGetValue(fromRight, out var cached))
            {
                return cached;
            }

            int w = artwork.PixelWidth, h = artwork.PixelHeight;
            byte[] source = artwork.Pixels;
            byte[] faded = new byte[source.Length];
            double cx = fromRight ? 1.0 : 0.0;
            for (int y = 0; y < h; y++)
            {
                // Shown UniformToFill in a wide panel: only the middle band is visible, so
                // flatten the vertical falloff.
                double dy = ((y + 0.5) / h - 0.5) * 0.5;
                for (int x = 0; x < w; x++)
                {
                    double dx = (x + 0.5) / w - cx;
                    double alpha = Math.Clamp(1 - Math.Sqrt(dx * dx + dy * dy), 0, 1);
                    int i = (y * w + x) * 4;
                    // Premultiplied BGRA: scale every channel.
                    faded[i] = (byte)(source[i] * alpha);
                    faded[i + 1] = (byte)(source[i + 1] * alpha);
                    faded[i + 2] = (byte)(source[i + 2] * alpha);
                    faded[i + 3] = (byte)(source[i + 3] * alpha);
                }
            }

            var bitmap = new WriteableBitmap(w, h);
            using (Stream pixels = bitmap.PixelBuffer.AsStream())
            {
                pixels.Write(faded, 0, faded.Length);
            }
            bitmap.Invalidate();

            variants[fromRight] = bitmap;
            return bitmap;
        }
    }
}
