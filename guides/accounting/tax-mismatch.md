# Tax Mismatch

Use this guide for non-zero timesheets that appear to have 0% tax while the
client invoice has non-zero tax. Keep the work read-only and produce evidence
that can be compared with Excel, CSV, another MCP, or an accounting export.

Start narrow with one invoice. Confirm invoice tax, allocated timesheets, and
whether 0% tax is legitimate for the client, category, billable type, or invoice.

Useful evidence:

```bash
INV=142
tp invoice get "$INV" --json
tp invoice lines "$INV" --json
tp invoice timesheets "$INV" --json
tp invoice timesheets "$INV" --writeoff --json
```

Report the invoice IDs checked, row count, tax basis, and whether write-offs
were included.
