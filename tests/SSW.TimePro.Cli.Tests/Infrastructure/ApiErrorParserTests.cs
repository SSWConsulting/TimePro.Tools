using FluentAssertions;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Infrastructure;

public class ApiErrorParserTests
{
    [Fact]
    public void ExtractDetail_ProblemDetailsWithValidationErrors_FlattensFieldMessages()
    {
        const string body = """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
              "title": "One or more validation errors occurred.",
              "status": 400,
              "traceId": "00-abc-def-00",
              "errors": {
                "IterationId": ["The Iteration field is required."],
                "Hours": ["Must be greater than 0.", "Must be less than 24."]
              }
            }
            """;

        ApiErrorParser.ExtractDetail(body).Should().Be(
            "IterationId: The Iteration field is required.; Hours: Must be greater than 0.; Hours: Must be less than 24.");
    }

    [Fact]
    public void ExtractDetail_ProblemDetailsWithoutErrors_UsesDetail()
    {
        const string body = """
            {"title":"Bad Request","status":400,"detail":"Timesheet entry is locked."}
            """;

        ApiErrorParser.ExtractDetail(body).Should().Be("Timesheet entry is locked.");
    }

    [Fact]
    public void ExtractDetail_ProblemDetailsWithOnlyTitle_UsesTitle()
    {
        ApiErrorParser.ExtractDetail("""{"title":"Suggestion cannot be updated.","status":400}""")
            .Should().Be("Suggestion cannot be updated.");
    }

    [Fact]
    public void ExtractDetail_BareJsonString_ReturnsUnquotedValue()
    {
        ApiErrorParser.ExtractDetail("\"Employee BOB is not allowed to edit this entry.\"")
            .Should().Be("Employee BOB is not allowed to edit this entry.");
    }

    [Fact]
    public void ExtractDetail_MessageProperty_ReturnsMessage()
    {
        ApiErrorParser.ExtractDetail("""{"message":"Project 1I776Q requires an iteration."}""")
            .Should().Be("Project 1I776Q requires an iteration.");
    }

    [Fact]
    public void ExtractDetail_FlatValidationObject_ReturnsFieldMessages()
    {
        ApiErrorParser.ExtractDetail("""{"status":400,"CategoryID":"Please specify a category."}""")
            .Should().Be("CategoryID: Please specify a category.");
    }

    [Fact]
    public void ExtractDetail_NonJsonBody_ReturnsSingleLineRawBody()
    {
        ApiErrorParser.ExtractDetail("<html>\n  <body>Server Error</body>\n</html>")
            .Should().Be("<html> <body>Server Error</body> </html>");
    }

    [Fact]
    public void ExtractDetail_LongBody_TruncatesTo500Characters()
    {
        var detail = ApiErrorParser.ExtractDetail(new string('x', 900));

        detail.Should().HaveLength(501);
        detail.Should().EndWith("…");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractDetail_EmptyBody_ReturnsNull(string? body)
    {
        ApiErrorParser.ExtractDetail(body).Should().BeNull();
    }
}
