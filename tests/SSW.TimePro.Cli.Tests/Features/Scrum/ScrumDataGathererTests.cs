using FluentAssertions;
using NSubstitute;
using SSW.TimePro.Cli.Features.Scrum;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Shared.Models;
using Xunit;

namespace SSW.TimePro.Cli.Tests.Features.Scrum;

/// <summary>
/// Tests for <see cref="ScrumDataGatherer"/> — the TimePro + GitHub + repo-mapping
/// glue behind <c>tp scrum</c>. The API and gh CLI are faked so we can drive the
/// project-selection, --project override, smart-selection and internal/external
/// logic deterministically (no network, no <c>gh</c> process).
/// </summary>
public class ScrumDataGathererTests
{
    private const string Emp = "JEK";
    // A weekday, so the "previous work day" walk-back is deterministic.
    private static readonly DateOnly Today = new(2026, 6, 18);

    // Northwind-only placeholders (see CLAUDE.md).
    private const string ClientId = "NWIND";
    private const string ProjectId = "1I776Q";
    private const string IssuesRepo = "Northwind/traders-app";

    /// <summary>In-memory gh double — records calls and returns canned PRs/issues per repo.</summary>
    private sealed class FakeGh : IGhCli
    {
        public Dictionary<string, List<GhCli.PullRequest>> Prs { get; } = new();
        public Dictionary<string, List<GhCli.Issue>> Issues { get; } = new();
        public List<string> PrCalls { get; } = [];

        public List<GhCli.PullRequest> ListMyPullRequests(string ownerRepo, int limit = 20)
        {
            PrCalls.Add(ownerRepo);
            return Prs.TryGetValue(ownerRepo, out var v) ? v : [];
        }

        public List<GhCli.Issue> ListMyAssignedIssues(string ownerRepo, int limit = 10) =>
            Issues.TryGetValue(ownerRepo, out var v) ? v : [];
    }

    private static GhCli.PullRequest OpenPr(int number, string title) =>
        new(number, title, "OPEN", MergedAt: null,
            UpdatedAt: new DateTimeOffset(2026, 6, 18, 9, 0, 0, TimeSpan.Zero),
            Url: $"https://github.com/{IssuesRepo}/pull/{number}");

    private static TimesheetItem Logged(string clientId, string projectId, string? notes = null) =>
        new() { ClientId = clientId, ProjectId = projectId, Client = clientId, Project = projectId, Notes = notes };

    private static (ITimeProApiClient api, IConfigService cfg, FakeGh gh) Harness(
        List<TimesheetItem>? today = null,
        List<AppointmentItem>? appointments = null,
        List<RepoMappingEntry>? mappings = null)
    {
        var api = Substitute.For<ITimeProApiClient>();
        api.GetTimesheetsAsync(default!, default, default)
            .ReturnsForAnyArgs(ci => ci.Arg<DateOnly>() == Today ? (today ?? []) : []);
        api.GetAppointmentsAsync(default!, default, default, default)
            .ReturnsForAnyArgs(_ => appointments ?? []);

        var cfg = Substitute.For<IConfigService>();
        cfg.LoadGlobalConfig().Returns(new GlobalConfig());
        cfg.LoadRepoMappings().Returns(mappings ?? []);

        return (api, cfg, new FakeGh());
    }

    private static RepoMappingEntry Mapping(string clientId, string projectId, string? issuesRepo) =>
        new() { ClientId = clientId, ProjectId = projectId, ProjectName = projectId, IssuesRepo = issuesRepo };

