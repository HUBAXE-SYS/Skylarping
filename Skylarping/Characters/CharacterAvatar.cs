/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Turns any picture (a character card PNG, or the user's own PNG/JPEG/GIF/BMP) into the
// square PNG Skylarping uses as a contact picture. Card art is usually a tall portrait,
// so the square is taken near the top, where the face normally is, rather than from the
// middle. Re-encoding also drops the card JSON a card PNG carries, so the avatar stays small.
/*==========================================================*/

using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Skylarping.Characters
{
    public static class CharacterAvatar
    {
        public const int Size = 256;

        public const string OpenFilter =
            "Pictures (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files (*.*)|*.*";

        /// <summary>Returns a Size x Size PNG (smaller if the picture is), or throws CharacterCardException.</summary>
        public static byte[] Normalize(byte[] image)
        {
            if (image == null || image.Length == 0)
                throw new CharacterCardException("The picture is empty.");

            BitmapSource source;
            try
            {
                using (var stream = new MemoryStream(image))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    source = decoder.Frames[0]; // first frame of an animated GIF
                }
            }
            catch (Exception ex)
            {
                throw new CharacterCardException("That picture couldn't be opened. Use a PNG, JPEG, GIF or BMP file.", ex);
            }

            int w = source.PixelWidth, h = source.PixelHeight;
            if (w <= 0 || h <= 0)
                throw new CharacterCardException("That picture is empty.");

            // Square crop: centred horizontally; for portraits, a fifth of the way down.
            int side = Math.Min(w, h);
            int x = (w - side) / 2;
            int y = (h - side) / 5;
            BitmapSource result = new CroppedBitmap(source, new Int32Rect(x, y, side, side));

            if (side > Size)
            {
                double scale = (double)Size / side;
                result = new TransformedBitmap(result, new ScaleTransform(scale, scale));
            }

            // Flatten odd pixel formats (indexed, CMYK, 16-bit) to plain 32-bit for the encoder.
            if (result.Format != PixelFormats.Bgra32 && result.Format != PixelFormats.Bgr32)
                result = new FormatConvertedBitmap(result, PixelFormats.Bgra32, null, 0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result));
            using (var output = new MemoryStream())
            {
                encoder.Save(output);
                return output.ToArray();
            }
        }
    }
}
