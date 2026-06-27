# TimePro Developer Diagnostics

Use this skill to reproduce bugs, gather evidence, verify fixes, and decide when to move from CLI evidence to App Insights.

## Environment safety
- Local and staging can be more experimental when the task requires it: setup changes, refreshes, and smoke writes are acceptable if scoped and reversible.
- Production defaults to read-only. Ask the user before any non-read-only production action.
- Non-read-only production actions include create, update, delete, accept, refresh, persist, cancel, login/token changes, and changing the active production tenant profile.
- `tp ts suggest` refreshes suggested timesheets before reading them. Treat it as non-read-only in production and ask first.
- Do not read tenant config files or secret stores directly. Use `tp tenant list` and `tp tenant info`.
- Prefer `--tenant <name> --env <env>` on each command so diagnostics do not mutate the active tenant.

## Select the target
Start with the developer guide when the request is a bug report rather than a
specific command failure:

```bash
tp dev guide --use-case "suggested timesheets missing for ALEX on 2026-03-12" --json
```

```bash
tp tenant info --tenant northwind --env staging --json
tp tenant info --tenant northwind --env prod --json
tp user me --tenant northwind --env staging --json
```

Use the default `timepro-tenant-setup` skill when the task is only to switch profiles or set up tenant/environment configs.

Use the narrower developer skills when the bug matches them:

- `timepro-dev-timesheet-diagnostics` for suggested timesheets, CRM bookings, saved timesheets, accept/create/update flows, and missing or duplicate time rows.
- `timepro-dev-finance-diagnostics` for invoices, credit notes, receipts, client rates, prepaid drawdown, tax, billing status, and external accounting sync bugs.

## Reproduce a user/date bug
Capture exact tenant, environment, EmpID, date, and command output. Use exact `yyyy-MM-dd` dates.

```bash
# Resolve identity
tp user list "Alex" --tenant northwind --env staging --all --limit 10 --json
tp user get ALEX --tenant northwind --env staging --json

# Timesheets and persisted suggestions for that employee/date
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp ts get --from 2026-03-09 --to 2026-03-13 --tenant northwind --env staging --emp-id ALEX --json

# Suggested timesheet refresh for the selected tenant-profile employee
tp ts suggest 2026-03-12 --tenant northwind --env staging --json

# CRM bookings for the selected tenant-profile employee
tp bk list --date 2026-03-12 --tenant northwind --env staging --json
tp bk list --week 0 --tenant northwind --env staging --json

# Wider context
tp leave list --tenant northwind --env staging --emp-id ALEX --filter UPCOMING --limit 10 --json
tp query --from 2026-03-01 --to 2026-03-31 --tenant northwind --env staging --emp-id ALEX --json
```

Current limitation: `tp ts suggest` and `tp bk list` use the employee stored in the selected tenant profile and do not accept `--emp-id`. If the bug is about another employee, use a profile for that employee or record the CLI gap.

## Verify a fix
Verify at the lowest accessible environment that proves the change, then repeat the exact read-only evidence in staging or production if needed.

```bash
# Staging verification through the installed tool
tp tenant info --tenant northwind --env staging --json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp bk list --date 2026-03-12 --tenant northwind --env staging --json
```

For production verification, stay read-only unless the user explicitly approves a non-read-only check and you have stated the exact command first.

## App Insights follow-up
Use telemetry after the CLI proves the request shape or identifies a CLI gap. Keep queries tight by time window, EmpID, endpoint, and operation.

```bash
az monitor app-insights query \
  --app <APP_INSIGHTS_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --analytics-query "requests | where timestamp between (datetime(2026-03-12T00:00:00Z) .. datetime(2026-03-13T00:00:00Z)) | where url has 'Timesheet' or url has 'Booking' | project timestamp, name, url, resultCode, success, duration, operation_Id | order by timestamp desc | take 50"

az monitor app-insights query \
  --app <APP_INSIGHTS_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --analytics-query "traces | where timestamp between (datetime(2026-03-12T00:00:00Z) .. datetime(2026-03-13T00:00:00Z)) | where message has 'ALEX' or tostring(customDimensions) has 'ALEX' | project timestamp, severityLevel, message, customDimensions, operation_Id | order by timestamp desc | take 100"

az monitor app-insights query \
  --app <APP_INSIGHTS_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --analytics-query "exceptions | where timestamp between (datetime(2026-03-12T00:00:00Z) .. datetime(2026-03-13T00:00:00Z)) | where tostring(customDimensions) has 'ALEX' or operation_Name has 'Timesheet' | project timestamp, type, problemId, outerMessage, customDimensions, operation_Id | order by timestamp desc | take 50"
```

## Report format
1. Target tenant/environment and whether commands were read-only.
2. Reproduction command and observed output shape.
3. User/date identity proof.
4. Timesheet, suggested-timesheet, CRM booking, leave, and query evidence.
5. App Insights evidence, if used.
6. Fix verification or next smallest missing diagnostic.
