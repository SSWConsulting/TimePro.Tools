# TimePro Accounting Client Diagnostics

Use this guide to diagnose one client's accounting position using read-only TimePro evidence. It is suitable for aged debtors, unbilled work, invoice history, credit notes, rates, and external-system comparison.

Start with the guide router:

```bash
tp accounting guide --use-case "client accounting position" --json
```

## Gather Evidence
Use a single client ID.

```bash
CLIENT=NWIND
tp invoice list --query "$CLIENT" --limit 500 --field DateCreated --dir desc --json > /tmp/tp-client-invoices.json
tp receipt outstanding "$CLIENT" --json > /tmp/tp-client-aged-debtors.json
tp unbilled list --client "$CLIENT" --json > /tmp/tp-client-unbilled.json
tp creditnote list --client "$CLIENT" --json > /tmp/tp-client-creditnotes.json
tp rate list --client "$CLIENT" --show-expired --json > /tmp/tp-client-rates.json
tp recurring list --client "$CLIENT" --json > /tmp/tp-client-recurring.json
```

For a revenue threshold report:

```bash
FROM=2025-06-27
TO=2026-06-27
tp client billable-work --from "$FROM" --to "$TO" --threshold 0 --json > /tmp/tp-client-billable-work.json
```

## Summarise Position

```bash
python3 - <<'PY'
import json

def load(path):
    with open(path) as f:
        return json.load(f)

def rows(value):
    if isinstance(value, dict):
        return value.get("data") or value.get("rows") or []
    return value or []

def money(value):
    return float(value or 0)

invoices = rows(load("/tmp/tp-client-invoices.json"))
aged = rows(load("/tmp/tp-client-aged-debtors.json"))
unbilled = rows(load("/tmp/tp-client-unbilled.json"))
creditnotes = rows(load("/tmp/tp-client-creditnotes.json"))
rates = rows(load("/tmp/tp-client-rates.json"))

summary = {
    "invoiceCount": len(invoices),
    "invoiceTotalIncGst": sum(money(r.get("sellTotal")) for r in invoices),
    "invoicePaid": sum(money(r.get("paidAmt")) for r in invoices),
    "invoiceOutstanding": sum(money(r.get("osAmt")) for r in invoices),
    "agedDebtorCount": len(aged),
    "unbilledCount": len(unbilled),
    "unbilledExGst": sum(money(r.get("billableAmount") or r.get("sellTotal") or r.get("amount")) for r in unbilled),
    "creditNoteCount": len(creditnotes),
    "creditNoteTotal": sum(money(r.get("amount") or r.get("sellTotal")) for r in creditnotes),
    "rateCount": len(rates),
}

print(json.dumps(summary, indent=2))
PY
```

## Compare External Data
When the user supplies Excel, CSV, Xero MCP, or another source:

- Normalize client identity before comparing.
- Choose one date basis: invoice date, date created, payment date, or service period.
- Normalize sign convention for receipts and credit notes.
- Compare ex-GST, GST, and inc-GST separately.
- Report unmatched rows in both directions, not only amount deltas.

## Report
State:

- Client ID and date window.
- Record counts for invoices, aged debtors, unbilled rows, credit notes, rates, and recurring templates.
- Totals with GST basis.
- Whether credit notes and write-offs are netted.
- CSV or JSON artifact paths created for follow-up.
