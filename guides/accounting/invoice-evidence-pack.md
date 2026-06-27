# Invoice Evidence Pack

Use this guide to assemble a read-only invoice reconciliation pack. Keep the
invoice header, product lines, allocated timesheets, write-offs, receipts, and
credit notes separate until the user confirms how totals should be netted.

Useful evidence:

```bash
INV=142
tp invoice get "$INV" --json
tp invoice lines "$INV" --json
tp invoice timesheets "$INV" --json
tp invoice timesheets "$INV" --writeoff --json
tp invoice receipts "$INV" --json
```

Check header subtotal, GST, total, paid amount, outstanding amount, line totals,
allocated timesheet totals, write-off totals, and receipt sign convention.
