# CRM Bookings Missing

Use this guide when TimePro reads work but CRM appointment/bookings input is
empty or inconsistent.

Useful evidence:

```bash
tp tenant info --tenant northwind --env staging --json
tp user get ALEX --tenant northwind --env staging --json
tp bk list --date 2026-03-12 --tenant northwind --env staging --json
tp bk list --week 0 --tenant northwind --env staging --json
```

Check employee mapping, appointment owner/attendee expectations, date-window
boundaries, and whether the issue reproduces in another environment.
