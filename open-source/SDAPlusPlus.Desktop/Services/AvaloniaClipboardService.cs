using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace SDAPlusPlus.Desktop.Services;

public sealed class AvaloniaClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
