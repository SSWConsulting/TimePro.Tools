# TimePro Developer Timesheet Diagnostics

Use this skill when a bug is about suggested timesheets, CRM bookings, saved timesheets, missing rows, duplicate rows, or an accept/create/update flow.

This is a developer diagnostic skill. The goal is to identify the smallest failing boundary: tenant profile, employee identity, CRM booking source, suggestion refresh, saved timesheet read model, rate/tax enrichment, or persistence.

## Safety boundary
- Local and staging can be more experimental when scoped and reversible.
- Production defaults to read-only. Ask the user before create, update, delete, accept, refresh, cancel, login/token changes, or tenant-profile mutation.
- `tp ts suggest` refreshes and persists suggested-timesheet state before reading it. Treat it as non-read-only in production.
- `tp ts accept`, `tp ts create`, `tp ts update`, and `tp ts delete` are writes in every environment.
- Prefer `--tenant <name> --env <env>` on each command so diagnostics do not mutate the active tenant.

## Triage questions
Ask for these before running broad checks:

1. Tenant profile and environment.
2. EmpID, date, and timezone/date expectation.
3. Whether the expected row should come from CRM bookings, existing timesheets, copied entries, manual create, or suggested-timesheet generation.
4. Whether staging/local writes are allowed.
5. What proves the bug or fix: CLI JSON, UI state, App Insights, or environment diff.

For an initial evidence plan, ask the developer guide first:

```bash
tp dev guide --use-case "suggested timesheets missing for ALEX on 2026-03-12" --json
```

## Suggested timesheets missing
First prove the selected tenant-profile employee. `tp ts suggest` currently uses the employee stored in the tenant profile; it does not accept `--emp-id`.

```bash
tp tenant info --tenant northwind --env staging --json
tp user me --tenant northwind --env staging --json
tp user list "Alex" --tenant northwind --env staging --all --limit 10 --json
tp user get ALEX --tenant northwind --env staging --json
```

Compare bookings, saved timesheets, leave, and generated suggestions for the exact date:

```bash
tp bk list --date 2026-03-12 --tenant northwind --env staging --json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp leave list --tenant northwind --env staging --emp-id ALEX --filter UPCOMING --limit 20 --json
tp ts suggest 2026-03-12 --tenant northwind --env staging --json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
```

Common bug boundaries:

- Wrong tenant profile employee: `tp user me` differs from the EmpID being investigated.
- CRM employee mismatch: TimePro employee exists, but the CRM user/appointment mapping does not.
- CRM connection/config failure: bookings fail or return empty while TimePro reads still work.
- Appointment ownership/attendee mismatch: appointment exists for another participant or owner but not the selected employee.
- Date boundary issue: appointment start/end crosses midnight or the query window is off by one day.
- Suggestion hidden by saved timesheet, leave, public holiday, weekend, or duplicate prevention.
- Refresh succeeded but no suggested rows persisted, which points at suggestion-generation logic rather than display.

## CRM bookings missing
Bookings are the closest CLI proxy for CRM appointment input.

```bash
tp bk list --date 2026-03-12 --tenant northwind --env staging --json
tp bk list --week 0 --tenant northwind --env staging --json
tp ts suggest 2026-03-12 --tenant northwind --env staging --json
```

If bookings are empty but the user expects CRM appointments, capture:

- tenant/environment
- current tenant-profile EmpID
- exact date range queried
- CLI response shape and status code if an error envelope is returned
- whether the same user has bookings in another environment

Use `timepro-env-compare` when the same EmpID/date behaves differently across environments.

## Saved timesheets wrong
Use read-only evidence before writing or accepting anything:

```bash
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp ts get --from 2026-03-09 --to 2026-03-13 --tenant northwind --env staging --emp-id ALEX --json
tp query --from 2026-03-01 --to 2026-03-31 --tenant northwind --env staging --emp-id ALEX --json
tp rate get --client NWIND --tenant northwind --env staging --date 2026-03-12 --json
tp rate list --client NWIND --tenant northwind --env staging --emp-id ALEX --show-expired --json
```

For create/update/accept bugs, capture the request intent and the post-write read model:

```bash
# Only in local/staging, or in production after explicit user approval
tp ts accept 142 --tenant northwind --env staging --location SSW --yes --json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
```

Bug-focused checks:

- Did the row save under the expected EmpID, client, project, category, billable type, location, and date?
- Was the sell price/rate calculated from the expected client rate for that date?
- Did tax default from the client rate when the request did not explicitly set tax?
- Is the row locked, invoiced, written off, or otherwise blocked from update/delete?
- Did `ts get` and `query` disagree, which suggests a read-model or endpoint difference?

## App Insights follow-up
Use telemetry after CLI evidence narrows the boundary. Keep the time window tight and include EmpID/date/endpoint terms.

```bash
az monitor app-insights query \
  --app <APP_INSIGHTS_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --analytics-query "requests | where timestamp between (datetime(2026-03-12T00:00:00Z) .. datetime(2026-03-13T00:00:00Z)) | where url has 'Timesheet' or url has 'Appointments' or url has 'Booking' or url has 'Crm' | project timestamp, name, url, resultCode, success, duration, operation_Id | order by timestamp desc | take 80"

az monitor app-insights query \
  --app <APP_INSIGHTS_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --analytics-query "traces | where timestamp between (datetime(2026-03-12T00:00:00Z) .. datetime(2026-03-13T00:00:00Z)) | where message has 'ALEX' or tostring(customDimensions) has 'ALEX' or message has 'appointment' or message has 'suggest' | project timestamp, severityLevel, message, customDimensions, operation_Id | order by timestamp desc | take 100"

az monitor app-insights query \
  --app <APP_INSIGHTS_NAME> \
  --resource-group <RESOURCE_GROUP> \
  --analytics-query "exceptions | where timestamp between (datetime(2026-03-12T00:00:00Z) .. datetime(2026-03-13T00:00:00Z)) | where operation_Name has 'Timesheet' or operation_Name has 'Appointment' or tostring(customDimensions) has 'ALEX' | project timestamp, type, problemId, outerMessage, customDimensions, operation_Id | order by timestamp desc | take 50"
```

## Verify the fix
Prefer the smallest verification that proves the changed boundary:

```bash
tp tenant info --tenant northwind --env staging --json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp bk list --date 2026-03-12 --tenant northwind --env staging --json
tp ts suggest 2026-03-12 --tenant northwind --env staging --json
```

Report tenant/environment, read-only/write status, exact commands, observed JSON shape, suspected failing boundary, telemetry evidence if used, and the smallest next fix or verification step.
