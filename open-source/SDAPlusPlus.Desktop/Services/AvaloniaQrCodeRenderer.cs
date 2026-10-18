using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace SDAPlusPlus.Desktop.Services;

public sealed class AvaloniaQrCodeRenderer : IQrCodeRenderer
{
    public IImage Render(string content, int size)
    {
        BarcodeWriterPixelData writer = new()
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new EncodingOptions { Width = size, Height = size, Margin = 2, PureBarcode = true }
        };
        PixelData pixels = writer.Write(content);
        WriteableBitmap bitmap = new(
            new PixelSize(pixels.Width, pixels.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        using ILockedFramebuffer frame = bitmap.Lock();
        Marshal.Copy(pixels.Pixels, 0, frame.Address, pixels.Pixels.Length);
        return bitmap;
    }
}
