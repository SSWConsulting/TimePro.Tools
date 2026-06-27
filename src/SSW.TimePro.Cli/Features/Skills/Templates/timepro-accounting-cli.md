# TimePro Accounting (CLI)

Accountant-facing read-only access to SSW TimePro via the `tp` CLI. Pipe `--json` output into `jq` or Python to calculate totals, compare against Xero, or audit historical data.

## Setup
This skill reuses the tenant config already configured for `tp`. If `tp login` has been run, nothing else is needed. Otherwise run `tp login --tenant <id>` first.

Start with `tp info --json`; prefer it over `tp --version` because it includes active tenant, user, and update status.

The sibling skill `timepro-accounting` without the `-cli` suffix hits the same API via raw HTTP. Prefer this CLI skill when `tp` is installed.

## Before reconciling
Ask what the user is trying to verify before pulling broad data:

- Target: invoices, paid receipts, aged debtors, prepaid drawdown, unbilled work, credit notes, rates, or Xero/export parity.
- External evidence: Excel, CSV, Xero MCP, bank-feed MCP, or another system.
- Date basis: invoice date, created date, payment date, or service period.
- Tax basis: ex-GST, GST, inc-GST, or all three.
- Treatment: credit notes/write-offs netted into totals or reported separately.
- Tolerance: exact match, cents, rounding, or a larger materiality threshold.

Prefer a narrow evidence pack over a broad dump. The aim is to prove one comparison cleanly, then widen only if the first comparison is coherent.

## Quick reference
All commands accept `--json` for machine output.

```bash
# Invoices
tp invoice list --limit 50 --json
tp invoice list --query Northwind --field DateCreated --dir desc --json
tp invoice get <INV>
tp invoice lines <INV>
tp invoice timesheets <INV>
tp invoice timesheets <INV> --writeoff
tp invoice receipts <INV>

# Receipts
tp receipt list --limit 500 --field PaymentDate --dir desc --json
tp receipt get <RCPT>
tp receipt outstanding <CLIENT_ID>

# Credit notes, products, discounts
tp creditnote list --client <CLIENT_ID> --json
tp product list --json
tp product list --prepaid --json
tp product get <PROD_ID>
tp product discounts --client <CLIENT_ID>

# Rates, unbilled, outstanding
tp rate list --client <CLIENT_ID> --show-expired --json
tp client outstanding --json
tp unbilled list --client <CLIENT_ID> --json

# Client billable-work threshold report
tp client billable-work --from 2025-06-26 --to 2026-06-26 --threshold 50000 --json
tp client billable-work --from 2025-06-26 --to 2026-06-26 --threshold 50000 --output ./Reports/client-billable-work.csv

# Recurring invoice templates
tp recurring list --client <CLIENT_ID> --json
tp recurring get <ID>

# Prepaid drawdown
tp prepaid summary <INVOICE_ID> --json
tp prepaid status <INVOICE_ID> --output /tmp/prepaid.pdf

# Cross-employee/client/project timesheet query
tp query --from 2026-03-01 --to 2026-03-31 --json
tp query --from 2026-03-01 --to 2026-03-31 --client <CID> --json
```

## Common workflows

### Drill into an invoice
```bash
INV=142
tp invoice get $INV --json          > /tmp/inv_header.json
tp invoice lines $INV --json        > /tmp/inv_lines.json
tp invoice timesheets $INV --json   > /tmp/inv_ts.json
tp invoice receipts $INV --json     > /tmp/inv_receipts.json
```

Reconcile line `sellTotal` to invoice header `subTotal`, `subTotal + salesTaxAmt` to header `sellTotal`, receipt `paidTotal` to header `paidAmt`, and header `osAmt` to remaining outstanding.

### Monthly invoiced sales
```bash
tp invoice list --limit 500 --field DateCreated --dir desc --json \
  | jq '[.data[] | select(.dateCreated | startswith("2026-03"))] | {count: length, total: map(.sellTotal) | add}'
```

### Monthly receipts
```bash
tp receipt list --limit 500 --field PaymentDate --dir desc --json \
  | jq '[.data[] | select(.paymentDate | startswith("2026-03"))] | {count: length, total: (map(.paidTotal // .paid) | add | fabs)}'
```

### Common report patterns
Use CSV when the next step is Excel, Google Sheets, or a reconciliation upload. Prefer a stable column list and include counts/totals beside the CSV path in your final answer.

#### Clients with at least $50k invoiced revenue in the last 12 months
For invoice-backed revenue, filter by the invoiced totals in the report output. Use `--threshold 0` so the JSON includes clients with billable work below the report's normal $50k threshold, then qualify by invoiced revenue.

```bash
FROM=2025-06-27
TO=2026-06-27

tp client billable-work --from "$FROM" --to "$TO" --threshold 0 --json \
  | jq -r '
      .rows
      | map(select(.invoicedExGstInWindow >= 50000))
      | (["clientId","clientName","firstInvoiceDate","invoiceCountInWindow","invoicedExGstInWindow","invoicedIncGstInWindow"],
         (.[] | [.clientId,.clientName,.firstInvoiceDate,.invoiceCountInWindow,.invoicedExGstInWindow,.invoicedIncGstInWindow]))
      | @csv' \
  > /tmp/timepro-clients-50k-revenue.csv
```

If product-only invoices are in scope, page `tp invoice list` and group by `clientId` instead of relying on the billable-work report.

#### Non-zero timesheets with 0% tax on non-zero-tax invoices
Use this when checking for timesheet tax drift after invoice allocation.

```bash
tp accounting guide --use-case "0% tax timesheets on taxable invoice" --json
```

