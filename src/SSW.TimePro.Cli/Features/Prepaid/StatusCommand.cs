using System.ComponentModel;
using SSW.TimePro.Cli.Infrastructure.ApiClient;
using SSW.TimePro.Cli.Infrastructure.Config;
using SSW.TimePro.Cli.Infrastructure.Output;
using Spectre.Console.Cli;

namespace SSW.TimePro.Cli.Features.Prepaid;

[Description("Download the prepaid drawdown status PDF for an invoice")]
public class StatusCommand : AsyncCommand<StatusCommand.Settings>
{
    private readonly ITimeProApiClient _api;
    private readonly IConfigService _config;

    public class Settings : CommandSettings
    {
        [CommandArgument(0, "<INVOICE_ID>")]
        [Description("Prepaid invoice ID")]
        public int InvoiceId { get; set; }

        [CommandOption("--template <ID>")]
        [Description("Report template ID (default 0 = tenant default)")]
        [DefaultValue(0)]
        public int TemplateId { get; set; }

        [CommandOption("--output <FILE>")]
        [Description("Path to write the PDF to. Default: prepaid-{invoiceId}.pdf in the current dir")]
        public string? Output { get; set; }
    }

    public StatusCommand(ITimeProApiClient api, IConfigService config)
    {
        _api = api;
        _config = config;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (_config.LoadActiveTenantConfig() is null)
        {
            OutputHelper.WriteError("Not logged in. Run 'tp login --tenant <id>' first.");
            return 1;
        }

        try
        {
            var bytes = await _api.GetPrepaidStatusReportPdfAsync(
                settings.InvoiceId, settings.TemplateId, CancellationToken.None);

            var path = settings.Output ?? Path.Combine(
                Environment.CurrentDirectory, $"prepaid-{settings.InvoiceId}.pdf");

            await File.WriteAllBytesAsync(path, bytes);
            OutputHelper.WriteSuccess($"Wrote {bytes.Length:N0} bytes to {path}");
            return 0;
        }
        catch (ApiException ex)
        {
            OutputHelper.WriteError($"API error ({ex.StatusCode}): {ex.Message}");
            return 1;
        }
    }
}
