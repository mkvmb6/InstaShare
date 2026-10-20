using QRCoder;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InstaShare.Services
{
    internal static class QrCodes
    {
        public static ImageSource Create(string text)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule: 10);

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(png);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
