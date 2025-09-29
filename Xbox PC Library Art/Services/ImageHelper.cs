using System.IO;
using System.Windows.Media.Imaging;

namespace XboxSteamCoverArtFixer.Services
{
    public static class ImageHelper
    {
        public static byte[] EnsurePng(byte[] bytes)
        {
            // Quick check: PNG magic
            if (bytes.Length > 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return bytes;
            }

            // Re-encode to PNG using WPF BitmapDecoder/Encoder
            using var input = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));

            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }

        internal static object WriteBackupOnce(string filePath)
        {
            throw new NotImplementedException();
        }
    }
}
