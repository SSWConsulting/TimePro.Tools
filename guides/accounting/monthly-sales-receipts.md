# Monthly Sales And Receipts

Use this guide for a quick read-only monthly snapshot of invoiced sales and
cash received. Keep invoiced sales and receipts separate because they answer
different accounting questions.

Monthly invoiced sales:

```bash
MONTH=2026-03

tp invoice list --limit 500 --field DateCreated --dir desc --json \
  | jq --arg month "$MONTH" '
      [.data[] | select(.dateCreated | startswith($month))]
      | {
          month: $month,
          invoiceCount: length,
          invoicedIncGst: (map(.sellTotal) | add // 0)
        }'
```

Monthly receipts, or money in:

```bash
MONTH=2026-03

tp receipt list --limit 500 --field PaymentDate --dir desc --json \
  | jq --arg month "$MONTH" '
      [.data[] | select(.paymentDate | startswith($month))]
      | {
          month: $month,
          receiptCount: length,
          receivedIncGst: (map(.paidTotal // .paid) | add // 0 | fabs)
        }'
```

If the target month may exceed one page, page with `--skip` and keep the
column basis stable before pushing the data into CSV or a spreadsheet.
