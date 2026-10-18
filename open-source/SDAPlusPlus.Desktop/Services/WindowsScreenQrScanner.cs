using System.ComponentModel;
using System.Runtime.InteropServices;
using SDAPlusPlus.Core.Qr;
using ZXing;
using ZXing.Common;

namespace SDAPlusPlus.Desktop.Services;

public sealed class WindowsScreenQrScanner : IScreenQrScanner
{
    public Task<string?> ScanAsync(
        QrCaptureMode captureMode,
        int cursorAreaSize,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Screen QR capture is currently available on Windows only.");
        }

        return Task.Run(() => CaptureAndDecode(captureMode, cursorAreaSize, cancellationToken), cancellationToken);
    }

    private static string? CaptureAndDecode(
        QrCaptureMode captureMode,
        int cursorAreaSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CaptureBounds bounds = ResolveCaptureBounds(captureMode, cursorAreaSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Windows did not return valid desktop bounds.");
        }

        byte[] pixels = CaptureBgra(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        cancellationToken.ThrowIfCancellationRequested();
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = [BarcodeFormat.QR_CODE]
            }
        };
        Result? result = reader.Decode(pixels, bounds.Width, bounds.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        return string.IsNullOrWhiteSpace(result?.Text) ? null : result.Text.Trim();
    }

    private static CaptureBounds ResolveCaptureBounds(QrCaptureMode mode, int cursorAreaSize)
    {
        if (mode == QrCaptureMode.FullDesktop)
        {
            return new CaptureBounds(
                GetSystemMetrics(SmXVirtualScreen),
                GetSystemMetrics(SmYVirtualScreen),
                GetSystemMetrics(SmCxVirtualScreen),
                GetSystemMetrics(SmCyVirtualScreen));
        }

        if (!GetCursorPos(out Point cursor))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        IntPtr monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        int monitorWidth = info.Monitor.Right - info.Monitor.Left;
        int monitorHeight = info.Monitor.Bottom - info.Monitor.Top;
        if (mode == QrCaptureMode.MonitorUnderCursor)
        {
            return new CaptureBounds(info.Monitor.Left, info.Monitor.Top, monitorWidth, monitorHeight);
        }

        int width = Math.Min(Math.Clamp(cursorAreaSize, 250, 2000), monitorWidth);
        int height = Math.Min(Math.Clamp(cursorAreaSize, 250, 2000), monitorHeight);
        int left = Math.Clamp(cursor.X - (width / 2), info.Monitor.Left, info.Monitor.Right - width);
        int top = Math.Clamp(cursor.Y - (height / 2), info.Monitor.Top, info.Monitor.Bottom - height);
        return new CaptureBounds(left, top, width, height);
    }

    private static byte[] CaptureBgra(int left, int top, int width, int height)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, SrcCopy | CaptureBlt))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb
                }
            };
            byte[] pixels = new byte[checked(width * height * 4)];
            if (GetDIBits(memoryDc, bitmap, 0, (uint)height, pixels, ref info, DibRgbColors) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return pixels;
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                SelectObject(memoryDc, previous);
            }
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }
            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;
    private const uint BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const uint MonitorDefaultToNearest = 2;

    private readonly record struct CaptureBounds(int Left, int Top, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr destination,
        int x,
        int y,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        uint operation);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr dc,
        IntPtr bitmap,
        uint start,
        uint lines,
        [Out] byte[] bits,
        ref BitmapInfo info,
        uint usage);
}
