# Employee Scoped Diagnostics

Use this guide when a bug depends on the selected employee, date range, or
profile mapping. Resolve the `empId` first, then compare every command against
the same tenant, environment, employee, and date window.

Useful evidence:

```bash
tp user list "Alex" --tenant northwind --env staging --json
tp user get ALEX --tenant northwind --env staging --json
tp ts get --from 2026-03-09 --to 2026-03-13 --tenant northwind --env staging --emp-id ALEX --json
tp leave list --filter UPCOMING --tenant northwind --env staging --emp-id ALEX --json
tp leave list --filter PAST --tenant northwind --env staging --emp-id ALEX --json
tp query --from 2026-03-09 --to 2026-03-13 --tenant northwind --env staging --emp-id ALEX --json
```

Check that the employee exists in the target environment, is enabled for the
date being diagnosed, has expected profile fields, and returns the same rows
when queried through direct timesheet and broader query paths.
