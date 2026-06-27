# Saved Timesheets Wrong

Use this guide for missing, duplicate, or incorrect saved timesheet rows.

Useful evidence:

```bash
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp ts get --from 2026-03-09 --to 2026-03-13 --tenant northwind --env staging --emp-id ALEX --json
tp query --from 2026-03-01 --to 2026-03-31 --tenant northwind --env staging --emp-id ALEX --json
```

Check create/update/accept boundaries, rate enrichment, category/billable type,
and whether the UI read model matches the API output.
