using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ModernFlyouts.Core.Media.Control
{
    /// <summary>
    /// UI-framework independent, immutable image: premultiplied BGRA8 pixels.
    /// The UI layer converts it to its own image type (and may cache the result).
    /// Safe to share across threads.
    /// </summary>
    public sealed class MediaArtwork
    {
        public MediaArtwork(byte[] pixels, int width, int height)
        {
            if (width <= 0 || height <= 0 || pixels == null || pixels.Length != width * height * 4)
            {
                throw new ArgumentException("Pixel buffer does not match the given size.");
            }

            Pixels = pixels;
            PixelWidth = width;
            PixelHeight = height;
        }

        /// <summary>Premultiplied BGRA8, row-major, stride = <see cref="PixelWidth"/> * 4.</summary>
        public byte[] Pixels { get; }

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public int Stride => PixelWidth * 4;

        public static async Task<MediaArtwork> DecodeAsync(IRandomAccessStreamReference reference)
        {
            if (reference == null)
            {
                return null;
            }

            using var stream = await reference.OpenReadAsync();
            return await DecodeAsync(stream);
        }

        public static async Task<MediaArtwork> DecodeAsync(System.IO.Stream stream)
        {
            if (stream == null)
            {
                return null;
            }

            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            // Copy first: the source stream may not be seekable or may be disposed by its owner.
            var copy = new System.IO.MemoryStream();
            await stream.CopyToAsync(copy);
            if (copy.Length == 0)
            {
                return null;
            }

            copy.Position = 0;
            using var randomAccess = System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(copy);
            return await DecodeAsync(randomAccess);
        }

        public static async Task<MediaArtwork> DecodeAsync(IRandomAccessStream stream)
        {
            if (stream == null || stream.Size == 0)
            {
                return null;
            }

            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);

            return new MediaArtwork(pixelData.DetachPixelData(), (int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight);
        }

        public MediaArtwork Crop(int x, int y, int width, int height)
        {
            x = Math.Clamp(x, 0, PixelWidth - 1);
            y = Math.Clamp(y, 0, PixelHeight - 1);
            width = Math.Clamp(width, 1, PixelWidth - x);
            height = Math.Clamp(height, 1, PixelHeight - y);

            if (x == 0 && y == 0 && width == PixelWidth && height == PixelHeight)
            {
                return this;
            }

            var result = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                System.Buffer.BlockCopy(Pixels, ((y + row) * PixelWidth + x) * 4, result, row * width * 4, width * 4);
            }

            return new MediaArtwork(result, width, height);
        }
    }
}