    [Fact]
    public async Task Override_IncludesUnloggedProject_AndGathersItsGitHubActivity()
    {
        // Nothing logged today, but the user passes --project for a mapped project.
        var (api, cfg, gh) = Harness(mappings: [Mapping(ClientId, ProjectId, IssuesRepo)]);
        gh.Prs[IssuesRepo] = [OpenPr(42, "Product search")];

        var model = await new ScrumDataGatherer(api, cfg, gh)
            .BuildAsync(Emp, Today, [ProjectId], forceInternal: null, CancellationToken.None, smartSelection: true);

        gh.PrCalls.Should().Contain(IssuesRepo, "the override must resolve the project's issuesRepo from the mapping");
        model.Today.Should().ContainSingle(i => i.Reference == "#42");
        model.PrimaryClientId.Should().Be(ClientId);
    }

    [Fact]
    public async Task Override_WithUnmappedId_IsIgnoredGracefully()
    {
        var (api, cfg, gh) = Harness(mappings: []); // no mapping for the requested id

        var model = await new ScrumDataGatherer(api, cfg, gh)
            .BuildAsync(Emp, Today, ["ZZZNONE"], forceInternal: null, CancellationToken.None, smartSelection: true);

        gh.PrCalls.Should().BeEmpty("an unmapped override has no issuesRepo to query");
        model.Today.Should().BeEmpty();
    }

    [Fact]
    public async Task NoOverride_AutoDetectsProjectsFromTodaysTimesheets()
    {
        var (api, cfg, gh) = Harness(
            today: [Logged(ClientId, ProjectId)],
            mappings: [Mapping(ClientId, ProjectId, IssuesRepo)]);
        gh.Prs[IssuesRepo] = [OpenPr(108, "Checkout API")];

        var model = await new ScrumDataGatherer(api, cfg, gh)
            .BuildAsync(Emp, Today, projectOverrides: null, forceInternal: null, CancellationToken.None, smartSelection: true);

        model.Today.Should().ContainSingle(i => i.Reference == "#108");
        model.PrimaryClientId.Should().Be(ClientId);
    }

    [Fact]
    public async Task Default_OpenPullRequest_GoesToToday()
    {
        var (api, cfg, gh) = Harness(
            today: [Logged(ClientId, ProjectId)],
            mappings: [Mapping(ClientId, ProjectId, IssuesRepo)]);
        gh.Prs[IssuesRepo] = [OpenPr(7, "Order history")];

        var model = await new ScrumDataGatherer(api, cfg, gh)
            .BuildAsync(Emp, Today, null, forceInternal: null, CancellationToken.None, smartSelection: false);

        model.Today.Should().ContainSingle(i => i.Reference == "#7");
    }

    [Fact]
    public async Task Internal_WhenNoClientBookingOrTimesheet()
    {
        var (api, cfg, gh) = Harness(mappings: [Mapping(ClientId, ProjectId, IssuesRepo)]);

        var model = await new ScrumDataGatherer(api, cfg, gh)
            .BuildAsync(Emp, Today, [ProjectId], forceInternal: null, CancellationToken.None, smartSelection: true);

        model.IsInternal.Should().BeTrue();
        model.Internal.Should().NotBeNull();
    }

    [Fact]
    public async Task External_WhenAClientBookingExists()
    {
        var (api, cfg, gh) = Harness(
            appointments: [new AppointmentItem { ClientId = ClientId }], // non-SSW client => external
            mappings: [Mapping(ClientId, ProjectId, IssuesRepo)]);

        var model = await new ScrumDataGatherer(api, cfg, gh)
            .BuildAsync(Emp, Today, [ProjectId], forceInternal: null, CancellationToken.None, smartSelection: true);

        model.IsInternal.Should().BeFalse();
    }

    [Fact]
    public async Task ForceInternal_OverridesClassification()
    {
        var (api, cfg, gh) = Harness(
            appointments: [new AppointmentItem { ClientId = ClientId }],
            mappings: [Mapping(ClientId, ProjectId, IssuesRepo)]);

        var model = await new ScrumDataGatherer(api, cfg, gh)
            .BuildAsync(Emp, Today, [ProjectId], forceInternal: true, CancellationToken.None, smartSelection: true);

        model.IsInternal.Should().BeTrue();
    }
}
