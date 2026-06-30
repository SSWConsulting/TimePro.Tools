using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Config;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class ApiUrlNormalizerTests
{
    [Theory]
    [InlineData("https://northwind.local-sswtimepro.com", "https://localhost:7107")]
    [InlineData("https://northwind.local-sswtimepro.com/", "https://localhost:7107")]
    [InlineData("https://northwind.local-sswtimepro.com/b/admin/api-key", "https://localhost:7107")]
    [InlineData("https://northwind.local-sswtimepro.com:443", "https://localhost:7107")]
    [InlineData("https://northwind.local-sswtimepro.com:7107", "https://localhost:7107")]
    public void Normalize_LocalTimeProHost_UsesLocalhostWithDevPort(string input, string expected)
    {
        var normalized = ApiUrlNormalizer.Normalize(input, out var note);

        normalized.Should().Be(expected);
        note.Should().Contain("local TimePro");
    }

    [Fact]
    public void Normalize_NonLocalHost_ReturnsOriginalUrl()
    {
        const string input = "https://api.example.com";

        var normalized = ApiUrlNormalizer.Normalize(input, out var note);

        normalized.Should().Be(input);
        note.Should().BeNull();
    }
}
