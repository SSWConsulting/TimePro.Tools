# Prepaid Reconciliation Delta

Use this guide when `tp prepaid summary` reports a non-zero **reconciliation
delta** — the warning that `original − drawnDown − credited ≠ remaining`. The
delta should always be zero; a non-zero value means a drawdown, write-off, or
credit is missing or double-counted.

Useful evidence (read-only; needs `tp feature accounting`):

```bash
tp prepaid summary 142 --tenant northwind --env prod --json \
  | jq '{original, drawnDown, credited, remaining, reconciliationDeltaExGst}'
tp invoice timesheets 142 --tenant northwind --env prod --json            # timesheets drawn down against the prepaid invoice
tp invoice timesheets 142 --tenant northwind --env prod --writeoff --json # write-offs that change the drawn-down total
tp creditnote list --client NWIND --tenant northwind --env prod --json    # credits that should reduce the balance
```

Check:

- **Sum the parts.** `drawnDown` should equal the allocated timesheet value;
  `credited` should equal the crediting credit notes. Recompute and find which
  bucket is off.
- **Write-offs** — a written-off row still affects drawdown; confirm `--writeoff`
  rows are accounted for.
- **Credits** — only credit notes with `isCreditingInvoice == true` and
  `associatedInvoiceId` equal to this invoice count toward `credited`. If a
  credit note exists but the delta persists, see the
  `credit-note-prepaid-credit` guide.

The delta is a *defined error condition* (it is asserted to be zero in tests), so
treat any non-zero value as a real discrepancy to chase down, not a rounding
artefact.
