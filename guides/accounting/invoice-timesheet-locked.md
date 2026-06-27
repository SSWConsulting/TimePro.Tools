# Locked Invoice or Timesheet

Use this guide when a timesheet **cannot be edited or moved**, or an invoice
**cannot be updated, posted, or reallocated** — even though the underlying data
looks correct. The usual cause is a *lock*, not bad data.

Two distinct locks:

- **Timesheet locked by an invoice** — once a timesheet is allocated to an
  invoice it is locked; only its location and description can change. Trying to
  move it to another invoice or edit its hours/rate fails.
- **Invoice locked** — an invoice can be locked by posting, a closed accounting
  period, or external (Xero) sync state. A locked invoice rejects updates and
  reallocations.

Useful evidence (read-only; needs `tp feature accounting`):

```bash
tp invoice get 142 --tenant northwind --env prod --json \
  | jq '{isLocked, isRecurring, sellTotal, paidAmt, osAmt, externalSyncStatus}'
tp invoice timesheets 142 --tenant northwind --env prod --json            # rows allocated to (and thus locked by) the invoice
tp invoice timesheets 142 --tenant northwind --env prod --writeoff --json # written-off rows
tp creditnote list --client NWIND --tenant northwind --env prod --json    # a credit note may be the only way to adjust a locked invoice
```

Check, in order:

- **Is the invoice `isLocked`?** Posted / period-locked / sync-locked invoices
  cannot be edited — the fix is to reverse/unlock the invoice or credit it, not
  to edit the row.
- **Is the timesheet locked by an invoice?** If it appears in `tp invoice
  timesheets`, it is allocated and locked — to move it you must first take it off
  that invoice (or write it off).
- **`externalSyncStatus`** — a row mid-sync to Xero can present as locked; see
  the `external-sync-bug` dev guide / `external-comparison` accounting guide.

Resolving a lock is a **write**: unlocking a period, reversing/crediting an
invoice, or reallocating timesheets all mutate financial data. On production,
gather the evidence read-only and get explicit permission before any change.

> Developers hit this too — a locked timesheet only allows location/description
> edits. It is also reachable from `tp dev guide` via `saved-timesheets-wrong`.
