# AppInsights Exception Correlation

Use this guide only after `tp` has identified a concrete employee, date,
invoice, client, or external reference. AppInsights is a follow-up correlation
step, not the starting point.

Useful evidence:

```bash
tp tenant info --tenant northwind --env staging --json
tp ts get 2026-03-12 --tenant northwind --env staging --emp-id ALEX --json
tp invoice get 142 --tenant northwind --env staging --json

az monitor app-insights query \
  --app "<appId>" \
  --analytics-query "requests | where timestamp > ago(24h) | where tostring(customDimensions) has '142' | project timestamp, name, resultCode, success, operation_Id | order by timestamp desc | take 50"

az monitor app-insights query \
  --app "<appId>" \
  --analytics-query "exceptions | where timestamp > ago(24h) | where tostring(customDimensions) has '142' or outerMessage has '142' | project timestamp, type, outerMessage, operation_Id | order by timestamp desc | take 50"
```

Keep telemetry output sanitized. Do not include resource identifiers,
credentials, user emails, sensitive stack-trace details, or real client data in
a public guide or PR. Use the smallest time window and a `tp`-derived reference.
