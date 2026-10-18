using Avalonia.Media;

namespace SDAPlusPlus.Desktop.Services;

public interface IQrCodeRenderer
{
    IImage Render(string content, int size);
}
