# TimePro Tenant Setup

Use this skill to move between configured TimePro tenant profiles safely.

## Rules
- Tenant profiles live in the TimePro CLI config directory, not the current repo.
- Do not use `direnv exec .` just to access tenant config.
- Never print, cat, copy, or commit API tokens or tenant config file contents.
- Prefer `tp tenant info` and `tp tenant list` for safe verification.
- Prefer per-command `--tenant` / `--env` overrides when a diagnostic should not change the active tenant.

## Existing profiles
```bash
tp tenant list
tp tenant info
tp user me --json
```

## Switch active tenant to SSW staging
Use this when the whole shell session should operate against SSW staging. It changes the global active tenant pointer.

```bash
# If the profile already exists
tp tenant set ssw-staging
tp tenant info
tp user me --json

# Smoke-read the target environment
tp ts get --week --json
tp bk list --week --json

# Restore when done, if needed
tp tenant set <previous-tenant>
```

If `ssw-staging` is missing, add it interactively:

```bash
tp login --tenant ssw-staging --api-url https://api.staging-sswtimepro.com
tp tenant info
```

## Use a specific tenant and environment without switching
Use this for one-off checks and comparisons. It binds only the current command and leaves the active tenant unchanged.

```bash
# Verify the resolved environment profile
tp tenant info --tenant northwind --env staging --json

# Run normal commands against that profile
tp user me --tenant northwind --env staging --json
tp user list "Alex" --tenant northwind --env staging --all --limit 10 --json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp query --from 2026-03-01 --to 2026-03-31 --tenant northwind --env staging --emp-id ALEX --json

# Production alias resolves to the base tenant profile
tp tenant info --tenant northwind --env prod --json
```

Resolution rules:
- `--tenant northwind --env prod` -> `northwind`
- `--tenant northwind --env production` -> `northwind`
- `--tenant northwind --env staging` -> `northwind-staging`
- `--tenant northwind --env stage` -> `northwind-staging`
- `--tenant northwind --env development` -> `northwind-dev`
- other environment values resolve to `northwind-<env>`

If `--env` is used without `--tenant`, the current active tenant is used as the base name after stripping any known environment suffix.

## Add environment profiles
Each environment is a separate profile name. Use the same TimePro tenant account if appropriate, but keep the API URL matched to the environment.

```bash
# Production/base profile
tp login --tenant northwind --api-url https://api.sswtimepro.com

# Staging profile
tp login --tenant northwind-staging --api-url https://api.staging-sswtimepro.com

# Dev/local profile, if available
tp login --tenant northwind-dev --api-url https://api.dev-sswtimepro.example

# Verify
tp tenant list
tp tenant info --tenant northwind --env staging --json
```

## MCP sessions
The MCP command has its own tenant/environment options. They bind only that MCP process and do not change the global active tenant.

```bash
tp mcp --tenant northwind --env staging
tp mcp --tenant ssw-staging
```

## Troubleshooting
| Symptom | Fix |
|---------|-----|
| `Tenant '<name>' not found` | Run `tp tenant list`, then `tp login --tenant <name> --api-url <url>` if the profile is missing |
| `Tenant config '<tenant>-<env>' not found` | Add the derived profile name or use the exact profile with `--tenant <profile>` and no `--env` |
| The command hit production unexpectedly | Run `tp tenant info --tenant <name> --env <env> --json` before the target command |
| `--tenant` is treated as an unexpected command option | Use a current `tp` build, or switch with `tp tenant set <profile>` as a fallback |
