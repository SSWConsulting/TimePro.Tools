# Accountant commands (read-only)

This doc covers `tp`'s invoice, receipt, credit-note, product, rate,
outstanding, billable-work threshold, unbilled, recurring, and prepaid
commands — everything an accountant needs to explore SSW TimePro data,
reconcile totals, or cross-check against Xero.

All commands are **read-only**. Writes (creating invoices, receipts, credit
notes, etc.) stay in the TimePro web app. For timesheeting commands (create /
update / accept / suggest), see the main [README](../README.md).

## Setup

Accountant commands reuse the tenant config already used by the rest of `tp`.
If `tp login --tenant <id>` has been run (check `~/.config/timepro-cli/tenants/`),
nothing else is needed.

## Command reference

All commands accept `--json` for machine-readable output.

### Invoices — `tp invoice` (alias `tp inv`)

```bash
tp invoice list                              # paged list (default 50 rows)
tp invoice list --query Northwind --limit 200 # filter + larger page
tp invoice list --field DateCreated --dir desc
tp invoice list --recurring                  # only recurring-generated invoices

tp invoice get <INVOICE_ID>                  # header: totals, GST, paid, OS
tp invoice lines <INVOICE_ID>                # line items (products billed)
tp invoice timesheets <INVOICE_ID>           # timesheets allocated
tp invoice timesheets <INVOICE_ID> --writeoff  # written-off timesheets
tp invoice receipts <INVOICE_ID>             # payments against the invoice
```

Paging flags on `list`: `--skip N`, `--limit N`, `--field COL`, `--dir asc|desc`.

### Receipts — `tp receipt` (alias `tp rcpt`)

```bash
tp receipt list                              # paid receipts, paged
tp receipt list --search "Northwind" --limit 500
tp receipt list --field PaymentDate --dir desc

tp receipt get <RECEIPT_ID>                  # detail + invoice allocations
tp receipt outstanding <CLIENT_ID>           # aged-debtor view
```

### Credit notes — `tp creditnote` (alias `tp cn`)

```bash
tp creditnote list --client <CLIENT_ID>
```

### Products — `tp product` (alias `tp prod`)

```bash
tp product list                              # products
tp product list --expand                     # include SKU counts
tp product list --prepaid                    # all prepaid SKUs
tp product get <PRODUCT_ID>
tp product discounts --client <CLIENT_ID>
```

### Rates — `tp rate`

```bash
tp rate get --client <CLIENT_ID>             # current employee, as-of date
tp rate list --client <CLIENT_ID>            # all configured rates
tp rate list --client <CLIENT_ID> --show-expired
tp rate list --client <CLIENT_ID> --emp-id <EMP_ID>
```

### Outstanding / unbilled

```bash
tp client outstanding                        # clients with unbilled time
tp unbilled list --client <CLIENT_ID>        # the timesheets themselves
tp receipt outstanding <CLIENT_ID>           # outstanding invoices (aged)
```

### Client billable-work threshold — `tp client billable-work`

```bash
tp client billable-work                      # last 12 months, $50k threshold, CSV under ./Reports
tp client billable-work --from 2025-06-26 --to 2026-06-26 --threshold 50000 --json
tp client billable-work --from 2025-06-26 --to 2026-06-26 --threshold 50000 --output ./Reports/client-billable-work.csv
```

JSON output is a report envelope with `startDate`, `endDate`,
`thresholdExGst`, `clientCount`, `totals`, and `rows`. Client data is under
`.rows[]`; use `.rows | map(...)` in `jq`, not a top-level array filter.

### Recurring invoice templates — `tp recurring`

```bash
tp recurring list                            # all templates
tp recurring list --client <CLIENT_ID>
tp recurring list --outdated                 # include stopped/inactive
tp recurring get <RECURRING_ID>              # details + product lines
```

### Prepaid drawdown — `tp prepaid`

```bash
tp prepaid summary <INVOICE_ID> --json       # structured drawdown totals
tp prepaid status <INVOICE_ID>               # writes prepaid-<id>.pdf in cwd
tp prepaid status <INVOICE_ID> --output /tmp/prepaid.pdf
tp prepaid status <INVOICE_ID> --template <TEMPLATE_ID>
```

`tp prepaid summary` composes existing read-only endpoints and returns
`exGst`, `gst`, and `incGst` totals for `original`, `drawnDown`, `credited`,
and `remaining`. The `remaining.exGst` value is sourced from the ledger-backed
`remainingPrepaidCredit` exposed by the client invoice table endpoint. The PDF
command remains available when the rendered report is needed.

## Common workflows

### 1. Drill into an invoice

