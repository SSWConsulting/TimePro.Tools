# Invoice Tax Bug

Use this guide when the bug is about invoice tax, timesheet tax, or 0% tax rows
on a taxable invoice.

Useful evidence:

```bash
tp invoice get 142 --tenant northwind --env staging --json
tp invoice lines 142 --tenant northwind --env staging --json
tp invoice timesheets 142 --tenant northwind --env staging --json
tp rate list --client NWIND --tenant northwind --env staging --emp-id ALEX --show-expired --json
```

Separate data issues from code boundaries: saved timesheet tax, invoice line tax,
rate lookup date, API mapping, UI read model, and external sync state.
