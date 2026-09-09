using SDAPlusPlus.Core.Hotkeys;

namespace SDAPlusPlus.Core.Tests;

public sealed class HotkeyGestureParserTests
{
    [Theory]
    [InlineData("Ctrl+Shift+S", "Ctrl+Shift+S")]
    [InlineData("alt + f12", "Alt+F12")]
    [InlineData("Control+Right", "Ctrl+Right")]
    [InlineData("Shift+7", "Shift+7")]
    public void ValidGestureIsCanonicalized(string input, string expected)
    {
        Assert.True(HotkeyGestureParser.TryParse(input, out HotkeyGesture? gesture));
        Assert.Equal(expected, gesture!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("S")]
    [InlineData("Win+S")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Ctrl+S")]
    [InlineData("Ctrl+Escape")]
    public void UnsafeOrUnsupportedGestureIsRejected(string? input)
    {
        Assert.False(HotkeyGestureParser.TryParse(input, out _));
    }
}
