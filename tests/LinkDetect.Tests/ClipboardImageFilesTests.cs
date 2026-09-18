using LinkDetect.Services;

namespace LinkDetect.Tests;

public sealed class ClipboardImageFilesTests
{
    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.JPG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.bmp", true)]
    [InlineData("a.txt", false)]
    [InlineData("a", false)]
    [InlineData(null, false)]
    public void IsSupportedImageFileMatchesExtensions(string? path, bool expected)
    {
        Assert.Equal(expected, ClipboardImageFiles.IsSupportedImageFile(path));
    }

    [Fact]
    public void PickFirstSupportedFileSkipsUnsupportedEntries()
    {
        var picked = ClipboardImageFiles.PickFirstSupportedFile(
            new string?[] { "a.txt", null, "b.jpg", "c.png" });

        Assert.Equal("b.jpg", picked);
    }

    [Fact]
    public void PickFirstSupportedFileReturnsNullWhenNoneMatch()
    {
        Assert.Null(ClipboardImageFiles.PickFirstSupportedFile(new string?[] { "a.txt", null }));
        Assert.Null(ClipboardImageFiles.PickFirstSupportedFile(null));
    }
}
