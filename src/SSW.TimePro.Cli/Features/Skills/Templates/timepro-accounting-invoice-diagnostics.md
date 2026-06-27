# TimePro Accounting Invoice Diagnostics

Use this guide to assemble a read-only invoice evidence pack. It is intended for accountants and agents comparing TimePro to Excel, CSV, Xero MCP, bank-feed MCP, or another external source.

Start with the guide router:

```bash
tp accounting guide --use-case "invoice reconciliation evidence pack" --json
```

## Gather Evidence
Use the smallest invoice anchor first.

```bash
INV=142
tp invoice get "$INV" --json > /tmp/tp-invoice-header.json
tp invoice lines "$INV" --json > /tmp/tp-invoice-lines.json
tp invoice timesheets "$INV" --json > /tmp/tp-invoice-timesheets.json
tp invoice timesheets "$INV" --writeoff --json > /tmp/tp-invoice-writeoffs.json
tp invoice receipts "$INV" --json > /tmp/tp-invoice-receipts.json
```

If the client ID is known, also capture related credit notes:

```bash
CLIENT=NWIND
tp creditnote list --client "$CLIENT" --json > /tmp/tp-client-creditnotes.json
```

## Calculate Totals
Use a script when the evidence has nested records or mixed signs.

```bash
python3 - <<'PY'
import json

def load(path):
    with open(path) as f:
        return json.load(f)

def money(value):
    return float(value or 0)

invoice = load("/tmp/tp-invoice-header.json")
lines = load("/tmp/tp-invoice-lines.json")
timesheets = load("/tmp/tp-invoice-timesheets.json")
writeoffs = load("/tmp/tp-invoice-writeoffs.json")
receipts = load("/tmp/tp-invoice-receipts.json")

line_ex = sum(money(r.get("sellTotal") or r.get("sellAmt")) for r in lines)
timesheet_ex = sum(money(r.get("billableAmount") or r.get("sellTotal") or r.get("amount")) for r in timesheets)
writeoff_ex = sum(money(r.get("billableAmount") or r.get("sellTotal") or r.get("amount")) for r in writeoffs)
paid = sum(abs(money(r.get("paidTotal") or r.get("paid"))) for r in receipts)

summary = {
    "invoiceId": invoice.get("invoiceId"),
    "clientId": invoice.get("clientId"),
    "headerSubTotalExGst": money(invoice.get("subTotal")),
    "headerGst": money(invoice.get("salesTaxAmt")),
    "headerTotalIncGst": money(invoice.get("sellTotal")),
    "headerPaid": money(invoice.get("paidAmt")),
    "headerOutstanding": money(invoice.get("osAmt")),
    "lineTotalExGst": line_ex,
    "timesheetTotalExGst": timesheet_ex,
    "writeoffTotalExGst": writeoff_ex,
    "receiptPaidTotal": paid,
    "lineDeltaExGst": line_ex - money(invoice.get("subTotal")),
    "paidDelta": paid - money(invoice.get("paidAmt")),
}

print(json.dumps(summary, indent=2))
PY
```

## Check
Verify:

- Header `subTotal + salesTaxAmt == sellTotal`.
- Product line ex-GST totals explain header subtotal.
- Allocated timesheets explain the billable work portion.
- Write-offs are included only when the question is about work performed rather than work billed.
- Receipt absolute totals explain paid amount.
- Outstanding amount equals invoice total minus paid amount.
- Credit notes are netted only when the user asks for net sales.

Report record counts, deltas, date field used, GST basis, and whether credit notes/write-offs were included.
