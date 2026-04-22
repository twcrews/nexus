using Nexus.Models;

namespace Nexus.Tests;

public class UserReferenceTests
{
    [Theory]
    [InlineData("Alice", "Alice")]
    [InlineData("Bob Smith", "Bob Smith")]
    [InlineData("Smith, Bob", "Bob Smith")]
    [InlineData("O'Brien, Mary", "Mary O'Brien")]
    [InlineData("Smith, Bob Jr.", "Bob Jr. Smith")]
    [InlineData("  Doe  ,  Jane  ", "Jane Doe")]
    public void DisplayName_ReturnsExpectedName(string name, string expected)
    {
        var user = new UserReference(name, null);
        Assert.Equal(expected, user.DisplayName);
    }

    [Fact]
    public void DisplayName_CommaAtStart_ReturnsNameUnchanged()
    {
        // comma at position 0: comma <= 0 → return Name as-is
        var user = new UserReference(",Smith", null);
        Assert.Equal(",Smith", user.DisplayName);
    }

    [Fact]
    public void DisplayName_TrailingCommaNoFirst_ReturnsLastName()
    {
        // "Last," — first part is empty after trim → return last
        var user = new UserReference("Smith,", null);
        Assert.Equal("Smith", user.DisplayName);
    }

    [Fact]
    public void DisplayName_NoComma_ReturnsNameUnchanged()
    {
        var user = new UserReference("SingleName", null);
        Assert.Equal("SingleName", user.DisplayName);
    }

    [Fact]
    public void DisplayName_AvatarUrlIsPreserved()
    {
        var user = new UserReference("Doe, John", "https://example.com/avatar.png");
        Assert.Equal("John Doe", user.DisplayName);
        Assert.Equal("https://example.com/avatar.png", user.AvatarUrl);
    }

    [Fact]
    public void UserReference_Equality_BasedOnNameAndAvatar()
    {
        var a = new UserReference("Alice", null);
        var b = new UserReference("Alice", null);
        Assert.Equal(a, b);
    }
}
