using ModernFlyouts.Core.Media.Control;
using System;
using Xunit;

namespace ModernFlyouts.Core.Tests
{
    public class MediaArtworkTests
    {
        [Fact]
        public void CropCopiesTheRequestedRegion()
        {
            // 3x2 image; each pixel's blue channel holds its index.
            var artwork = Create(3, 2);

            var cropped = artwork.Crop(1, 1, 2, 1);

            Assert.Equal(2, cropped.PixelWidth);
            Assert.Equal(1, cropped.PixelHeight);
            Assert.Equal(new byte[] { 4, 0, 0, 255, 5, 0, 0, 255 }, cropped.Pixels);
        }

        [Fact]
        public void CropOfWholeImageReturnsSameInstance()
        {
            var artwork = Create(2, 2);

            Assert.Same(artwork, artwork.Crop(0, 0, 2, 2));
        }

        [Fact]
        public void CropIsClampedToImageBounds()
        {
            var artwork = Create(4, 4);

            var cropped = artwork.Crop(2, 2, 10, 10);

            Assert.Equal(2, cropped.PixelWidth);
            Assert.Equal(2, cropped.PixelHeight);
        }

        [Fact]
        public void MismatchedBufferIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new MediaArtwork(new byte[3], 1, 1));
        }

        private static MediaArtwork Create(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                pixels[i * 4] = (byte)i;
                pixels[i * 4 + 3] = 255;
            }

            return new MediaArtwork(pixels, width, height);
        }
    }
}
