using SDAPlusPlus.Core.Qr;

namespace SDAPlusPlus.Desktop.Services;

public interface IScreenQrScanner
{
    Task<string?> ScanAsync(
        QrCaptureMode captureMode,
        int cursorAreaSize,
        CancellationToken cancellationToken = default);
}
