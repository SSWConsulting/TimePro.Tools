# Client Rate And Billing Status Bug

Use this guide for rate lookup, unbilled work, prepaid versus standard billing,
or outstanding status issues.

Useful evidence:

```bash
tp rate get --client NWIND --tenant northwind --env staging --date 2026-03-12 --json
tp rate list --client NWIND --tenant northwind --env staging --emp-id ALEX --show-expired --json
tp unbilled list --client NWIND --tenant northwind --env staging --json
tp client outstanding --tenant northwind --env staging --json
```

Check employee-specific rates, expired/future rates, prepaid flags, and whether
the saved timesheet amount was calculated before or after a rate change.
