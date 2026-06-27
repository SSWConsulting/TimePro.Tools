# Leave Profile Field Issue

Use this guide when leave, coverage, or suggested-timesheet behavior looks tied
to employee profile data. This is a CLI-only diagnostic path for missing or
unexpected fields such as timezone, site, work hours, lunch, or time-less
settings.

Useful evidence:

```bash
tp user get ALEX --tenant northwind --env staging --json
tp leave list --filter UPCOMING --tenant northwind --env staging --emp-id ALEX --json
tp leave list --filter PAST --tenant northwind --env staging --emp-id ALEX --json
tp ts check --week 0 --tenant northwind --env staging --emp-id ALEX --json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
```

Check the profile fields before blaming leave logic. If a value is missing,
compare local/staging/prod only with read-only commands unless the user has
approved a non-read-only production repair.
