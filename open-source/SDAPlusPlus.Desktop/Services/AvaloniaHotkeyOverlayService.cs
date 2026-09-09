using SDAPlusPlus.Desktop.Views;

namespace SDAPlusPlus.Desktop.Services;

public sealed class AvaloniaHotkeyOverlayService : IHotkeyOverlayService
{
    private HotkeyOverlayWindow? _window;

    public void Show(string title, string message)
    {
        _window ??= new HotkeyOverlayWindow();
        _window.ShowStatus(title, message);
    }

    public void Dispose()
    {
        _window?.Close();
        _window = null;
    }
}
