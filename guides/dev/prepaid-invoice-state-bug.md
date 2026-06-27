# Prepaid Invoice State Bug

Use this guide when a developer needs to reproduce or verify prepaid invoice
state, drawdown, remaining credit, allocated timesheets, or write-off behavior.
Keep the invoice header, product lines, allocated rows, write-offs, prepaid
summary, and broad timesheet query separate until the mismatch is proven.

Useful evidence:

```bash
INV=142
tp invoice get "$INV" --tenant northwind --env staging --json
tp invoice lines "$INV" --tenant northwind --env staging --json
tp invoice timesheets "$INV" --tenant northwind --env staging --json
tp invoice timesheets "$INV" --tenant northwind --env staging --writeoff --json
tp prepaid summary "$INV" --tenant northwind --env staging --json
tp query --from 2026-03-01 --to 2026-03-31 --tenant northwind --env staging --client NWIND --json
```

Check invoice status, allocated totals, write-off totals, prepaid original,
drawn-down, credited, and remaining values. Avoid publishing live amounts or
client-specific balances; report shape, deltas, and redacted totals when sharing
evidence.
