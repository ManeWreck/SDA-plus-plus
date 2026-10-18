using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SDAPlusPlus.Desktop.Views;

public partial class HotkeyOverlayWindow : Window
{
    private readonly DispatcherTimer _hideTimer;

    public HotkeyOverlayWindow()
    {
        InitializeComponent();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    public void ShowStatus(string title, string message)
    {
        OverlayTitle.Text = title;
        OverlayMessage.Text = message;
        if (!IsVisible)
        {
            Show();
        }

        Screen? screen = Screens.Primary;
        if (screen is not null)
        {
            PixelRect area = screen.WorkingArea;
            Position = new PixelPoint(area.X + ((area.Width - (int)Width) / 2), area.Y + 28);
        }

        _hideTimer.Stop();
        _hideTimer.Start();
    }
}
