# TimePro Developer Finance Diagnostics

Use this skill when a bug is about invoices, credit notes, receipts, client rates, prepaid drawdown, tax, billing status, or external accounting sync. This overlaps with accounting scenarios, but the developer goal is different: find the faulty code/data boundary and verify a fix, not produce a final accounting reconciliation.

## Safety boundary
- Local and staging can be more experimental when scoped and reversible.
- Production defaults to read-only. Ask the user before any non-read-only production action.
- Do not resync, edit, lock/unlock, create, update, delete, write off, or mutate external accounting state in production without explicit user approval.
- Prefer `--tenant <name> --env <env>` on each command so diagnostics do not mutate the active tenant.
- If an external MCP is available, use it for comparison only after normalizing invoice id/reference, client id, date basis, GST convention, amount sign, and currency.

## Triage questions
Ask for the smallest anchor first:

1. Invoice ID, credit note ID, receipt ID, client ID, EmpID, date range, or external sync reference.
2. Environment and tenant profile.
3. Whether the bug is calculation, missing data, wrong status, sync failure, UI display, or CLI/API shape.
4. Whether expected amounts are ex-GST, GST, or inc-GST.
5. Whether credit notes, write-offs, prepaid drawdown, and receipts should be netted or reported separately.

## Invoice and timesheet tax bugs
For a scenario like non-zero timesheets with 0% tax on an invoice that has non-zero tax, gather invoice and line evidence first:

```bash
tp invoice list --query NWIND --tenant northwind --env staging --limit 20 --json
tp invoice get 142 --tenant northwind --env staging --json
tp invoice lines 142 --tenant northwind --env staging --json
tp invoice timesheets 142 --tenant northwind --env staging --json
tp invoice timesheets 142 --tenant northwind --env staging --writeoff --json
tp invoice receipts 142 --tenant northwind --env staging --json
```

Then inspect the client/rate/tax inputs used by saved timesheets:

```bash
tp rate get --client NWIND --tenant northwind --env staging --date 2026-03-12 --json
tp rate list --client NWIND --tenant northwind --env staging --emp-id ALEX --show-expired --json
tp query --from 2026-03-01 --to 2026-03-31 --tenant northwind --env staging --emp-id ALEX --json
```

Bug-focused checks:

- Is invoice tax non-zero while one or more allocated non-zero timesheets have zero tax?
- Did a CLI-created or updated timesheet omit tax and rely on defaulting?
- Was an explicit 0% tax legitimate for a GST-free client or billable type?
- Did rate lookup use the active rate for the timesheet date, not a future or expired rate?
- Do invoice header totals agree with line items and allocated timesheets?

For a deeper read-only evidence pack, use the CLI diagnostics:

```bash
tp accounting tax-mismatches --query NWIND --tenant northwind --env staging --json
tp accounting invoice-diagnostics 142 --tenant northwind --env staging --json
```

## Client rate and billing status bugs
Rates are date-sensitive. Capture current, expired, and recommended views before concluding the calculation is wrong.

```bash
tp rate get --client NWIND --tenant northwind --env staging --date 2026-03-12 --json
tp rate recommend --client NWIND --tenant northwind --env staging --json
tp rate list --client NWIND --tenant northwind --env staging --emp-id ALEX --show-expired --json
tp unbilled list --client NWIND --tenant northwind --env staging --page-size 50 --json
tp client outstanding --tenant northwind --env staging --json
```

Bug-focused checks:

- Wrong employee-specific rate versus client default.
- Expired rate selected when a current rate exists.
- Future rate selected too early.
- Prepaid rate used for standard billable work, or standard rate used for prepaid work.
- Saved timesheet sell price differs from the rate returned for the same client/date.

## Credit note, receipt, and prepaid bugs
Use read-only commands to identify association and sign-convention issues:

```bash
tp creditnote list --client NWIND --tenant northwind --env staging --json
tp receipt list --search NWIND --tenant northwind --env staging --limit 50 --json
tp receipt get 242 --tenant northwind --env staging --json
tp prepaid summary 142 --tenant northwind --env staging --json
```

Bug-focused checks:

- Credit note is associated with the wrong invoice or not associated at all.
- Locked/associated credit note cannot be edited but the UI/API allowed an attempted update.
- Receipt amounts use a negative incoming-payment convention while the comparison source uses positive amounts.
- Prepaid drawdown remaining balance disagrees with invoice timesheets or credit notes.
- External sync status/reference is present on one artifact but not the related invoice, credit note, or receipt.

## External sync and telemetry
Only go to App Insights after CLI evidence identifies the invoice, credit note, receipt, client, or EmpID. Search by operation, id, and sync category names.

```bash
az monitor app-insights query \
  --app <APP_INSIGHTS_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --analytics-query "traces | where timestamp > ago(7d) | where message has 'Xero' or tostring(customDimensions) has 'Xero' or tostring(customDimensions) has '142' | project timestamp, severityLevel, message, customDimensions, operation_Id | order by timestamp desc | take 100"

az monitor app-insights query \
  --app <APP_INSIGHTS_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --analytics-query "exceptions | where timestamp > ago(7d) | where outerMessage has 'Xero' or tostring(customDimensions) has 'Xero' or tostring(customDimensions) has '142' | project timestamp, type, problemId, outerMessage, customDimensions, operation_Id | order by timestamp desc | take 80"

az monitor app-insights query \
  --app <APP_INSIGHTS_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --analytics-query "requests | where timestamp > ago(7d) | where url has 'ClientInvoice' or url has 'CreditNote' or url has 'Receipting' or url has 'Timesheets' | project timestamp, name, url, resultCode, success, duration, operation_Id | order by timestamp desc | take 100"
```

With a Xero MCP or another external MCP, ask it for the closest matching invoice/payment/credit note fields rather than assuming TimePro names. Compare mapped fields deliberately: reference/id, client, date basis, ex-GST/inc-GST, tax amount, paid/outstanding amount, status, and external sync id.

## Verify the fix
Verify the changed boundary, then rerun the exact diagnostic commands:

```bash
tp invoice get 142 --tenant northwind --env staging --json
tp invoice timesheets 142 --tenant northwind --env staging --json
tp rate get --client NWIND --tenant northwind --env staging --date 2026-03-12 --json
tp accounting invoice-diagnostics 142 --tenant northwind --env staging --json
```

Report whether the issue is data, CLI request shaping, API calculation, read-model mapping, external sync, or environment configuration. Include exact commands and keep accounting conclusions separate from bug evidence.
