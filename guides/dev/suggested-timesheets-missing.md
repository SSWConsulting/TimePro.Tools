# Suggested Timesheets Missing

Use this guide when expected suggestions do not appear for an employee/date, or
when "Update suggested timesheet" reports *no suggestions available for today*.

**Suggestions are derived from CRM bookings.** A suggested timesheet for a given
day is built from that day's CRM appointments — not from the previous day's saved
timesheet. So if there is **no CRM booking for the date, "no suggestions
available" is expected behaviour, not a bug.** Before suspecting the suggestion
logic (or a code change you are testing), confirm a booking even exists for that
exact date — this is the most common cause of a false "it's broken" report.

First prove the selected tenant profile employee because `tp ts suggest` uses
the employee on the selected tenant profile.

Useful evidence (run the booking check first):

```bash
tp tenant info --tenant northwind --env staging --json
tp user me --tenant northwind --env staging --json
tp bk list --date 2026-03-12 --tenant northwind --env staging --json   # no rows -> no suggestion is expected
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp ts suggest 2026-03-12 --tenant northwind --env staging --json
```

Check, in order: **CRM booking exists for the date** (no booking -> no
suggestion, by design), tenant-profile employee mismatch, leave/holiday coverage,
saved rows hiding suggestions, duplicate prevention, and refresh persistence.

If there are no bookings, the fix is data, not code: add the CRM appointment for
that day (or pick a date that has one), then re-run `tp ts suggest`. See the
`crm-bookings-missing` guide to diagnose why a booking is absent.
