using System.Runtime.InteropServices;
using SDAPlusPlus.Core.Hotkeys;

namespace SDAPlusPlus.Desktop.Services;

public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const int ScanQrId = 1;
    private const int ConfirmationsId = 2;
    private const int ToggleQrId = 3;
    private const int PreviousAccountId = 4;
    private const int NextAccountId = 5;
    private const int AutoLoginAllId = 6;
    private const uint WmHotkey = 0x0312;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmConfigure = 0x8001;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr MessageOnlyWindow = new(-3);

    private readonly object _sync = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly string _className = "SDAPlusPlusHotkeys-" + Guid.NewGuid().ToString("N");
    private readonly Thread? _thread;
    private WindowProcedure? _windowProcedure;
    private IntPtr _window;
    private GlobalHotkeyConfiguration? _pendingConfiguration;
    private GlobalHotkeyConfiguration? _activeConfiguration;
    private HotkeyRegistrationResult _lastResult = new(false, HotkeyRegistrationFailure.ServiceUnavailable);
    private bool _disposed;

    public WindowsGlobalHotkeyService()
    {
        if (!OperatingSystem.IsWindows())
        {
            _lastResult = new HotkeyRegistrationResult(false, HotkeyRegistrationFailure.UnsupportedPlatform);
            return;
        }

        _thread = new Thread(MessageThread)
        {
            IsBackground = true,
            Name = "SDA++ global hotkeys"
        };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            _lastResult = new HotkeyRegistrationResult(false, HotkeyRegistrationFailure.ServiceUnavailable);
        }
    }

    public event EventHandler? ScanQrRequested;
    public event EventHandler? ConfirmationsRequested;
    public event EventHandler? ToggleQrRequested;
    public event EventHandler? PreviousAccountRequested;
    public event EventHandler? NextAccountRequested;
    public event EventHandler? AutoLoginAllRequested;

    public HotkeyRegistrationResult Configure(GlobalHotkeyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (_disposed || !OperatingSystem.IsWindows())
        {
            return new HotkeyRegistrationResult(false, HotkeyRegistrationFailure.UnsupportedPlatform);
        }
        if (_window == IntPtr.Zero)
        {
            return _lastResult;
        }
        HotkeyGesture[] gestures =
        [
            configuration.ToggleQr,
            configuration.ScanQr,
            configuration.PreviousAccount,
            configuration.NextAccount,
            configuration.AutoLoginAll,
            configuration.Confirmations
        ];
        if (gestures.Distinct().Count() != gestures.Length)
        {
            return new HotkeyRegistrationResult(false, HotkeyRegistrationFailure.Duplicate);
        }

        lock (_sync)
        {
            _pendingConfiguration = configuration;
            SendMessage(_window, WmConfigure, IntPtr.Zero, IntPtr.Zero);
            return _lastResult;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window != IntPtr.Zero)
        {
            PostMessage(_window, WmClose, IntPtr.Zero, IntPtr.Zero);
        }
        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void MessageThread()
    {
        IntPtr instance = GetModuleHandle(null);
        _windowProcedure = WindowProc;
        var windowClass = new WindowClass
        {
            WindowProcedure = _windowProcedure,
            Instance = instance,
            ClassName = _className
        };

        ushort atom = RegisterClass(ref windowClass);
        if (atom == 0)
        {
            _lastResult = new HotkeyRegistrationResult(false, HotkeyRegistrationFailure.ServiceUnavailable);
            _ready.Set();
            return;
        }

        try
        {
            _window = CreateWindowEx(
                0,
                _className,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                MessageOnlyWindow,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            if (_window == IntPtr.Zero)
            {
                _lastResult = new HotkeyRegistrationResult(false, HotkeyRegistrationFailure.ServiceUnavailable);
                _ready.Set();
                return;
            }

            _lastResult = new HotkeyRegistrationResult(true);
            _ready.Set();
            while (GetMessage(out Message message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            UnregisterHotKey(_window, ScanQrId);
            UnregisterHotKey(_window, ConfirmationsId);
            UnregisterHotKey(_window, ToggleQrId);
            UnregisterHotKey(_window, PreviousAccountId);
            UnregisterHotKey(_window, NextAccountId);
            UnregisterHotKey(_window, AutoLoginAllId);
            _window = IntPtr.Zero;
            UnregisterClass(_className, instance);
            if (!_ready.IsSet)
            {
                _ready.Set();
            }
        }
    }

    private IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey)
        {
            if (wParam.ToInt32() == ScanQrId)
            {
                ScanQrRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (wParam.ToInt32() == ConfirmationsId)
            {
                ConfirmationsRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (wParam.ToInt32() == ToggleQrId) ToggleQrRequested?.Invoke(this, EventArgs.Empty);
            else if (wParam.ToInt32() == PreviousAccountId) PreviousAccountRequested?.Invoke(this, EventArgs.Empty);
            else if (wParam.ToInt32() == NextAccountId) NextAccountRequested?.Invoke(this, EventArgs.Empty);
            else if (wParam.ToInt32() == AutoLoginAllId) AutoLoginAllRequested?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        if (message == WmConfigure)
        {
            ApplyPendingConfiguration(window);
            return IntPtr.Zero;
        }

        if (message == WmClose)
        {
            DestroyWindow(window);
            return IntPtr.Zero;
        }

        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ApplyPendingConfiguration(IntPtr window)
    {
        GlobalHotkeyConfiguration? configuration = _pendingConfiguration;
        GlobalHotkeyConfiguration? previous = _activeConfiguration;
        UnregisterHotKey(window, ScanQrId);
        UnregisterHotKey(window, ConfirmationsId);
        UnregisterHotKey(window, ToggleQrId);
        UnregisterHotKey(window, PreviousAccountId);
        UnregisterHotKey(window, NextAccountId);
        UnregisterHotKey(window, AutoLoginAllId);
        if (configuration is null || !configuration.Enabled)
        {
            _activeConfiguration = configuration;
            _lastResult = new HotkeyRegistrationResult(true);
            return;
        }

        if (!TryRegisterConfiguration(window, configuration))
        {
            UnregisterHotKey(window, ScanQrId);
            UnregisterHotKey(window, ConfirmationsId);
            UnregisterHotKey(window, ToggleQrId);
            UnregisterHotKey(window, PreviousAccountId);
            UnregisterHotKey(window, NextAccountId);
            UnregisterHotKey(window, AutoLoginAllId);
            if (previous is { Enabled: true } && TryRegisterConfiguration(window, previous))
            {
                _activeConfiguration = previous;
            }
            else
            {
                _activeConfiguration = null;
            }
            _lastResult = new HotkeyRegistrationResult(false, HotkeyRegistrationFailure.AlreadyInUse);
            return;
        }

        _activeConfiguration = configuration;
        _lastResult = new HotkeyRegistrationResult(true);
    }

    private static bool TryRegisterConfiguration(IntPtr window, GlobalHotkeyConfiguration configuration)
    {
        (int Id, HotkeyGesture Gesture)[] bindings =
        [
            (ToggleQrId, configuration.ToggleQr),
            (ScanQrId, configuration.ScanQr),
            (PreviousAccountId, configuration.PreviousAccount),
            (NextAccountId, configuration.NextAccount),
            (AutoLoginAllId, configuration.AutoLoginAll),
            (ConfirmationsId, configuration.Confirmations)
        ];
        foreach ((int id, HotkeyGesture gesture) in bindings)
        {
            if (!RegisterHotKey(window, id, ToNativeModifiers(gesture.Modifiers), ToVirtualKey(gesture.Key)))
            {
                return false;
            }
        }

        return true;
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint native = ModNoRepeat;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) native |= ModControl;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) native |= ModShift;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) native |= ModAlt;
        return native;
    }

    private static uint ToVirtualKey(string key)
    {
        if (key.Length == 1)
        {
            return char.ToUpperInvariant(key[0]);
        }
        if (key.StartsWith('F') && int.TryParse(key[1..], out int functionKey))
        {
            return (uint)(0x70 + functionKey - 1);
        }
        return key switch
        {
            "Left" => 0x25,
            "Up" => 0x26,
            "Right" => 0x27,
            "Down" => 0x28,
            _ => throw new InvalidOperationException("Unsupported hotkey key.")
        };
    }

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WindowProcedure WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Value;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
