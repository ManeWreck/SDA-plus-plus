namespace SDAPlusPlus.Core.Hotkeys;

public static class HotkeyGestureParser
{
    public static bool TryParse(string? value, out HotkeyGesture? gesture)
    {
        gesture = null;
        string[] parts = value?.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length < 2)
        {
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (string rawPart in parts)
        {
            string part = rawPart.Trim();
            if (part.Equals("ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("control", StringComparison.OrdinalIgnoreCase))
            {
                if (modifiers.HasFlag(HotkeyModifiers.Control)) return false;
                modifiers |= HotkeyModifiers.Control;
            }
            else if (part.Equals("shift", StringComparison.OrdinalIgnoreCase))
            {
                if (modifiers.HasFlag(HotkeyModifiers.Shift)) return false;
                modifiers |= HotkeyModifiers.Shift;
            }
            else if (part.Equals("alt", StringComparison.OrdinalIgnoreCase))
            {
                if (modifiers.HasFlag(HotkeyModifiers.Alt)) return false;
                modifiers |= HotkeyModifiers.Alt;
            }
            else if (key is null && TryNormalizeKey(part, out string? normalized))
            {
                key = normalized;
            }
            else
            {
                return false;
            }
        }

        if (modifiers == HotkeyModifiers.None || key is null)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    private static bool TryNormalizeKey(string value, out string? key)
    {
        key = null;
        if (value.Length == 1 && char.IsAsciiLetterOrDigit(value[0]))
        {
            key = char.ToUpperInvariant(value[0]).ToString();
            return true;
        }

        if (value.Length is 2 or 3 && value[0] is 'f' or 'F' &&
            int.TryParse(value[1..], out int functionKey) && functionKey is >= 1 and <= 12)
        {
            key = "F" + functionKey;
            return true;
        }

        string? arrow = value.ToLowerInvariant() switch
        {
            "left" => "Left",
            "right" => "Right",
            "up" => "Up",
            "down" => "Down",
            _ => null
        };
        key = arrow;
        return arrow is not null;
    }
}
