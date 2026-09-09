using SDAPlusPlus.Core.Hotkeys;

namespace SDAPlusPlus.Desktop.Services;

public enum HotkeyRegistrationFailure
{
    None,
    UnsupportedPlatform,
    Duplicate,
    AlreadyInUse,
    ServiceUnavailable
}

public sealed record HotkeyRegistrationResult(bool Success, HotkeyRegistrationFailure Failure = HotkeyRegistrationFailure.None);

public sealed record GlobalHotkeyConfiguration(
    bool Enabled,
    HotkeyGesture ToggleQr,
    HotkeyGesture ScanQr,
    HotkeyGesture PreviousAccount,
    HotkeyGesture NextAccount,
    HotkeyGesture AutoLoginAll,
    HotkeyGesture Confirmations);

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? ScanQrRequested;
    event EventHandler? ConfirmationsRequested;
    event EventHandler? ToggleQrRequested;
    event EventHandler? PreviousAccountRequested;
    event EventHandler? NextAccountRequested;
    event EventHandler? AutoLoginAllRequested;

    HotkeyRegistrationResult Configure(GlobalHotkeyConfiguration configuration);
}
