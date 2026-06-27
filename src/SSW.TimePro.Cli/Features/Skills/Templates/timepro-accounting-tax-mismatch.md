# TimePro Accounting Tax Mismatch

Use this guide for non-zero timesheets that appear to have 0% tax while the client invoice has non-zero tax. Keep the work read-only and produce an evidence pack the user can compare with Excel, CSV, Xero MCP, or another accounting source.

Start with the guide router:

```bash
tp accounting guide --use-case "0% tax timesheets on taxable invoice" --json
```

## Inputs
Ask for:

- Client ID, invoice ID, or date window.
- Whether the comparison should use invoice created date, invoice date, or service period.
- Whether expected tax rates are expressed as `0.1`, `10`, or `10%`.
- Whether write-offs should be included or excluded.

## Gather Invoice Evidence
Prefer one invoice first. Widen only after the first mismatch shape is proven.

```bash
INV=142
tp invoice get "$INV" --json > /tmp/tp-invoice.json
tp invoice lines "$INV" --json > /tmp/tp-invoice-lines.json
tp invoice timesheets "$INV" --json > /tmp/tp-invoice-timesheets.json
tp invoice timesheets "$INV" --writeoff --json > /tmp/tp-invoice-writeoffs.json
```

For a client/date scan, page invoices and loop through candidate invoice IDs:

```bash
tp invoice list --query NWIND --limit 500 --field DateCreated --dir desc --json > /tmp/tp-invoices.json
```

## Detect Suspicious Rows
Normalize tax rates before comparison. Treat `0.1` and `10` as the same 10% rate.

```bash
python3 - <<'PY'
import csv, json, math

def pct(value):
    if value in (None, ""):
        return 0.0
    value = float(value)
    return value * 100 if abs(value) <= 1 else value

invoice = json.load(open("/tmp/tp-invoice.json"))
rows = json.load(open("/tmp/tp-invoice-timesheets.json"))
invoice_tax = pct(invoice.get("salesTaxPct"))
invoice_tax_amount = float(invoice.get("salesTaxAmt") or 0)

with open("/tmp/timepro-tax-mismatch.csv", "w", newline="") as f:
    writer = csv.DictWriter(f, fieldnames=[
        "invoiceId", "clientId", "invoiceTaxPct", "invoiceSalesTaxAmt",
        "timeId", "empId", "projectId", "categoryId", "billableId",
        "timesheetDate", "amountExGst", "timesheetTaxPct", "timesheetSalesTaxAmt"
    ])
    writer.writeheader()
    for row in rows:
        amount = float(row.get("billableAmount") or row.get("sellTotal") or row.get("amount") or 0)
        row_tax = pct(row.get("salesTaxPct"))
        if abs(amount) > 0.01 and invoice_tax > 0 and invoice_tax_amount > 0 and math.isclose(row_tax, 0.0):
            writer.writerow({
                "invoiceId": invoice.get("invoiceId"),
                "clientId": invoice.get("clientId"),
                "invoiceTaxPct": invoice_tax,
                "invoiceSalesTaxAmt": invoice.get("salesTaxAmt"),
                "timeId": row.get("timeId"),
                "empId": row.get("empId"),
                "projectId": row.get("projectId"),
                "categoryId": row.get("categoryId"),
                "billableId": row.get("billableId"),
                "timesheetDate": row.get("dateCreated") or row.get("date"),
                "amountExGst": amount,
                "timesheetTaxPct": row_tax,
                "timesheetSalesTaxAmt": row.get("salesTaxAmt"),
            })
PY
```

## Interpret
Check before calling it a bug:

- Was 0% tax legitimate for this client, billable type, category, or invoice?
- Did the timesheet predate a rate/tax configuration change?
- Did an import, write-off, or migration path bypass normal tax calculation?
- Does invoice tax come from product lines rather than the allocated timesheets?

Report the CSV path, row count, invoice IDs scanned, and the tax basis used.
