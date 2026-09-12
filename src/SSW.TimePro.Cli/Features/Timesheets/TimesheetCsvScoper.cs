using System.Text;

namespace SSW.TimePro.Cli.Features.Timesheets;

/// <summary>
/// Narrows the company-wide timesheet CSV export down to one employee. The export endpoint
/// only takes a date range, so the scoping has to happen here, on the returned file.
/// </summary>
public static class TimesheetCsvScoper
{
    private const string EmpIdHeader = "EmpID";

    public sealed record ScopeResult(string Csv, int TotalRows, int KeptRows, bool Filtered, string? Warning);

    /// <summary>
    /// Return the export unchanged. Keeping every employee's rows is a deliberate choice, so it
    /// has its own entry point rather than being what a missing scope id happens to do.
    /// </summary>
    public static ScopeResult Unscoped(string csv)
    {
        var dataRows = Math.Max(0, ReadRecords(csv).Count - 1);
        return new ScopeResult(csv, dataRows, dataRows, false, null);
    }

    /// <summary>
    /// Keep only the rows whose <c>EmpID</c> matches <paramref name="empId"/>. An export without
    /// an <c>EmpID</c> column comes back unchanged with <see cref="ScopeResult.Filtered"/> false
    /// and a warning. A blank <paramref name="empId"/> throws: silently widening the scope to
    /// every employee is the one outcome a caller asking for one employee must never get.
    /// </summary>
    public static ScopeResult Scope(string csv, string empId)
    {
        if (string.IsNullOrWhiteSpace(empId))
            throw new ArgumentException(
                "An employee id is required to scope the export. Use Unscoped to keep every employee's rows.",
                nameof(empId));

        var records = ReadRecords(csv);
        if (records.Count == 0)
            return new ScopeResult(csv, 0, 0, false, null);

        var dataRows = records.Count - 1;

        var header = records[0];
        var column = Array.FindIndex(
            header.Fields,
            f => string.Equals(f.Trim(), EmpIdHeader, StringComparison.OrdinalIgnoreCase));

        if (column < 0)
            return new ScopeResult(csv, dataRows, dataRows, false,
                $"The export has no '{EmpIdHeader}' column, so it could not be scoped to one employee. Writing the full export.");

        var kept = records
            .Skip(1)
            .Where(r => column < r.Fields.Length &&
                        string.Equals(r.Fields[column].Trim(), empId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var fallbackTerminator = records.Any(r => r.Terminator == "\r\n") ? "\r\n" : "\n";
        var builder = new StringBuilder();
        Append(builder, header, fallbackTerminator);
        foreach (var row in kept)
            Append(builder, row, fallbackTerminator);

        return new ScopeResult(builder.ToString(), dataRows, kept.Count, true, null);
    }

    private static void Append(StringBuilder builder, Record record, string fallbackTerminator)
    {
        builder.Append(record.Raw);
        builder.Append(record.Terminator.Length > 0 ? record.Terminator : fallbackTerminator);
    }

    private sealed record Record(string Raw, string Terminator, string[] Fields);

    /// <summary>
    /// Split the CSV into records, honouring RFC 4180 quoting: a delimiter or newline inside a
    /// quoted field is data, not a boundary. Each record keeps its original text so unfiltered
    /// rows are written back byte-for-byte.
    /// </summary>
    private static List<Record> ReadRecords(string csv)
    {
        var records = new List<Record>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var recordStart = 0;
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r' or '\n':
                    var terminator = c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n' ? "\r\n" : c.ToString();
                    fields.Add(field.ToString());
                    field.Clear();
                    AddRecord(records, csv[recordStart..i], terminator, fields);
                    fields.Clear();
                    i += terminator.Length - 1;
                    recordStart = i + 1;
                    break;

                default:
                    field.Append(c);
                    break;
            }
        }

        if (recordStart < csv.Length)
        {
            fields.Add(field.ToString());
            AddRecord(records, csv[recordStart..], string.Empty, fields);
        }

        return records;
    }

    private static void AddRecord(List<Record> records, string raw, string terminator, List<string> fields)
    {
        if (fields.Count == 1 && fields[0].Length == 0)
            return;

        records.Add(new Record(raw, terminator, fields.ToArray()));
    }
}
