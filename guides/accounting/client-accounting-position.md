# Client Accounting Position

Use this guide for one-client accounting evidence: invoices, unpaid invoices,
aged debtors, unbilled work, credit notes, rates, recurring templates, and
revenue threshold checks.

Useful evidence:

```bash
CLIENT=NWIND
tp invoice list --query "$CLIENT" --limit 500 --field DateCreated --dir desc --json
tp receipt outstanding "$CLIENT" --json
tp unbilled list --client "$CLIENT" --json
tp creditnote list --client "$CLIENT" --json
tp rate list --client "$CLIENT" --show-expired --json
```

State record counts, GST basis, date basis, and whether credit notes or
write-offs were netted.