```bash
INV=142
tp invoice get $INV --json         > /tmp/inv_header.json
tp invoice lines $INV --json       > /tmp/inv_lines.json
tp invoice timesheets $INV --json  > /tmp/inv_ts.json
tp invoice receipts $INV --json    > /tmp/inv_receipts.json
```

Reconciliation: sum of line `sellTotal` should equal invoice header `subTotal`
(ex-GST); `subTotal + salesTaxAmt` should equal header `sellTotal` (inc-GST);
sum of `abs(paidTotal)` on receipts should equal header `paidAmt`; header
`osAmt` should equal total minus paid.

### 2. Monthly invoiced sales

```bash
tp invoice list --limit 500 --field DateCreated --dir desc --json \
  | jq '[.data[] | select(.dateCreated | startswith("2026-03"))]
        | {count: length, total: (map(.sellTotal) | add)}'
```

### 3. Monthly receipts (money in)

```bash
tp receipt list --limit 500 --field PaymentDate --dir desc --json \
  | jq '[.data[] | select(.paymentDate | startswith("2026-03"))]
        | {count: length, total: (map(.paidTotal // .paid) | add | fabs)}'
```

### 4. Aged debtors for one client

```bash
tp receipt outstanding NWIND           # human table
tp receipt outstanding NWIND --json    # further processing
```

### 5. Unbilled revenue

```bash
tp client outstanding --json           # all clients with unbilled time
tp unbilled list --client NWIND --json
```

### 6. Clients above a billable-work threshold

```bash
tp client billable-work --from 2025-06-26 --to 2026-06-26 --threshold 50000 --json \
  | jq '.rows | map({clientId, clientName, firstInvoiceDate, billableTimesheetValueExGst})'
```

Use this when the question is "which clients had at least X billable work in
the last 12 months?" The report calculates billable `B` and `BPP` timesheet
value ex-GST, includes each client's first invoice date, and can write a CSV to
`./Reports` for spreadsheet review.

### 7. Clients with at least $50k invoiced revenue in the last 12 months

For invoice-backed revenue, qualify by invoiced totals instead of billable work.
Use `--threshold 0` so the JSON includes all clients with billable work, then
filter by revenue.

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

If product-only invoices are in scope, page `tp invoice list` and group by
`clientId` instead of relying on the billable-work report.

### 8. Non-zero timesheets with 0% tax on non-zero-tax invoices

Use this when checking for timesheet tax drift after invoice allocation.

```bash
tp accounting guide --use-case "0% tax timesheets on taxable invoice" --json
```

If the guide matches the investigation, use
`guides/accounting/tax-mismatch.md`. It composes read-only invoice and
allocated-timesheet commands into a CSV report without requiring a dedicated
diagnostic command.

### 9. Credit-note audit

```bash
tp creditnote list --client NWIND --json \
  | jq 'sort_by(.creditNoteDate) | map({id, date: .creditNoteDate, amount, note})'
```

### 10. Push JSON to CSV

Use `jq @csv` for simple flat records and Python `csv.DictWriter` when the
report needs loops, joins, paging, or nested records.

```bash
tp receipt list --limit 500 --field PaymentDate --dir desc --json \
  | jq -r '
      .data
      | (["receiptId","invoiceId","paymentDate","clientName","paidTotal"],
         (.[] | [.saleReceiptId,.invoiceId,.paymentDate,.coName,(.paidTotal // .paid)]))
      | @csv' \
  > /tmp/timepro-paid-receipts.csv
```

## MCP — the same surface as tools

Accounting MCP tools are opt-in. Enable them once:

```bash
tp feature accounting enable
```

`tp mcp` (stdio transport) then exposes every read-only endpoint above as a
tool, plus cross-domain reads useful to accountants (timesheet queries,
billing-type / category / location codes, current user, project summaries).

