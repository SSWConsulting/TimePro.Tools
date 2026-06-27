# External Comparison

Use this guide when comparing TimePro data with Excel, CSV, another MCP, or an
accounting export.

Normalize before comparing:

- identity/reference fields
- date basis and time zone
- ex-GST, GST, and inc-GST amounts
- receipt and credit-note sign convention
- rounding/materiality threshold
- unmatched rows in both directions

Useful evidence:

```bash
tp invoice list --limit 500 --field DateCreated --dir desc --json
tp receipt list --limit 500 --field PaymentDate --dir desc --json
tp creditnote list --client NWIND --json
```
