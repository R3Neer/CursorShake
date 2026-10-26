using CursorShake.Windows.Configuration;
using Xunit;

namespace CursorShake.Windows.Tests;

public sealed class WorkspaceResourcesTests
{
    [Fact]
    public void NestedResourceIsReadWithoutChangingItsBytes()
    {
        using var fixture = new CursorFixture();
        byte[] expected = [0, 1, 127, 128, 255];
        string path = fixture.Write("themes/custom/images/cursor.cur", expected);
        Assert.Equal(expected, WorkspaceResources.ReadContained(Path.Combine(fixture.DirectoryPath, "themes/custom"), "images/cursor.cur", expected.Length));
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("../outside.cur")]
    [InlineData("images/../../outside.cur")]
    [InlineData("images/./cursor.cur")]
    [InlineData("images//cursor.cur")]
    [InlineData("images\\..\\outside.cur")]
    [InlineData("C:\\outside.cur")]
    [InlineData("/outside.cur")]
    [InlineData("cursor.cur:alternate")]
    [InlineData("")]
    public void UnsafeResourcePathsAreRejectedBeforeReading(string relative)
    {
        using var fixture = new CursorFixture();
        fixture.Write("outside.cur", [42]);
        var error = Assert.Throws<ConfigurationException>(() => WorkspaceResources.ReadContained(fixture.DirectoryPath, relative, 16));
        Assert.Equal("UnsafePath", error.Code);
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(Path.Combine(fixture.DirectoryPath, "outside.cur")));
    }

    [Fact]
    public void FileOverTheReadBudgetIsRejectedWithoutTruncationOrMutation()
    {
        using var fixture = new CursorFixture();
        string path = fixture.Write("cursor.cur", [1, 2, 3, 4, 5]);
        Assert.Throws<ConfigurationException>(() => WorkspaceResources.ReadContained(fixture.DirectoryPath, "cursor.cur", 4));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(path));
    }
}
