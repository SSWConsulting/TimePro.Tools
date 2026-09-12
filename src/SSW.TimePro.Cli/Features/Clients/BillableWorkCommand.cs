using System.ComponentModel;
using System.Globalization;
using System.Text;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using SSW.TimePro.Cli.Shared.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Clients;

[Description("Export clients over a billable work threshold")]
public class BillableWorkCommand : AsyncCommand<BillableWorkCommand.Settings>
{
    private const int PageSize = 500;

    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandOption("--from <DATE>")]
        [Description("Start date (yyyy-MM-dd). Defaults to one year before --to")]
        public string? From { get; set; }

        [CommandOption("--to <DATE>")]
        [Description("End date (yyyy-MM-dd). Defaults to today")]
        public string? To { get; set; }

        [CommandOption("--threshold <AMOUNT>")]
        [Description("Minimum billable work ex-GST. Default: 50000")]
        [DefaultValue(typeof(decimal), "50000")]
        public decimal Threshold { get; set; } = 50000m;

        [CommandOption("--output <FILE>")]
        [Description("CSV path. Defaults to ./Reports/timepro-clients-{threshold}-billable-work-{from}-to-{to}.csv")]
        public string? Output { get; set; }

        [CommandOption("--json")]
        [Description("Write report JSON to stdout instead of creating a CSV")]
        public bool Json { get; set; }
    }

    public BillableWorkCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (_config.LoadActiveTenantConfig() is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        var (start, end) = ResolveDateRange(settings);
        if (start > end)
        {
            OutputHelper.WriteError("--from must be on or before --to.");
            return 1;
        }

        var output = ResolveOutputPath(settings.Output, settings.Threshold, start, end);

        try
        {
            WriteProgress(settings, "Fetching timesheets in window...");
            var timesheets = await _api.QueryTimesheetsAsync(new TimesheetSummaryFilter
            {
                StartDate = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            }, cancellationToken);

            var workByClient = BuildWorkByClient(timesheets, start, end);
            var qualifiedClientIds = workByClient
                .Where(pair => pair.Value.BillableTimesheetValueExGst >= settings.Threshold)
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            WriteProgress(settings, $"Qualified clients: {qualifiedClientIds.Count}");
            WriteProgress(settings, "Fetching invoice history...");

            var invoicesByClient = await FetchInvoicesByClientAsync(end, cancellationToken);
            var invoiceHeaders = await FetchInvoiceHeadersAsync(invoicesByClient, qualifiedClientIds, start, end, settings.Json, cancellationToken);
            var rows = BuildRows(workByClient, qualifiedClientIds, invoicesByClient, invoiceHeaders, start, end);

            var totalValue = rows.Sum(row => row.BillableTimesheetValueExGst);
            var totalHours = rows.Sum(row => row.BillableHoursBAndBpp);
            var totalInvoicedExGst = rows.Sum(row => row.InvoicedExGstInWindow);
            var totalInvoicedIncGst = rows.Sum(row => row.InvoicedIncGstInWindow);

            if (settings.Json)
            {
                OutputHelper.WriteJson(new
                {
                    startDate = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    endDate = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    thresholdExGst = settings.Threshold,
                    clientCount = rows.Count,
                    totals = new
                    {
                        billableTimesheetValueExGst = totalValue,
                        billableHoursBAndBpp = totalHours,
                        invoiceCountInWindow = rows.Sum(row => row.InvoiceCountInWindow),
                        invoicedExGstInWindow = totalInvoicedExGst,
                        invoicedIncGstInWindow = totalInvoicedIncGst
                    },
                    rows
                });
                return 0;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? Environment.CurrentDirectory);
            await WriteCsvAsync(output, rows, cancellationToken);

            AnsiConsole.MarkupLine($"[green]CSV:[/] {Markup.Escape(output)}");
            AnsiConsole.MarkupLine($"[green]Clients:[/] {rows.Count}");
            AnsiConsole.MarkupLine($"[green]Billable work ex-GST:[/] ${totalValue:N2}");
            AnsiConsole.MarkupLine($"[green]Billable hours:[/] {totalHours:N1}h");
            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteApiError(ex, settings.Json);
            return 1;
        }
    }

    private static void WriteProgress(Settings settings, string message)
    {
        if (!settings.Json)
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
    }

    private static (DateOnly Start, DateOnly End) ResolveDateRange(Settings settings)
    {
        var end = settings.To is null
            ? DateOnly.FromDateTime(DateTime.Today)
            : DateOnly.ParseExact(settings.To, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var start = settings.From is null
            ? end.AddYears(-1)
            : DateOnly.ParseExact(settings.From, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return (start, end);
    }

    private static string ResolveOutputPath(string? output, decimal threshold, DateOnly start, DateOnly end)
    {
        if (!string.IsNullOrWhiteSpace(output))
            return Path.GetFullPath(output);

        var thresholdText = threshold.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_');
        var fileName = $"timepro-clients-{thresholdText}-billable-work-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}.csv";
        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Reports", fileName));
    }

    private static Dictionary<string, ClientWork> BuildWorkByClient(List<TimesheetSummaryEntry> timesheets, DateOnly start, DateOnly end)
    {
        var result = new Dictionary<string, ClientWork>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in timesheets)
        {
            if (entry.BillableId is not ("B" or "BPP"))
                continue;

            if (!string.IsNullOrWhiteSpace(entry.TimesheetDate) &&
                DateTime.TryParse(entry.TimesheetDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsedDate))
            {
                var date = DateOnly.FromDateTime(parsedDate);
                if (date < start || date > end)
                    continue;
            }

            if (string.IsNullOrWhiteSpace(entry.ClientId))
                continue;

            if (!result.TryGetValue(entry.ClientId, out var work))
            {
                work = new ClientWork(entry.ClientId, entry.ClientName ?? entry.ClientId);
                result.Add(entry.ClientId, work);
            }

            work.ClientName = entry.ClientName ?? work.ClientName;
            work.BillableHoursBAndBpp += entry.TotalHours;
            work.BillableTimesheetValueExGst += entry.TotalHours * entry.SellPrice;
            work.BillableTimesheetCount++;

            if (entry.BillableId == "B")
                work.BillableHoursB += entry.TotalHours;
            else
                work.BillableHoursBpp += entry.TotalHours;

            if (!string.IsNullOrWhiteSpace(entry.EmpId))
                work.EmployeeIds.Add(entry.EmpId);
            if (!string.IsNullOrWhiteSpace(entry.ProjectId))
                work.ProjectIds.Add(entry.ProjectId);
        }

        return result;
    }

    private async Task<Dictionary<string, List<InvoiceSearchRow>>> FetchInvoicesByClientAsync(DateOnly end, CancellationToken cancellationToken)
    {
        var invoicesById = new Dictionary<int, InvoiceSearchRow>();

        foreach (var onlyRecurring in new[] { false, true })
        {
            var skip = 0;
            while (true)
            {
                var page = await _api.ListInvoicesAsync(
                    query: null,
                    skip,
                    limit: PageSize,
                    field: "DateCreated",
                    dir: "desc",
                    onlyRecurring,
                    cancellationToken);

                var rows = page?.Data ?? [];
                foreach (var row in rows)
                    invoicesById[row.InvoiceId] = row;

                if (rows.Count < PageSize || page is null || skip + PageSize >= page.Total)
                    break;

                skip += PageSize;
            }
        }

        return invoicesById.Values
            .Where(invoice => !string.IsNullOrWhiteSpace(invoice.ClientId))
            .Where(invoice => DateOnly.FromDateTime(invoice.DateCreated) <= end)
            .GroupBy(invoice => invoice.ClientId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(invoice => invoice.DateCreated).ThenBy(invoice => invoice.InvoiceId).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<int, InvoiceHeader>> FetchInvoiceHeadersAsync(
        Dictionary<string, List<InvoiceSearchRow>> invoicesByClient,
        HashSet<string> qualifiedClientIds,
        DateOnly start,
        DateOnly end,
        bool quiet,
        CancellationToken cancellationToken)
    {
        var headers = new Dictionary<int, InvoiceHeader>();
        var clientIndex = 0;

        foreach (var clientId in qualifiedClientIds)
        {
            clientIndex++;
            if (!invoicesByClient.TryGetValue(clientId, out var invoices))
                continue;

            foreach (var invoice in invoices)
            {
                var date = DateOnly.FromDateTime(invoice.DateCreated);
                if (date < start || date > end)
                    continue;

                var header = await _api.GetInvoiceAsync(invoice.InvoiceId, cancellationToken);
                if (header is not null)
                    headers[invoice.InvoiceId] = header;
            }

            if (!quiet && clientIndex % 10 == 0)
                AnsiConsole.MarkupLine($"[dim]Invoice headers: {clientIndex}/{qualifiedClientIds.Count} clients[/]");
        }

        return headers;
    }

    private static List<ClientBillableWorkRow> BuildRows(
        Dictionary<string, ClientWork> workByClient,
        HashSet<string> qualifiedClientIds,
        Dictionary<string, List<InvoiceSearchRow>> invoicesByClient,
        Dictionary<int, InvoiceHeader> invoiceHeaders,
        DateOnly start,
        DateOnly end)
    {
        var rows = new List<ClientBillableWorkRow>();

        foreach (var clientId in qualifiedClientIds)
        {
            var work = workByClient[clientId];
            invoicesByClient.TryGetValue(clientId, out var invoices);
            invoices ??= [];

            var firstInvoice = invoices.FirstOrDefault();
            var invoicesInWindow = invoices
                .Where(invoice =>
                {
                    var date = DateOnly.FromDateTime(invoice.DateCreated);
                    return date >= start && date <= end;
                })
                .ToList();

            var invoicedIncGst = invoicesInWindow.Sum(invoice => invoice.SellTotal);
            var invoicedExGst = invoicesInWindow.Sum(invoice =>
                invoiceHeaders.TryGetValue(invoice.InvoiceId, out var header)
                    ? header.SubTotal ?? invoice.SellTotal
                    : invoice.SellTotal);

            rows.Add(new ClientBillableWorkRow(
                ClientId: clientId,
                ClientName: work.ClientName,
                FirstInvoiceDate: firstInvoice is null ? null : DateOnly.FromDateTime(firstInvoice.DateCreated),
                FirstInvoiceId: firstInvoice?.InvoiceId,
                FirstInvoiceType: firstInvoice?.InvoiceType,
                BillableTimesheetValueExGst: work.BillableTimesheetValueExGst,
                BillableHoursBAndBpp: work.BillableHoursBAndBpp,
                BillableHoursB: work.BillableHoursB,
                BillableHoursBpp: work.BillableHoursBpp,
                BillableTimesheetCount: work.BillableTimesheetCount,
                EmployeeCount: work.EmployeeIds.Count,
                ProjectCount: work.ProjectIds.Count,
                InvoiceCountInWindow: invoicesInWindow.Count,
                InvoicedExGstInWindow: invoicedExGst,
                InvoicedIncGstInWindow: invoicedIncGst));
        }

        return rows
            .OrderByDescending(row => row.BillableTimesheetValueExGst)
            .ThenBy(row => row.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task WriteCsvAsync(string path, List<ClientBillableWorkRow> rows, CancellationToken cancellationToken)
    {
        await using var stream = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await stream.WriteLineAsync(string.Join(",", CsvColumns.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            var values = new[]
            {
                row.ClientId,
                row.ClientName,
                row.FirstInvoiceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                row.FirstInvoiceId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.FirstInvoiceType ?? string.Empty,
                Money(row.BillableTimesheetValueExGst),
                Number(row.BillableHoursBAndBpp),
                Number(row.BillableHoursB),
                Number(row.BillableHoursBpp),
                row.BillableTimesheetCount.ToString(CultureInfo.InvariantCulture),
                row.EmployeeCount.ToString(CultureInfo.InvariantCulture),
                row.ProjectCount.ToString(CultureInfo.InvariantCulture),
                row.InvoiceCountInWindow.ToString(CultureInfo.InvariantCulture),
                Money(row.InvoicedExGstInWindow),
                Money(row.InvoicedIncGstInWindow)
            };

            await stream.WriteLineAsync(string.Join(",", values.Select(EscapeCsv).ToArray()).AsMemory(), cancellationToken);
        }
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static readonly string[] CsvColumns =
    [
        "client_id",
        "client_name",
        "first_invoice_date",
        "first_invoice_id",
        "first_invoice_type",
        "billable_timesheet_value_ex_gst",
        "billable_hours_B_and_BPP",
        "billable_hours_B",
        "billable_hours_BPP",
        "billable_timesheet_count",
        "employee_count",
        "project_count",
        "invoice_count_in_window",
        "invoiced_ex_gst_in_window",
        "invoiced_inc_gst_in_window"
    ];

    private sealed class ClientWork(string clientId, string clientName)
    {
        public string ClientId { get; } = clientId;
        public string ClientName { get; set; } = clientName;
        public decimal BillableTimesheetValueExGst { get; set; }
        public decimal BillableHoursBAndBpp { get; set; }
        public decimal BillableHoursB { get; set; }
        public decimal BillableHoursBpp { get; set; }
        public int BillableTimesheetCount { get; set; }
        public HashSet<string> EmployeeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ProjectIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ClientBillableWorkRow(
        string ClientId,
        string ClientName,
        DateOnly? FirstInvoiceDate,
        int? FirstInvoiceId,
        string? FirstInvoiceType,
        decimal BillableTimesheetValueExGst,
        decimal BillableHoursBAndBpp,
        decimal BillableHoursB,
        decimal BillableHoursBpp,
        int BillableTimesheetCount,
        int EmployeeCount,
        int ProjectCount,
        int InvoiceCountInWindow,
        decimal InvoicedExGstInWindow,
        decimal InvoicedIncGstInWindow);
}
