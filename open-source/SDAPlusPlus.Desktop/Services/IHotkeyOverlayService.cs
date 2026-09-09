namespace SDAPlusPlus.Desktop.Services;

public interface IHotkeyOverlayService : IDisposable
{
    void Show(string title, string message);
}
