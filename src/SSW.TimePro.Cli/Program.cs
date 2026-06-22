using Microsoft.Extensions.DependencyInjection;
using SSW.TimePro.Cli.Features.Auth;
using SSW.TimePro.Cli.Features.Bookings;
using SSW.TimePro.Cli.Features.Tenants;
using SSW.TimePro.Cli.Features.Timesheets;
using SSW.TimePro.Cli.Features.Users;
using SSW.TimePro.Cli.Infrastructure;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.DependencyInjection;
using Spectre.Console.Cli;

using ClientSearch = SSW.TimePro.Cli.Features.Clients.SearchCommand;
using ClientOutstanding = SSW.TimePro.Cli.Features.Clients.OutstandingCommand;
using ProjectList = SSW.TimePro.Cli.Features.Projects.ListCommand;
using ProjectRecent = SSW.TimePro.Cli.Features.Projects.RecentCommand;
using RateGet = SSW.TimePro.Cli.Features.Rates.GetCommand;
using RateList = SSW.TimePro.Cli.Features.Rates.ListCommand;
using LeaveList = SSW.TimePro.Cli.Features.Leave.ListCommand;
using LeaveCreate = SSW.TimePro.Cli.Features.Leave.CreateCommand;
using LeaveCancel = SSW.TimePro.Cli.Features.Leave.CancelCommand;
using LeaveBalance = SSW.TimePro.Cli.Features.Leave.BalanceCommand;
using InvList = SSW.TimePro.Cli.Features.Invoices.ListCommand;
using InvGet = SSW.TimePro.Cli.Features.Invoices.GetCommand;
using InvLines = SSW.TimePro.Cli.Features.Invoices.LinesCommand;
using InvTimesheets = SSW.TimePro.Cli.Features.Invoices.TimesheetsCommand;
using InvReceipts = SSW.TimePro.Cli.Features.Invoices.ReceiptsCommand;
using ReceiptList = SSW.TimePro.Cli.Features.Receipts.ListCommand;
using ReceiptGet = SSW.TimePro.Cli.Features.Receipts.GetCommand;
using ReceiptOutstanding = SSW.TimePro.Cli.Features.Receipts.OutstandingCommand;
using CreditNoteList = SSW.TimePro.Cli.Features.CreditNotes.ListCommand;
using ProductList = SSW.TimePro.Cli.Features.Products.ListCommand;
using ProductGet = SSW.TimePro.Cli.Features.Products.GetCommand;
using ProductDiscounts = SSW.TimePro.Cli.Features.Products.DiscountsCommand;
using RecurringList = SSW.TimePro.Cli.Features.Recurring.ListCommand;
using RecurringGet = SSW.TimePro.Cli.Features.Recurring.GetCommand;
using PrepaidStatus = SSW.TimePro.Cli.Features.Prepaid.StatusCommand;
using PrepaidSummary = SSW.TimePro.Cli.Features.Prepaid.SummaryCommand;
using UnbilledList = SSW.TimePro.Cli.Features.Unbilled.ListCommand;
using LocationInfo = SSW.TimePro.Cli.Features.Location.InfoCommand;
using LocationSet = SSW.TimePro.Cli.Features.Location.SetCommand;
using MapSet = SSW.TimePro.Cli.Features.RepoMap.SetCommand;
using MapList = SSW.TimePro.Cli.Features.RepoMap.ListCommand;
using MapRemove = SSW.TimePro.Cli.Features.RepoMap.RemoveCommand;
using MapDetect = SSW.TimePro.Cli.Features.RepoMap.DetectCommand;
using SkillsCreate = SSW.TimePro.Cli.Features.Skills.CreateCommand;
using BlogList = SSW.TimePro.Cli.Features.Blogs.ListCommand;
using IterationList = SSW.TimePro.Cli.Features.Iterations.ListCommand;
using SummaryCmd = SSW.TimePro.Cli.Features.Summary.SummaryCommand;
using ReportCmd = SSW.TimePro.Cli.Features.Report.ReportCommand;
using QueryCmd = SSW.TimePro.Cli.Features.Query.QueryCommand;
using McpHost = SSW.TimePro.Cli.Features.Mcp.McpHostCommand;
using ScrumCmd = SSW.TimePro.Cli.Features.Scrum.ScrumCommand;

