# Clients With 50k Invoiced Revenue

Use this guide when the question is "which clients had at least $50k invoiced
revenue in the last 12 months?" For invoice-backed revenue, qualify by invoiced
totals instead of billable work.

Use `--threshold 0` so the JSON includes all clients with billable work, then
filter by revenue:

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

Report the CSV path, row count, threshold, date range, and whether the total is
ex-GST or inc-GST. If product-only invoices are in scope, page
`tp invoice list` and group by `clientId` instead of relying on the
billable-work report.
