using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal static class WindowEffects
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_MAINWINDOW = 2;
        private const int DWMSBT_TRANSIENTWINDOW = 3;
        private const int DWMSBT_TABBEDWINDOW = 4;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        public static void ApplyModernChrome(Form form)
        {
            if (!OperatingSystem.IsWindows() || form == null || form.IsDisposed || form.Handle == IntPtr.Zero)
            {
                return;
            }

            TrySetAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
            TrySetAttribute(form.Handle, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_MAINWINDOW);
            TrySetAttribute(form.Handle, DWMWA_CAPTION_COLOR, ColorToBgr(Branding.WindowBackground));
            TrySetAttribute(form.Handle, DWMWA_BORDER_COLOR, ColorToBgr(Branding.Outline));
            TrySetAttribute(form.Handle, DWMWA_TEXT_COLOR, ColorToBgr(Branding.HeadingText));
        }

        private static void TrySetAttribute(IntPtr handle, int attribute, int value)
        {
            try
            {
                DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
            }
            catch
            {
            }
        }

        private static int ColorToBgr(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }
    }
}
