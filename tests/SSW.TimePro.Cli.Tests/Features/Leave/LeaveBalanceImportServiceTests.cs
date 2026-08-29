using System.Text;
using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Leave;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Leave;

public class LeaveBalanceImportServiceTests : IDisposable
{
    private const string ValidCsv = "Employee,Leave Type,Units\nJane Doe,Annual Leave,76.00\n";

    private readonly string _tempDir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "tp-balance-import-tests", Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public async Task Import_WithValidCsv_SendsFileContentsVerbatim()
    {
        var api = Substitute.For<ITimeProApiClient>();
        string? sent = null;
        api.ImportLeaveBalancesAsync(Arg.Do<string>(value => sent = value), Arg.Any<CancellationToken>())
            .Returns(new ImportLeaveBalancesResult
            {
                AsAtDate = new DateOnly(2026, 8, 1),
                Created = 1,
                Updated = 2,
                UnmatchedEmployees = ["Nobody Here"],
                Warnings = ["Jane Doe has an unusually large balance"]
            });

        var path = WriteFile("balances.csv", ValidCsv);
        var service = new LeaveBalanceImportService(api);

        var result = await service.ImportAsync(path, TestContext.Current.CancellationToken);

        // The server parses the CSV, so it must arrive exactly as written — no re-encoding.
        sent.Should().Be(ValidCsv);
        result.Created.Should().Be(1);
        result.Updated.Should().Be(2);
        result.UnmatchedEmployees.Should().Equal("Nobody Here");
        result.Warnings.Should().ContainSingle();
    }

    [Fact]
    public async Task Import_WhenFileMissing_DoesNotCallApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var service = new LeaveBalanceImportService(api);

        var act = () => service.ImportAsync(Path.Combine(_tempDir, "nope.csv"), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LeaveBalanceImportValidationException>())
            .WithMessage("File not found:*");
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.ImportLeaveBalancesAsync));
    }

    [Theory]
    [InlineData("~/Downloads/LeaveBalances.csv")]
    [InlineData(@"~\Downloads\LeaveBalances.csv")]
    public void ExpandHomeDirectory_WithTildePrefix_UsesCurrentUserProfile(string path)
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "LeaveBalances.csv");

        var expanded = LeaveBalanceImportService.ExpandHomeDirectory(path);

        expanded.Should().Be(expected);
    }

    [Theory]
    [InlineData("~northwind/LeaveBalances.csv")]
    [InlineData("exports/~/LeaveBalances.csv")]
    public void ExpandHomeDirectory_WhenTildeIsNotTheFirstSegment_LeavesPathUnchanged(string path)
    {
        LeaveBalanceImportService.ExpandHomeDirectory(path).Should().Be(path);
    }

    [Fact]
    public async Task Import_WhenRelativePathResolvesElsewhere_ReportsBothPaths()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var service = new LeaveBalanceImportService(api);

        // A shell that strips backslashes turns "C:\Users\me\x.csv" into a drive-relative
        // "C:Usersmex.csv", which silently resolves against the working directory.
        var act = () => service.ImportAsync("Usersmexero.csv", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LeaveBalanceImportValidationException>())
            .WithMessage("*resolved from 'Usersmexero.csv'*");
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.ImportLeaveBalancesAsync));
    }

    [Fact]
    public async Task Import_WhenPathIsDirectory_DoesNotCallApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var service = new LeaveBalanceImportService(api);

        var act = () => service.ImportAsync(_tempDir, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LeaveBalanceImportValidationException>())
            .WithMessage("*is a directory*");
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.ImportLeaveBalancesAsync));
    }

    [Fact]
    public async Task Import_WhenFileEmpty_DoesNotCallApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var service = new LeaveBalanceImportService(api);
        var path = WriteFile("empty.csv", string.Empty);

        var act = () => service.ImportAsync(path, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LeaveBalanceImportValidationException>())
            .WithMessage("File is empty:*");
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.ImportLeaveBalancesAsync));
    }

    [Fact]
    public async Task Import_WhenFileIsWhitespaceOnly_DoesNotCallApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var service = new LeaveBalanceImportService(api);
        var path = WriteFile("blank.csv", "   \n\n");

        var act = () => service.ImportAsync(path, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LeaveBalanceImportValidationException>())
            .WithMessage("File contains no data:*");
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.ImportLeaveBalancesAsync));
    }

    [Fact]
    public async Task Import_WhenFileIsBinary_DoesNotCallApi()
    {
        var api = Substitute.For<ITimeProApiClient>();
        var service = new LeaveBalanceImportService(api);
        var path = Path.Combine(_tempDir, "balances.xlsx");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x01]);

        var act = () => service.ImportAsync(path, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LeaveBalanceImportValidationException>())
            .WithMessage("*looks like a binary file*");
        api.ShouldNotHaveReceived(nameof(ITimeProApiClient.ImportLeaveBalancesAsync));
    }

    [Fact]
    public async Task Import_WhenApiReturnsForbidden_ExplainsAdminRequirement()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.ImportLeaveBalancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ImportLeaveBalancesResult?>>(_ => throw new ApiException(403, "Forbidden", null));
        var service = new LeaveBalanceImportService(api);
        var path = WriteFile("balances.csv", ValidCsv);

        var act = () => service.ImportAsync(path, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LeaveBalanceImportValidationException>())
            .WithMessage("*leave admin rights*");
    }

    [Fact]
    public async Task Import_WhenApiRejectsCsv_SurfacesServerMessage()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.ImportLeaveBalancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ImportLeaveBalancesResult?>>(_ => throw new ApiException(
                422, "Unprocessable Entity", "\"Missing required column 'Units'.\""));
        var service = new LeaveBalanceImportService(api);
        var path = WriteFile("balances.csv", ValidCsv);

        var act = () => service.ImportAsync(path, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LeaveBalanceImportValidationException>())
            .WithMessage("*Missing required column 'Units'.*");
    }

    [Fact]
    public async Task Import_WhenApiFailsForAnotherReason_LeavesApiExceptionUnwrapped()
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.ImportLeaveBalancesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ImportLeaveBalancesResult?>>(_ => throw new ApiException(500, "Server Error", null));
        var service = new LeaveBalanceImportService(api);
        var path = WriteFile("balances.csv", ValidCsv);

        var act = () => service.ImportAsync(path, TestContext.Current.CancellationToken);

        // 5xx should reach the caller as-is so the response body stays diagnosable.
        await act.Should().ThrowAsync<ApiException>();
    }

    [Theory]
    [InlineData("\"Missing required column 'Units'.\"", "Missing required column 'Units'.")]
    [InlineData("Not json at all", "Not json at all")]
    [InlineData("", "the file was rejected without a reason.")]
    public void DescribeUnprocessable_UnwrapsServerBody(string body, string expected)
    {
        LeaveBalanceImportService.DescribeUnprocessable(body).Should().Be(expected);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
