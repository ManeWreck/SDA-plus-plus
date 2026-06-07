using Newtonsoft.Json;
using System;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    public enum QrCaptureMode
    {
        FullDesktop = 0,
        MonitorUnderCursor = 1,
        AreaAroundCursor = 2
    }

    public sealed class HotkeyBinding
    {
        [JsonProperty("ctrl")]
        public bool Control { get; set; }

        [JsonProperty("shift")]
        public bool Shift { get; set; }

        [JsonProperty("alt")]
        public bool Alt { get; set; }

        [JsonProperty("key")]
        public int KeyCode { get; set; }

        public HotkeyBinding Clone()
        {
            return new HotkeyBinding
            {
                Control = Control,
                Shift = Shift,
                Alt = Alt,
                KeyCode = KeyCode
            };
        }
    }

    internal static class HotkeyBindingHelper
    {
        public static HotkeyBinding CreateDefault(Keys keyCode)
        {
            return new HotkeyBinding
            {
                Control = true,
                Shift = true,
                Alt = false,
                KeyCode = (int)keyCode
            };
        }

        public static HotkeyBinding Normalize(HotkeyBinding binding, Keys fallback)
        {
            if (binding == null)
            {
                return CreateDefault(fallback);
            }

            if (binding.KeyCode == 0 || IsModifierKey((Keys)binding.KeyCode))
            {
                binding.KeyCode = (int)fallback;
            }

            return binding;
        }

        public static string ToDisplayText(HotkeyBinding binding)
        {
            if (binding == null || binding.KeyCode == 0)
            {
                return "Не задано";
            }

            string text = "";
            if (binding.Control)
            {
                text += "Ctrl+";
            }

            if (binding.Shift)
            {
                text += "Shift+";
            }

            if (binding.Alt)
            {
                text += "Alt+";
            }

            text += ((Keys)binding.KeyCode).ToString();
            return text;
        }

        public static HotkeyBinding FromKeyEvent(KeyEventArgs e)
        {
            if (IsModifierKey(e.KeyCode))
            {
                return null;
            }

            return new HotkeyBinding
            {
                Control = e.Control,
                Shift = e.Shift,
                Alt = e.Alt,
                KeyCode = (int)e.KeyCode
            };
        }

        public static bool Equals(HotkeyBinding left, HotkeyBinding right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return left.Control == right.Control
                && left.Shift == right.Shift
                && left.Alt == right.Alt
                && left.KeyCode == right.KeyCode;
        }

        public static bool IsValid(HotkeyBinding binding)
        {
            return binding != null && binding.KeyCode != 0 && !IsModifierKey((Keys)binding.KeyCode);
        }

        private static bool IsModifierKey(Keys keyCode)
        {
            return keyCode == Keys.ControlKey
                || keyCode == Keys.ShiftKey
                || keyCode == Keys.Menu
                || keyCode == Keys.LControlKey
                || keyCode == Keys.RControlKey
                || keyCode == Keys.LShiftKey
                || keyCode == Keys.RShiftKey
                || keyCode == Keys.LMenu
                || keyCode == Keys.RMenu;
        }
    }
}