If the guide matches the investigation, use the `timepro-accounting-tax-mismatch`
skill. It composes read-only `tp invoice ...` evidence into
`/tmp/timepro-tax-mismatch.csv` without requiring a dedicated diagnostic command.

### Aged debtors for one client
```bash
tp receipt outstanding NWIND
tp receipt outstanding NWIND --json
```

### Unbilled revenue
```bash
tp client outstanding --json
tp unbilled list --client NWIND --json
```

### Clients above a billable-work threshold
```bash
tp client billable-work --from 2025-06-26 --to 2026-06-26 --threshold 50000 --json \
  | jq '.rows | map({clientId, clientName, firstInvoiceDate, billableTimesheetValueExGst})'
```

This report returns an envelope with `clientCount`, `startDate`, `endDate`, `thresholdExGst`, `totals`, and `rows`.

### Push JSON to CSV
Use `jq @csv` for simple flat records and Python `csv.DictWriter` when the report needs loops, joins, paging, or nested records.

```bash
tp receipt list --limit 500 --field PaymentDate --dir desc --json \
  | jq -r '
      .data
      | (["receiptId","invoiceId","paymentDate","clientName","paidTotal"],
         (.[] | [.saleReceiptId,.invoiceId,.paymentDate,.coName,(.paidTotal // .paid)]))
      | @csv' \
  > /tmp/timepro-paid-receipts.csv
```

## Deep reconciliation diagnostics

### Invoice evidence pack
For a single invoice, fetch the header, product lines, allocated timesheets, write-offs, and receipts. Compare each subtotal deliberately.

```bash
INV=142
tp invoice get $INV --json          > /tmp/tp-invoice-header.json
tp invoice lines $INV --json        > /tmp/tp-invoice-lines.json
tp invoice timesheets $INV --json   > /tmp/tp-invoice-timesheets.json
tp invoice timesheets $INV --writeoff --json > /tmp/tp-invoice-writeoffs.json
tp invoice receipts $INV --json     > /tmp/tp-invoice-receipts.json
```

Check:
- Header `subTotal + salesTaxAmt == sellTotal`.
- Sum of line `sellTotal` matches header `subTotal`.
- Sum of allocated timesheet ex-GST amounts explains the billable work on the invoice.
- Receipt absolute total matches paid amount.
- Outstanding amount matches invoice total minus paid amount.
- Write-offs are included only when the user asks for work performed rather than work billed.

### CSV or Excel comparison
When the user provides a CSV/Excel export, normalize it before comparing:

```bash
# Example CSV shape: invoiceId,clientId,date,totalIncGst,paidIncGst
csvcut -c invoiceId,clientId,date,totalIncGst,paidIncGst external.csv \
  | csvsort -c invoiceId > /tmp/external-normalized.csv

tp invoice list --limit 500 --field DateCreated --dir desc --json \
  | jq -r '.data[] | [.invoiceId, .clientId, .dateCreated[0:10], .sellTotal, .paidAmt] | @csv' \
  | sort > /tmp/timepro-normalized.csv

diff -u /tmp/external-normalized.csv /tmp/timepro-normalized.csv
```

If the spreadsheet was generated from another system, do not assume its sign convention or date field matches TimePro. Prove both.

### Another MCP comparison
When another MCP is available, use it for the external side and keep TimePro evidence read-only:

1. Ask the external MCP for matching invoices/receipts by external reference, invoice number, client, date, and amount.
2. Ask TimePro for the matching evidence with `tp invoice ...`, `tp receipt ...`, or `tp prepaid ...`.
3. Compare normalized records: ids/references, dates, ex-GST, GST, inc-GST, paid, outstanding, and status.
4. Report unmatched records in both directions, not just amount deltas.

### Guide-backed diagnostics
Prefer the accounting guide before manually stitching primitives:

- `tp accounting guide --json` asks the right setup questions for Excel, CSV, Xero MCP, bank MCP, or another external source.
- `timepro-accounting-tax-mismatch` composes invoice and allocated-timesheet evidence for 0% tax drift checks.
- `timepro-accounting-invoice-diagnostics` assembles an invoice evidence pack from header, lines, allocated/write-off timesheets, receipts, and credit notes.
- `timepro-accounting-client-diagnostics` assembles client-level invoice, debt, unbilled, credit note, rate, and external comparison evidence.

If using `tp mcp` with accounting enabled, MCP exposes primitive read-only tools.
Use skills or guide-backed markdown to compose multi-step diagnostics locally.

Enable this MCP surface once with:

```bash
tp feature accounting enable
```

## Data gotchas
- Receipt `paidTotal` is negative for incoming payments in raw JSON. Report positive sales with `abs()`.
- Date field choice matters: receipts use `paymentDate` for money in, invoices often use `dateCreated` for raised.
- Paged list endpoints may need a high `--limit` or `--skip` walk for full months.
- Tax rates may arrive as `0.1` or `10` for 10%. Normalize before comparing.
- State whether GST is included or excluded.
- State whether credit notes are netted off or reported separately.
- Always show record count alongside totals.

## External-system comparison nudges
- Ask which system is authoritative for each field: invoice identity, customer, payment date, tax rate, tax amount, and status.
- Normalize before comparing: ids/references, dates/time zones, currency, tax basis, sign convention, and rounding.
- With another MCP such as Xero, ask that MCP for the closest matching records and field meanings first; do not assume the tool names or field names match TimePro.
- Report unmatched records in both directions plus amount deltas. Avoid saying "Xero is wrong" or "TimePro is wrong" until the basis and sign conventions are proven.

{{CURRENT_CONFIGURATION}}
