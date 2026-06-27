# Environment Comparison

Use this guide when the same command or business object behaves differently
between local, staging, and production.

Useful evidence:

```bash
tp tenant info --tenant northwind --env prod --json
tp tenant info --tenant northwind --env staging --json
tp user get ALEX --tenant northwind --env prod --json
tp user get ALEX --tenant northwind --env staging --json
```

Keep production read-only unless the user explicitly approves a non-read-only
diagnostic command.
