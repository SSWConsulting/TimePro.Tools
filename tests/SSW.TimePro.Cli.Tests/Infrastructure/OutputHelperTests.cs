using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.Output;
using System.Text.Json;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class OutputHelperTests
{
    [Fact]
    public void SerializeJson_WritesValidJson_ForLongStringsWithControlCharacters()
    {
        var payload = new
        {
            note = string.Join(" ", Enumerable.Repeat("Build the Northwind Checkout API", 8))
                   + "\nTabbed\tvalue"
        };

        var output = OutputHelper.SerializeJson(payload);

        using var document = JsonDocument.Parse(output);
        document.RootElement.GetProperty("note").GetString().Should().Be(payload.note);
        output.Should().Contain("\\n");
        output.Should().Contain("\\t");
    }
}
