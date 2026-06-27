# Recurring Invoice Not Generating

Use this guide when a client has not received a scheduled invoice, or a recurring
invoice template appears stalled. The template's `isActive`, `lastInvEndDate`,
and `nextInvoiceDate` tell the story.

Useful evidence (read-only; needs `tp feature accounting`):

```bash
tp recurring list --client NWIND --tenant northwind --env prod --outdated --json   # templates overdue to generate
tp recurring get 7 --tenant northwind --env prod --json \
  | jq '{isActive, lastInvEndDate, nextInvoiceDate}'
tp invoice list --query NWIND --recurring --tenant northwind --env prod --json     # invoices actually generated from recurring templates
```

Check:

- **`isActive`** — an inactive template will never generate; that may be
  intentional (paused) or an accidental deactivation.
- **`nextInvoiceDate` in the past** with no matching generated invoice → the
  scheduler did not run or the template is stuck; confirm by listing recurring
  invoices with `tp invoice list --recurring`.
- **`lastInvEndDate`** — compare to the expected cadence; a gap between
  `lastInvEndDate` and `nextInvoiceDate` larger than the cycle suggests a missed
  run.

If `--outdated` returns the template but invoices still aren't appearing, the
generation job (not the template data) is the next thing to investigate.
