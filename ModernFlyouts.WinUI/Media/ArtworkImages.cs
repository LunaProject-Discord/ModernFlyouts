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
    }
}