Start it the same way the timesheet tools are already configured in your MCP
client (Claude Code / VS Code / Codex — see the main README's MCP section).
Before enabling or changing MCP features, ask what the MCP use-case is: Excel
or CSV comparison, Xero MCP composition, bank-feed MCP comparison, prepaid
drawdown, aged debtors, invoice diagnosis, or another workflow.

Accounting tool names (complete list):

- Invoices: `ListInvoices`, `GetInvoice`, `GetInvoiceLines`,
  `GetInvoiceTimesheets`, `GetInvoiceReceipts`, `GetInvoicesByClient`,
  `GetUnpaidInvoicesByClient`
- Receipts: `ListPaidReceipts`, `GetReceiptDetail`, `GetClientOutstanding`
- Credit notes: `ListCreditNotes`
- Products: `ListProducts`, `GetProduct`, `ListAllSkus`,
  `GetProductDiscountsForClient`
- Rates / outstanding: `ListClientRates`, `GetClientsWithOutstandingTime`,
  `GetUnbilledTimesheetsForClient`
- Recurring: `ListRecurringInvoices`, `GetRecurringInvoice`
- Prepaid: `GetPrepaidStatus` (structured JSON),
  `GetPrepaidStatusPdf` (base64-wrapped PDF)
- Cross-domain: `QueryTimesheets`, `GetCurrentUser`, `ListCategories`,
  `ListBillableTypes`, `ListLocations`, `GetProjectsSummary`

MCP tools should mirror CLI behavior. If a report contains business logic,
prefer a CLI command or guide-backed skill first:

- `tp accounting guide --json`
- `guides/accounting/tax-mismatch.md`
- `guides/accounting/invoice-evidence-pack.md`
- `guides/accounting/client-accounting-position.md`

The timesheet-side tools (`GetTimesheets`, `SearchClients`,
`GetProjectsForClient`, `GetClientRate`, `GetCrmBookings`, `ListIterations`,
...) remain exposed by `LookupMcpTools` / `TimesheetMcpTools`, so an
accountant session has access to the full read surface in one MCP server.

## Xero cross-check (via MCP composition)

For reconciling TimePro against Xero, there's no bespoke feature — connect
a Xero MCP server alongside `tp mcp` in Claude Code and let an agent call
tools from both:

- *"Reconcile TimePro paid receipts for March 2026 against Xero bank receipts.
  Call `ListPaidReceipts` in TimePro for March; fetch the equivalent Xero bank
  rec for the same period; flag any receipts in one system but not the other,
  or amount mismatches >$0.01 keyed on invoice reference."*

- *"For prepaid invoice 142, call `GetPrepaidStatus` and compare
  `remaining.exGst` against Xero manual journals tagged with that invoice.
  Confirm the drawdown entries net to the Xero journal totals."*

- *"Find TimePro invoices where `externalSyncStatus != 1`; for each, check
  whether a matching invoice exists in Xero. Report what's missing."*

## Two skills, one tool

The `~/.claude/skills/timepro-accounting/SKILL.md` and
`~/.claude/skills/timepro-sales/SKILL.md` skills (raw `curl` against the
TimePro API with a `.env` file) are preserved intact. They're the right tool
when `tp` isn't installed, when demonstrating the raw HTTP shape, or when
teaching someone the API.

The CLI-based sibling is generated by:

```bash
tp feature accounting enable
tp skills create .claude
# writes .claude/skills/timepro-accounting-cli/SKILL.md
```

It uses the same tenant config as the rest of `tp`, so there's no separate
API key to rotate. Both skills can coexist — the `-cli` suffix prevents any
name collision even if you drop the generated file into `~/.claude/skills/`.

## Data gotchas

- **Receipt sign convention**: `paidTotal` is **negative** for incoming
  payments (the receipt type's `typeSign` encodes direction). Default table
  output shows absolute values; JSON preserves the raw sign. Report sales as
  `abs(sum)`.
- **Date field choice matters**:
  - Receipts: `paymentDate` (money in) vs `dateCreated` (entered).
  - Invoices: `dateCreated` (raised) vs `dateStart` / `dateEnd` (period).
  - SQL reports sometimes filter on invoice date, which excludes March
    payments against pre-March invoices. Confirm the convention before
    comparing totals.
- **`dateFrom` / `dateTo` on paged endpoints are ignored** — fetch by sort
  order and page, then filter client-side with `jq` or Python.
- **Paging**: `tp invoice list` and `tp receipt list` default to limit 50/100.
  Raise `--limit` or walk `--skip` when covering full months.
- **GST**: Invoice header `subTotal` is GST-**exclusive**, `salesTaxAmt` is the
  GST component, and `sellTotal` is GST-**inclusive**. Invoice line `sellAmt`
  and `sellTotal` are GST-exclusive. Timesheet `sellTotal`, `billableAmount`,
  and `amount` are treated as GST-exclusive for reconciliation; use
  `salesTaxAmt` / `salesTaxPct` when present, or the invoice header rate.
- **Credit notes** are negative-signed adjustments. Decide whether to net
  them off or report separately before presenting a number.
- **Write-offs**: Timesheets allocated to an invoice may be written off.
  Include both `allocated` and `--writeoff` views when auditing total hours.

## Output etiquette

When presenting numbers to the user:

- State the **date field** used (`paymentDate` / `dateCreated`).
- State whether **GST** is included or excluded.
- State whether **credit notes** are netted off or shown separately.
- Show **record count** alongside any total — single-number answers hide
  filter mistakes.
