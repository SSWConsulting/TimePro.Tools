# Suggested Timesheets Missing

Use this guide when expected suggestions do not appear for an employee/date.
First prove the selected tenant profile employee because `tp ts suggest` uses
the employee on the selected tenant profile.

Useful evidence:

```bash
tp tenant info --tenant northwind --env staging --json
tp user me --tenant northwind --env staging --json
tp bk list --date 2026-03-12 --tenant northwind --env staging --json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp ts suggest 2026-03-12 --tenant northwind --env staging --json
```

Check tenant-profile employee mismatch, missing CRM bookings, leave/holiday
coverage, saved rows hiding suggestions, duplicate prevention, and refresh
persistence.
