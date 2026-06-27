# TimePro Environment Compare

Use this skill to compare TimePro environments for consistency before or after a fix, migration, configuration change, or data investigation.

## Safety boundary
- Compare environments with read-only commands by default.
- Local and staging can be more experimental when the task calls for setup or smoke writes.
- Production defaults to read-only. Ask the user before any non-read-only production action.
- Do not run `tp ts suggest` against production without permission because it refreshes and persists suggested-timesheet state.
- Do not inspect tenant config files directly; use `tp tenant list` and `tp tenant info`.

## Compare tenant profiles
Start by proving which profiles the commands resolve to:

```bash
tp tenant info --tenant northwind --env prod --json > /tmp/tp-prod-tenant.json
tp tenant info --tenant northwind --env staging --json > /tmp/tp-staging-tenant.json
jq -S . /tmp/tp-prod-tenant.json > /tmp/tp-prod-tenant.sorted.json
jq -S . /tmp/tp-staging-tenant.json > /tmp/tp-staging-tenant.sorted.json
diff -u /tmp/tp-prod-tenant.sorted.json /tmp/tp-staging-tenant.sorted.json
```

Expected differences normally include API URL and environment name. Unexpected differences include employee profile, tenant id, or a missing profile.

## Compare a user/date scenario
Use the same EmpID and date in every environment. Normalize JSON before diffing so field order does not create noise.

```bash
# User identity
tp user get ALEX --tenant northwind --env prod --json > /tmp/tp-prod-user.json
tp user get ALEX --tenant northwind --env staging --json > /tmp/tp-staging-user.json
jq -S . /tmp/tp-prod-user.json > /tmp/tp-prod-user.sorted.json
jq -S . /tmp/tp-staging-user.json > /tmp/tp-staging-user.sorted.json
diff -u /tmp/tp-prod-user.sorted.json /tmp/tp-staging-user.sorted.json

# Timesheets and persisted suggestions
tp ts get 2026-03-12 --tenant northwind --env prod --emp-id ALEX --json > /tmp/tp-prod-timesheets.json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json > /tmp/tp-staging-timesheets.json
jq -S . /tmp/tp-prod-timesheets.json > /tmp/tp-prod-timesheets.sorted.json
jq -S . /tmp/tp-staging-timesheets.json > /tmp/tp-staging-timesheets.sorted.json
diff -u /tmp/tp-prod-timesheets.sorted.json /tmp/tp-staging-timesheets.sorted.json

# Historical context
tp query --from 2026-03-01 --to 2026-03-31 --tenant northwind --env prod --emp-id ALEX --json > /tmp/tp-prod-query.json
tp query --from 2026-03-01 --to 2026-03-31 --tenant northwind --env staging --emp-id ALEX --json > /tmp/tp-staging-query.json
```

## Compare bookings and generated suggestions
CRM bookings are read-only but use the selected tenant-profile employee. Suggested timesheet refresh uses the selected tenant-profile employee too, and is not a read-only production command.

```bash
# CRM bookings for the selected tenant-profile employee
tp bk list --date 2026-03-12 --tenant northwind --env prod --json > /tmp/tp-prod-bookings.json
tp bk list --date 2026-03-12 --tenant northwind --env staging --json > /tmp/tp-staging-bookings.json

# Suggested refresh is acceptable in staging/local when scoped
tp ts suggest 2026-03-12 --tenant northwind --env staging --json > /tmp/tp-staging-suggestions.json

# Production suggested refresh requires explicit user permission first
# tp ts suggest 2026-03-12 --tenant northwind --env prod --json > /tmp/tp-prod-suggestions.json
```

## Compare reference data
Only compare the reference data needed by the bug. Avoid broad dumps unless the user asked for an environment audit.

```bash
tp leave list --tenant northwind --env prod --emp-id ALEX --filter UPCOMING --limit 10 --json > /tmp/tp-prod-leave.json
tp leave list --tenant northwind --env staging --emp-id ALEX --filter UPCOMING --limit 10 --json > /tmp/tp-staging-leave.json
tp rate list --tenant northwind --env prod --client NWIND --show-expired --json > /tmp/tp-prod-rates.json
tp rate list --tenant northwind --env staging --client NWIND --show-expired --json > /tmp/tp-staging-rates.json
tp loc info --tenant northwind --env prod --json > /tmp/tp-prod-location.json
tp loc info --tenant northwind --env staging --json > /tmp/tp-staging-location.json
```

## Report format
1. Target environments and tenant profiles.
2. Commands run, with any production commands marked read-only or approved.
3. Expected differences.
4. Unexpected differences and likely owner: config, data, code, or CLI capability.
5. Smallest next verification step.
