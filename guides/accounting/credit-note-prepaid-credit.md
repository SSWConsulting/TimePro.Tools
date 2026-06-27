# Credit Note Not Reducing Prepaid Balance

Use this guide when a credit note was issued for a prepaid client but the
prepaid **Credited** bucket (in `tp prepaid summary`) did not move.

The prepaid summary only counts a credit note toward `credited` when **both** are
true:

- `isCreditingInvoice == true` (it credits a specific invoice, not a standalone
  credit), and
- `associatedInvoiceId` equals the **prepaid invoice** you are looking at.

A standalone credit note, or one that credits a *different* invoice, will not
change this invoice's prepaid balance — by design.

Useful evidence (read-only; needs `tp feature accounting`):

```bash
tp creditnote list --client NWIND --tenant northwind --env prod --json \
  | jq '.[] | {id, total, isCreditingInvoice, associatedInvoiceId}'
tp prepaid summary 142 --tenant northwind --env prod --json \
  | jq '{creditingCreditNoteCount, credited}'
```

Check:

- **`isCreditingInvoice`** — if `false`, it is a standalone credit and won't
  reduce any invoice's prepaid balance.
- **`associatedInvoiceId`** — must equal the prepaid invoice (`142` here). A
  credit note pointed at another invoice reduces *that* invoice instead.
- **`creditingCreditNoteCount`** — confirms how many credit notes the summary
  actually counted; if it is lower than you expect, one of the above is the
  reason.

If the credit note targets the right invoice but the prepaid total still looks
wrong, move on to the `prepaid-reconciliation-delta` guide.
