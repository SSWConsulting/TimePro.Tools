# External Sync Bug

Use this guide for invoice, receipt, credit note, or external reference/status
sync failures.

Useful evidence:

```bash
tp invoice get 142 --tenant northwind --env staging --json
tp invoice receipts 142 --tenant northwind --env staging --json
tp creditnote list --client NWIND --tenant northwind --env staging --json
tp receipt list --search NWIND --tenant northwind --env staging --limit 50 --json
```

Use telemetry only after CLI evidence identifies the concrete invoice, receipt,
credit note, client, or external reference.
