# Receipt Allocated to the Wrong Invoice

Use this guide when a client has paid but an invoice still shows an outstanding
balance, or a single payment looks split across invoices incorrectly. A receipt
carries **allocations** (one receipt can be applied across many invoices), so a
mis-allocation leaves one invoice short while another is over-paid.

Useful evidence (read-only; needs `tp feature accounting`):

```bash
tp receipt get 108 --tenant northwind --env prod --json | jq '.allocations'   # where this payment actually landed
tp invoice receipts 142 --tenant northwind --env prod --json                  # receipts applied to the suspect invoice
tp invoice get 142 --tenant northwind --env prod --json | jq '{sellTotal, paidAmt, osAmt}'
tp receipt outstanding NWIND --tenant northwind --env prod --json             # aged view across the client's invoices
```

Check:

- **Receipt allocations vs invoice.** Do the receipt's `allocations` add up to
  the receipt total, and is the suspect invoice in the list with the expected
  amount?
- **Under-allocation.** If the invoice's `osAmt` is non-zero but the client has
  paid, the receipt was likely applied to a different invoice — compare against
  `tp receipt outstanding NWIND`.
- **Split correctness.** For a payment spread across invoices, confirm each
  allocation matches the intended invoice; a wrong split shows up as one invoice
  over-paid and another still outstanding.

Re-allocating a receipt is a **write**. On production, confirm the mis-allocation
read-only first, then get permission before moving the payment.
