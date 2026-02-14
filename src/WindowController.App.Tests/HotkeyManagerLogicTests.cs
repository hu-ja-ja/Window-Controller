using System.Windows.Input;
using WindowController.App;
using Xunit;

namespace WindowController.App.Tests;

public class HotkeyManagerLogicTests
{
    [Theory]
    [InlineData(Key.A, "A")]
    [InlineData(Key.Z, "Z")]
    [InlineData(Key.D0, "0")]
    [InlineData(Key.D9, "9")]
    [InlineData(Key.NumPad0, "NumPad0")]
    [InlineData(Key.NumPad7, "NumPad7")]
    [InlineData(Key.F13, "F13")]
    [InlineData(Key.F24, "F24")]
    [InlineData(Key.PageUp, "PageUp")]
    [InlineData(Key.PageDown, "PageDown")]
    [InlineData(Key.Escape, "Escape")]
    public void GetKeyString_ReturnsExpected(Key key, string expected)
    {
        Assert.Equal(expected, HotkeyManager.GetKeyString(key));
    }

    [Theory]
    [InlineData("ESC", 0x1B)]
    [InlineData("Escape", 0x1B)]
    [InlineData("PGUP", 0x21)]
    [InlineData("PageDown", 0x22)]
    [InlineData("DEL", 0x2E)]
    [InlineData("Insert", 0x2D)]
    [InlineData("PrtSc", 0x2C)]
    [InlineData("Space", 0x20)]
    public void GetVirtualKeyCode_SpecialStrings_ReturnExpected(string key, int expectedVk)
    {
        Assert.Equal(expectedVk, HotkeyManager.GetVirtualKeyCode(key));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("[", 0)]
    [InlineData("-", 0)]
    public void GetVirtualKeyCode_UnsupportedCharacters_ReturnZero(string key, int expectedVk)
    {
        Assert.Equal(expectedVk, HotkeyManager.GetVirtualKeyCode(key));
    }

    [Theory]
    [InlineData("F13", 0x7C)]
    [InlineData("F24", 0x87)]
    public void GetVirtualKeyCode_FunctionKeys_ReturnExpected(string key, int expectedVk)
    {
        Assert.Equal(expectedVk, HotkeyManager.GetVirtualKeyCode(key));
    }
}
