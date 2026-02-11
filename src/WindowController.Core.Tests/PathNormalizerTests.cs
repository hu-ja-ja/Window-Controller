using WindowController.Core;

namespace WindowController.Core.Tests;

public class PathNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalize_EmptyOrNull_ReturnsEmpty(string? input, string expected)
    {
        Assert.Equal(expected, PathNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_DrivePath_NoChange()
    {
        var result = PathNormalizer.Normalize(@"C:\Program Files\App\app.exe");
        Assert.Equal(@"C:\Program Files\App\app.exe", result);
    }

    [Fact]
    public void Normalize_DoubleBackslash_Collapsed()
    {
        var result = PathNormalizer.Normalize(@"C:\\Program Files\\App\\app.exe");
        Assert.Equal(@"C:\Program Files\App\app.exe", result);
    }

    [Fact]
    public void Normalize_QuadBackslash_Collapsed()
    {
        var result = PathNormalizer.Normalize("C:\\\\\\\\foo\\\\\\\\bar.exe");
        Assert.Equal(@"C:\foo\bar.exe", result);
    }

    [Fact]
    public void Normalize_UncPath_PreservesPrefix()
    {
        var result = PathNormalizer.Normalize(@"\\server\share\file.txt");
        Assert.Equal(@"\\server\share\file.txt", result);
    }

    [Fact]
    public void Normalize_UncPath_DoubleBackslash_Collapsed()
    {
        var result = PathNormalizer.Normalize(@"\\\\server\\\\share\\file.txt");
        Assert.Equal(@"\\server\share\file.txt", result);
    }
}
