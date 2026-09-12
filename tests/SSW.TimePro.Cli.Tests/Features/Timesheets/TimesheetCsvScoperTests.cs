using FluentAssertions;
using SSW.TimePro.Cli.Features.Timesheets;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Timesheets;

public class TimesheetCsvScoperTests
{
    private const string Csv =
        "Date,EmpID,ProjectID,Description,Hours\n" +
        "2026-09-01,BOB,1I776Q,\"Product search, phase 2\",8\n" +
        "2026-09-01,ANN,1I776Q,Checkout API,8\n" +
        "2026-09-02,BOB,1I776Q,\"Order history; \"\"nice to have\"\"\",4\n";

    [Fact]
    public void Scope_KeepsOnlyTheRequestedEmployee_AndItsQuotedFields()
    {
        var result = TimesheetCsvScoper.Scope(Csv, "BOB");

        result.Filtered.Should().BeTrue();
        result.Warning.Should().BeNull();
        result.TotalRows.Should().Be(3);
        result.KeptRows.Should().Be(2);

        var lines = result.Csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3);
        lines[0].Should().StartWith("Date,EmpID");
        lines[1].Should().Be("2026-09-01,BOB,1I776Q,\"Product search, phase 2\",8");
        lines[2].Should().Be("2026-09-02,BOB,1I776Q,\"Order history; \"\"nice to have\"\"\",4");
    }

    [Fact]
    public void Scope_MatchesEmpIdByHeaderName_NotPosition()
    {
        var csv =
            "EmpID,Date,Hours\n" +
            "ANN,2026-09-01,8\n" +
            "BOB,2026-09-01,8\n";

        var result = TimesheetCsvScoper.Scope(csv, "BOB");

        result.KeptRows.Should().Be(1);
        result.Csv.Should().Contain("BOB").And.NotContain("ANN");
    }

    [Fact]
    public void Scope_IgnoresQuotedNewlinesWhenSplittingRows()
    {
        var csv =
            "Date,EmpID,Description\n" +
            "2026-09-01,BOB,\"Product search\nsecond line\"\n" +
            "2026-09-01,ANN,Checkout API\n";

        var result = TimesheetCsvScoper.Scope(csv, "BOB");

        result.TotalRows.Should().Be(2);
        result.KeptRows.Should().Be(1);
        result.Csv.Should().Contain("second line").And.NotContain("Checkout API");
    }

    [Fact]
    public void Scope_WithAnotherEmployee_KeepsThatEmployeesRows()
    {
        var result = TimesheetCsvScoper.Scope(Csv, "ANN");

        result.KeptRows.Should().Be(1);
        result.Csv.Should().Contain("Checkout API").And.NotContain("Product search");
    }

    [Fact]
    public void Unscoped_ReturnsTheRawExport()
    {
        var result = TimesheetCsvScoper.Unscoped(Csv);

        result.Filtered.Should().BeFalse();
        result.Warning.Should().BeNull();
        result.Csv.Should().Be(Csv);
        result.TotalRows.Should().Be(3);
        result.KeptRows.Should().Be(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Scope_WithBlankEmpId_Throws_RatherThanKeepingEveryEmployee(string empId)
    {
        var act = () => TimesheetCsvScoper.Scope(Csv, empId);

        act.Should().Throw<ArgumentException>().WithParameterName("empId");
    }

    [Fact]
    public void Scope_WithoutAnEmpIdColumn_ReturnsTheRawExportAndWarns()
    {
        var csv =
            "Date,Employee,Hours\n" +
            "2026-09-01,Bob Northwind,8\n";

        var result = TimesheetCsvScoper.Scope(csv, "BOB");

        result.Filtered.Should().BeFalse();
        result.Csv.Should().Be(csv);
        result.Warning.Should().Contain("EmpID");
    }

    [Fact]
    public void Scope_PreservesCrLfLineEndings()
    {
        var csv = "Date,EmpID,Hours\r\n2026-09-01,BOB,8\r\n2026-09-01,ANN,8\r\n";

        var result = TimesheetCsvScoper.Scope(csv, "BOB");

        result.Csv.Should().Be("Date,EmpID,Hours\r\n2026-09-01,BOB,8\r\n");
    }
}
