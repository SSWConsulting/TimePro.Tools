namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Paid receipt row from /api/receipting/PaidReceiptsPaged,
/// /api/receipting/GetPaidReceiptTableForClient, and /api/v2/ClientInvoice/{id}/receipts.
///
/// Note: PaidTotal is typically NEGATIVE for incoming payments — the SaleReceiptType.TypeSign
/// field encodes direction. Report positive sales with abs().
/// </summary>
public class ReceiptRow
{
    public int SaleReceiptId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Paid { get; set; }
    public decimal? PaidTotal { get; set; }
    public string? CoName { get; set; }
    public string? ClientId { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? PaymentDate { get; set; }
    public string? SaleReceiptStatus { get; set; }
    public string? Note { get; set; }
    public int? CreditNoteId { get; set; }
    public bool? IsCreditingPrepaid { get; set; }
    public ReceiptTypeInfo? SaleReceiptType { get; set; }
}

/// <summary>
/// Nested receipt type info (present on some endpoints like PaidReceiptsPaged).
/// </summary>
public class ReceiptTypeInfo
{
    public string? Id { get; set; }
    public string? TypeName { get; set; }
    public string? TypeSign { get; set; }
}

/// <summary>
/// Receipt detail view model from /api/Receipting/details/{id}.
/// Allocations list how a single receipt is split across multiple invoices.
/// </summary>
public class ReceiptDetail
{
    public int SaleReceiptId { get; set; }
    public string? ClientId { get; set; }
    public string? CoName { get; set; }
    public DateTime? PaymentDate { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? EmpUpdated { get; set; }
    public string? Note { get; set; }
    public string? SaleReceiptStatus { get; set; }
    public decimal? Total { get; set; }
    public decimal? PaidTotal { get; set; }
    public string? ReferenceCode { get; set; }
    public ReceiptTypeInfo? SaleReceiptType { get; set; }
    public List<ReceiptAllocation>? Allocations { get; set; }
}

/// <summary>
/// One invoice allocation within a receipt's detail view.
/// </summary>
public class ReceiptAllocation
{
    public int InvoiceId { get; set; }
    public decimal Paid { get; set; }
    public decimal? InvoiceTotal { get; set; }
    public decimal? Outstanding { get; set; }
    public DateTime? DateInvoiced { get; set; }
}

/// <summary>
/// Aged-debtor view from /api/Receipting/ClientOutstanding/{clientId}.
/// </summary>
public class ClientOutstandingSummary
{
    public string? ClientId { get; set; }
    public string? CoName { get; set; }
    public string? ClientFirstName { get; set; }
    public string? ClientSurname { get; set; }
    public string? ContactPerson { get; set; }
    public List<OutstandingInvoiceEntry>? OutstandingInvoices { get; set; }
}

/// <summary>
/// One outstanding-invoice row inside <see cref="ClientOutstandingSummary"/>.
/// Field names are loose because the backend reuses PaymentViewModel here.
/// </summary>
public class OutstandingInvoiceEntry
{
    public int InvoiceId { get; set; }
    public DateTime? DateInvoiced { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? Total { get; set; }
    public decimal? PaidAmt { get; set; }
    public decimal? OsAmt { get; set; }
    public int? DaysOverdue { get; set; }
    public string? InvoiceType { get; set; }
}

/// <summary>
/// Row from /api/clients/OutstandingTime — clients with unbilled time.
/// </summary>
public class ClientOutstandingTimeRow
{
    public string? ClientId { get; set; }
    public string? CoName { get; set; }
    public string? EmpId { get; set; }
    public string? FirstName { get; set; }
    public string? Surname { get; set; }
    public string? Suburb { get; set; }
    public string? State { get; set; }
    public decimal? Os { get; set; }
    public decimal? Billable { get; set; }
    public DateTime? DateUpdated { get; set; }
    public DateTime EarliestUnAllocatedTimesheetDate { get; set; }
}