// Configure DI
var services = new ServiceCollection();
services.AddSingleton<IConfigService, ConfigService>();
services.AddSingleton<ITenantProvider, DefaultTenantProvider>();
services.AddHttpClient<ITimeProApiClient, TimeProApiClient>();

var registrar = new TypeRegistrar(services);

// Build command tree
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("tp");
    config.SetApplicationVersion($"{BuildInfo.Version}+{BuildInfo.Commit}");

    // Auth
    config.AddCommand<LoginCommand>("login")
        .WithDescription("Authenticate with a TimePro tenant");
    config.AddCommand<LogoutCommand>("logout")
        .WithDescription("Remove stored credentials");

    // Tenant management
    config.AddBranch("tenant", tenant =>
    {
        tenant.SetDescription("Manage tenants");
        tenant.AddCommand<TenantSetCommand>("set")
            .WithDescription("Switch the active tenant");
        tenant.AddCommand<TenantInfoCommand>("info")
            .WithDescription("Show active tenant details");
        tenant.AddCommand<TenantListCommand>("list")
            .WithDescription("List all stored tenants");
    });

    // Helper to register all timesheet subcommands on a branch
    void RegisterTimesheetCommands(IConfigurator<CommandSettings> branch)
    {
        branch.AddCommand<GetCommand>("get")
            .WithDescription("View timesheets for a day or week");
        branch.AddCommand<CreateCommand>("create")
            .WithDescription("Create a new timesheet entry");
        branch.AddCommand<UpdateCommand>("update")
            .WithDescription("Update an existing timesheet");
        branch.AddCommand<DeleteCommand>("delete")
            .WithDescription("Delete a timesheet entry");
        branch.AddCommand<SuggestCommand>("suggest")
            .WithDescription("View suggested timesheets");
        branch.AddCommand<AcceptCommand>("accept")
            .WithDescription("Accept a suggested timesheet");
        branch.AddCommand<ExportCommand>("export")
            .WithDescription("Export timesheets to CSV");
        branch.AddCommand<CheckCommand>("check")
            .WithDescription("Validate timesheets for a week");
        branch.AddCommand<CopyCommand>("copy")
            .WithDescription("Copy timesheets from one day to another");
    }

    // Timesheets (with alias)
    config.AddBranch("timesheet", ts =>
    {
        ts.SetDescription("Manage timesheets");
        RegisterTimesheetCommands(ts);
    });

    config.AddBranch("ts", ts =>
    {
        ts.SetDescription("Manage timesheets (alias)");
        RegisterTimesheetCommands(ts);
    });

    // Bookings (with alias)
    config.AddBranch("booking", bk =>
    {
        bk.SetDescription("CRM bookings/appointments");
        bk.AddCommand<ListCommand>("list")
            .WithDescription("List CRM bookings");
    });

    config.AddBranch("bk", bk =>
    {
        bk.SetDescription("CRM bookings (alias)");
        bk.AddCommand<ListCommand>("list")
            .WithDescription("List CRM bookings");
    });

    // Leave (with alias)
    void RegisterLeaveCommands(IConfigurator<CommandSettings> branch)
    {
        branch.AddCommand<LeaveList>("list")
            .WithDescription("List leave entries");
        branch.AddCommand<LeaveCreate>("create")
            .WithDescription("Create a leave request");
        branch.AddCommand<LeaveCancel>("cancel")
            .WithDescription("Cancel a leave request");
        branch.AddCommand<LeaveBalance>("balance")
            .WithDescription("Show leave stats (days since last leave, leave taken in last 12 months)");
    }

    config.AddBranch("leave", lv =>
    {
        lv.SetDescription("Manage leave/EasyLeave");
        RegisterLeaveCommands(lv);
    });

    config.AddBranch("lv", lv =>
    {
        lv.SetDescription("Manage leave (alias)");
        RegisterLeaveCommands(lv);
    });

    // Client (with alias)
    void RegisterClientCommands(IConfigurator<CommandSettings> branch)
    {
        branch.AddCommand<ClientSearch>("search")
            .WithDescription("Search for clients by name");
        branch.AddCommand<ClientOutstanding>("outstanding")
            .WithDescription("List clients with unbilled time");
    }

    config.AddBranch("client", cl =>
    {
        cl.SetDescription("Client operations");
        RegisterClientCommands(cl);
    });

    config.AddBranch("cl", cl =>
    {
        cl.SetDescription("Client operations (alias)");
        RegisterClientCommands(cl);
    });

    // Project (with alias)
    config.AddBranch("project", pj =>
    {
        pj.SetDescription("Project operations");
        pj.AddCommand<ProjectList>("list")
            .WithDescription("List projects for a client");
        pj.AddCommand<ProjectRecent>("recent")
            .WithDescription("Surface recent/likely projects for timesheet filling");
    });

    config.AddBranch("proj", pj =>
    {
        pj.SetDescription("Project operations (alias)");
        pj.AddCommand<ProjectList>("list")
            .WithDescription("List projects for a client");
        pj.AddCommand<ProjectRecent>("recent")
            .WithDescription("Surface recent/likely projects for timesheet filling");
    });

    // Rate
    config.AddBranch("rate", rate =>
    {
        rate.SetDescription("Rate information");
        rate.AddCommand<RateGet>("get")
            .WithDescription("Get client rate for current employee");
        rate.AddCommand<RateList>("list")
            .WithDescription("List all configured rates for a client (paged)");
    });

    // Iteration (with alias)
    config.AddBranch("iteration", it =>
    {
        it.SetDescription("Sprint/iteration operations");
        it.AddCommand<IterationList>("list")
            .WithDescription("List iterations for a project");
    });

    config.AddBranch("iter", it =>
    {
        it.SetDescription("Sprint/iteration (alias)");
        it.AddCommand<IterationList>("list")
            .WithDescription("List iterations for a project");
    });

    // Location (with alias)
    config.AddBranch("location", loc =>
    {
        loc.SetDescription("Location and WFH settings");
        loc.AddCommand<LocationInfo>("info")
            .WithDescription("Show location defaults");
        loc.AddCommand<LocationSet>("set")
            .WithDescription("Set WFH day defaults");
    });

    config.AddBranch("loc", loc =>
    {
        loc.SetDescription("Location settings (alias)");
        loc.AddCommand<LocationInfo>("info")
            .WithDescription("Show location defaults");
        loc.AddCommand<LocationSet>("set")
            .WithDescription("Set WFH day defaults");
    });

    // Repo mapping
    config.AddBranch("map", map =>
    {
        map.SetDescription("Repository-to-project mappings");
        map.AddCommand<MapSet>("set")
            .WithDescription("Map a repo path to a client/project");
        map.AddCommand<MapList>("list")
            .WithDescription("List repo mappings");
        map.AddCommand<MapRemove>("remove")
            .WithDescription("Remove a repo mapping");
        map.AddCommand<MapDetect>("detect")
            .WithDescription("Detect mapping for current directory");
    });

    // Skills
    config.AddBranch("skills", skills =>
    {
        skills.SetDescription("Agent skill file management");
        skills.AddCommand<SkillsCreate>("create")
            .WithDescription("Generate agent skill files");
    });

    // ───── Accounting (read-only) ─────

    // Invoice (with alias `inv`)
    void RegisterInvoiceCommands(IConfigurator<CommandSettings> branch)
    {
        branch.AddCommand<InvList>("list")
            .WithDescription("List invoices (paged, filtered)");
        branch.AddCommand<InvGet>("get")
            .WithDescription("Get invoice header details");
        branch.AddCommand<InvLines>("lines")
            .WithDescription("List line items (products) on an invoice");
        branch.AddCommand<InvTimesheets>("timesheets")
            .WithDescription("List timesheets allocated to (or written off against) an invoice");
        branch.AddCommand<InvReceipts>("receipts")
            .WithDescription("List receipts against an invoice");
    }

    config.AddBranch("invoice", inv =>
    {
        inv.SetDescription("Invoices (read-only)");
        RegisterInvoiceCommands(inv);
    });

    config.AddBranch("inv", inv =>
    {
        inv.SetDescription("Invoices (alias)");
        RegisterInvoiceCommands(inv);
    });

    // Receipt (with alias `rcpt`)
    void RegisterReceiptCommands(IConfigurator<CommandSettings> branch)
    {
        branch.AddCommand<ReceiptList>("list")
            .WithDescription("List paid receipts (paged)");
        branch.AddCommand<ReceiptGet>("get")
            .WithDescription("Get a receipt with its invoice allocations");
        branch.AddCommand<ReceiptOutstanding>("outstanding")
            .WithDescription("Aged-debtor view for a client");
    }

    config.AddBranch("receipt", rcpt =>
    {
        rcpt.SetDescription("Receipts (read-only)");
        RegisterReceiptCommands(rcpt);
    });

    config.AddBranch("rcpt", rcpt =>
    {
        rcpt.SetDescription("Receipts (alias)");
        RegisterReceiptCommands(rcpt);
    });

    // Credit notes (alias `cn`)
    void RegisterCreditNoteCommands(IConfigurator<CommandSettings> branch)
    {
        branch.AddCommand<CreditNoteList>("list")
            .WithDescription("List credit notes for a client");
    }

    config.AddBranch("creditnote", cn =>
    {
        cn.SetDescription("Credit notes (read-only)");
        RegisterCreditNoteCommands(cn);
    });

    config.AddBranch("cn", cn =>
    {
        cn.SetDescription("Credit notes (alias)");
        RegisterCreditNoteCommands(cn);
    });

    // Products (alias `prod`)
    void RegisterProductCommands(IConfigurator<CommandSettings> branch)
    {
        branch.AddCommand<ProductList>("list")
            .WithDescription("List products (or all prepaid SKUs with --prepaid)");
        branch.AddCommand<ProductGet>("get")
            .WithDescription("Get a single product by ID");
        branch.AddCommand<ProductDiscounts>("discounts")
            .WithDescription("Show product discounts for a client");
    }

    config.AddBranch("product", prod =>
    {
        prod.SetDescription("Sale products / SKUs (read-only)");
        RegisterProductCommands(prod);
    });

    config.AddBranch("prod", prod =>
    {
        prod.SetDescription("Products (alias)");
        RegisterProductCommands(prod);
    });

    // Recurring invoices
    config.AddBranch("recurring", rec =>
    {
        rec.SetDescription("Recurring invoice templates (read-only)");
        rec.AddCommand<RecurringList>("list")
            .WithDescription("List recurring invoice templates");
        rec.AddCommand<RecurringGet>("get")
            .WithDescription("Get a recurring invoice template by ID");
    });

    // Prepaid drawdown
    config.AddBranch("prepaid", pp =>
    {
        pp.SetDescription("Prepaid drawdown reports");
        pp.AddCommand<PrepaidSummary>("summary")
            .WithDescription("Show structured prepaid drawdown totals for an invoice");
        pp.AddCommand<PrepaidStatus>("status")
            .WithDescription("Download the prepaid drawdown status PDF for an invoice");
    });

    // Unbilled time
    config.AddBranch("unbilled", ub =>
    {
        ub.SetDescription("Unbilled (unallocated) time");
        ub.AddCommand<UnbilledList>("list")
            .WithDescription("List unbilled timesheets for a client");
    });

    // User
    config.AddBranch("user", user =>
    {
        user.SetDescription("User information");
        user.AddCommand<MeCommand>("me")
            .WithDescription("Show current user info");
    });

    // Blog
    config.AddBranch("blog", blog =>
    {
        blog.SetDescription("SSW employee blog posts");
        blog.AddCommand<BlogList>("list")
            .WithDescription("List latest blog posts");
    });

    // Summary & Report (top-level)
    config.AddCommand<SummaryCmd>("summary")
        .WithDescription("Project hours breakdown for a period");
    config.AddCommand<ReportCmd>("report")
        .WithDescription("Monthly summary with billable % and WFH breakdown");
    config.AddCommand<QueryCmd>("query")
        .WithDescription("Query timesheets across employees, clients, projects");

    // Daily scrum
    config.AddCommand<ScrumCmd>("scrum")
        .WithDescription("Generate a daily scrum email from timesheets + GitHub activity");

    // MCP
    config.AddCommand<McpHost>("mcp")
        .WithDescription("Start MCP server (stdio transport)");
});

return await app.RunAsync(args);
