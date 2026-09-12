using System.Text.Json.Serialization;

namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Receipt-payment row from /api/v2/ClientInvoice/{id}/receipts, also nested under
/// <see cref="PaidReceiptRow.SaleReceiptPaids"/>. One row per invoice a receipt pays.
///
/// Note: Paid is typically NEGATIVE for incoming payments — the receipt type's TypeSign
/// encodes direction. Report positive sales with abs().
/// </summary>
public class ReceiptRow
{
    public int SaleReceiptId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Paid { get; set; }
    public string? CoName { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? PaymentDate { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? EmpUpdated { get; set; }
    public string? Note { get; set; }
    public string? SaleReceiptStatus { get; set; }
    public int? CreditNoteId { get; set; }
    public bool? IsCreditingPrepaid { get; set; }
}

/// <summary>
/// Receipt row from /api/receipting/PaidReceiptsPaged. Receipt-level, so it carries the
/// total and the list of invoices it was allocated across rather than a single invoice ID.
/// </summary>
public class PaidReceiptRow
{
    public int SaleReceiptId { get; set; }
    public string? ClientId { get; set; }
    public string? CoName { get; set; }
    public string? ClientFirstName { get; set; }
    public string? ClientSurname { get; set; }
    public DateTime? PaymentDate { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? EmpUpdated { get; set; }
    public string? Note { get; set; }
    public string? Bank { get; set; }
    public string? Batch { get; set; }
    public string? Branch { get; set; }
    public string? Drawer { get; set; }
    public string? Month { get; set; }
    public string? CategoryId { get; set; }
    public string? ExportId { get; set; }
    public decimal Unallocated { get; set; }
    public decimal PaidTotal { get; set; }
    public List<int> InvoiceIds { get; set; } = [];
    public List<ReceiptRow> SaleReceiptPaids { get; set; } = [];
    public ReceiptTypeInfo? SaleReceiptType { get; set; }
    public string? ExternalSyncType { get; set; }
    public string? ExternalSyncId { get; set; }
    public int? CreditNoteId { get; set; }
}

/// <summary>
/// Nested receipt type info. Only PaidReceiptsPaged populates the fields beyond Id/TypeName/TypeSign.
/// </summary>
public class ReceiptTypeInfo
{
    public string? Id { get; set; }
    public string? TypeName { get; set; }
    public string? TypeSign { get; set; }
    public int? Count { get; set; }
    public string? Note { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? EmpUpdated { get; set; }
    public bool? IsHardCoded { get; set; }
}

/// <summary>
/// Envelope returned by /api/Receipting/details/{id} — the receipt plus the lookup data
/// the web edit screen needs alongside it.
/// </summary>
public class ReceiptDetailResponse
{
    public ReceiptDetail? Receipt { get; set; }
    public string? ContactPerson { get; set; }
    public List<ReceiptPaymentMethod> PaymentMethods { get; set; } = [];
    public decimal TotalPaid { get; set; }
    public decimal InvoicesAmount { get; set; }
}

/// <summary>
/// The receipt inside <see cref="ReceiptDetailResponse"/>.
/// </summary>
public class ReceiptDetail
{
    /// <remarks>The endpoint serialises this one as a string.</remarks>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int SaleReceiptId { get; set; }

    public string? ReceiptType { get; set; }
    public string? ClientId { get; set; }
    public string? CategoryId { get; set; }
    public string? CoName { get; set; }
    public string? ClientFirstName { get; set; }
    public string? ClientSurname { get; set; }
    public DateTime? PaymentDate { get; set; }
    public bool IsNew { get; set; }
    public string? BatchNo { get; set; }
    public decimal ReceiptTotal { get; set; }
    public string? Note { get; set; }
    public List<ReceiptAllocation> SaleReceiptPaids { get; set; } = [];
    public List<ReceiptAllocation> OutstandingInvoices { get; set; } = [];
    public string? ExternalSyncType { get; set; }
    public string? ExternalSyncId { get; set; }
}

/// <summary>
/// One invoice allocation on a receipt detail: how much of the receipt went to that invoice.
/// </summary>
public class ReceiptAllocation
{
    public int InvoiceId { get; set; }
    public int SaleReceiptId { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? PaymentDate { get; set; }
    public decimal Total { get; set; }
    public decimal AlreadyPaidAmt { get; set; }
    public decimal PaidAmt { get; set; }
    public decimal Balance { get; set; }
    public bool IsAllocated { get; set; }
    public string? ClientId { get; set; }
    public string? CoName { get; set; }
    public string? SaleReceiptStatus { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// A payment method offered by the receipt detail endpoint.
/// </summary>
public class ReceiptPaymentMethod
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Sign { get; set; }
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
