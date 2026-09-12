# Manual MCP client testing for the SDK 2.x upgrade

The automated harness (`tests/SSW.TimePro.Cli.Integration/Mcp`) proves the wire shapes and the
staging smoke proves one real read/write round trip. Neither can see what a real client does with
that wire: approval prompts, annotation rendering, how an error reads in the transcript, or whether
a client's own discovery path works. This doc is the manual pass that closes that gap.

## Build the candidate server

```bash
cd <worktree>            # e.g. .timepro-worktrees/mcp-v2, branch feat/mcp-sdk-2
dotnet build -c Release
```

The server entry point is then:

```
dotnet <worktree>/src/SSW.TimePro.Cli/bin/Release/net10.0/SSW.TimePro.Cli.dll mcp --tenant <name>
```

Use `--tenant ssw-staging` for anything that writes. `dotnet run --project <worktree>/src/SSW.TimePro.Cli -- mcp --tenant ssw-staging`
also works but adds build output on first start, which some clients treat as a startup failure —
prefer the built DLL.

Do not install the branch as a global tool; that would replace the `tp` the rest of the machine uses.

### Client registration

Claude Code:

```bash
claude mcp add timepro-sdk2 -- dotnet <worktree>/src/SSW.TimePro.Cli/bin/Release/net10.0/SSW.TimePro.Cli.dll mcp --tenant ssw-staging
```

Codex CLI (`~/.codex/config.toml`):

```toml
[mcp_servers.timepro-sdk2]
command = "dotnet"
args = ["<worktree>/src/SSW.TimePro.Cli/bin/Release/net10.0/SSW.TimePro.Cli.dll", "mcp", "--tenant", "ssw-staging"]
```

Claude Desktop (`~/Library/Application Support/Claude/claude_desktop_config.json`) and
VS Code / Copilot (`.vscode/mcp.json` or the user `mcp.json`) take the same
`command` / `args` pair.

Register it under a distinct name (`timepro-sdk2`) so the existing `timepro` entry stays untouched
and can be compared side by side.

## Per-client checklist

Run all of these against `ssw-staging`. Northwind (`NWIND`) is the only client to touch. Pick one
of its projects that uses iterations and one that does not; project ids are not committed to this
public repository, so get them from `tp project list --client NWIND --tenant ssw-staging --json`.

1. **Discovery** — the client connects without error and lists the server as healthy. Note in the
   table whether it used the 2026-07-28 `server/discover` path or the legacy `initialize`
   handshake (the CLI logs neither; infer it from the client's own logs, e.g. `claude --debug`,
   Codex `RUST_LOG=debug`, the Claude Desktop MCP log, or the VS Code MCP output channel).
2. **tools/list count** — 18 tools with the accounting feature off, 47 with
   `tp feature accounting enable`. Anything else is a regression, not a client quirk.
3. **One read** — ask for this week's timesheets (`get_timesheets`) and a Northwind lookup
   (`get_projects_for_client` with `NWIND`). Do **not** compare `get_timesheets` against
   `tp ts get --json`: the two have always differed, and deliberately so — MCP returns a flat array
   and skips weekends, the CLI returns an envelope grouped by day including weekends (recorded in
   the parity table as "MCP reshapes the row; CLI returns the API shape"). Compare instead against
   the shape in `tests/SSW.TimePro.Cli.Integration/Goldens/Mcp/Tools/GetTimesheets.populated.json`,
   or against the same tool call on the installed 0.3.x release with identical arguments.
4. **One write** — create one timesheet on the iteration-using Northwind project with the note
   `MCP SDK2 client check, safe to delete`, confirm it reads back, then delete it. Do not create
   leave requests and do not import leave balances.
5. **Error rendering** — call a tool with a bad argument (e.g. `get_projects_for_client` with a
   nonexistent client id) and check the failure reads as a message rather than a raw stack trace or
   a silent empty result. Also try an unknown tool name if the client allows it: the server answers
   JSON-RPC `-32602 Unknown tool: '<name>'`.
6. **Cancellation** — start a read and interrupt it (Esc in Claude Code, Ctrl+C in Codex CLI).
   The server must stay usable for the next call, not wedge or die.
7. **Shutdown** — quit the client and confirm no `dotnet ... mcp` process is left behind
   (`pgrep -fl "SSW.TimePro.Cli.dll mcp"`).

## What the harness cannot see — look for these explicitly

- **Approval prompts.** Two tools are annotated today: `get_leave_balance_status`
  (`readOnlyHint: true`, `destructiveHint: false`) and, on the accounting surface,
  `import_leave_balances` (`destructiveHint: true`, `idempotentHint: true`, `readOnlyHint: false`).
  Everything else is unannotated, so clients fall back to their default of treating each call as
  potentially destructive. Confirm each client prompts before `create_timesheet` /
  `update_timesheet` / `delete_timesheet` / `update_leave`, and note whether reads prompt too
  (they will until the annotation follow-up widens the coverage).
- **Annotation rendering.** For those same two tools, check the badge the client shows matches the
  advertised annotation: `get_leave_balance_status` read-only, `import_leave_balances` destructive.
  For every other tool there is nothing to render, so a badge means the client is inferring from
  the name — worth knowing before the annotation PR.
- **Tool description truncation.** Several descriptions are long; check how each client's tool
  picker displays them.
- **Stdout hygiene in practice.** The harness fails loudly on non-protocol stdout. In a real client
  it looks like a silent connection drop instead, so watch for a server that connects and then
  disappears.
- **Latency and timeouts.** Staging is slower than the WireMock fixtures; a client with a short
  tool timeout may cut off a slow week query.

## Results

Record the client version exactly as the client reports it (`claude --version`,
`codex --version`, Claude Desktop → About, VS Code → About plus the Copilot Chat extension
version).

| Client | Version | Negotiated protocol | tools/list | Read | Write | Errors | Cancel | Shutdown | Notes |
|---|---|---|---|---|---|---|---|---|---|
| Claude Code | | | | | | | | | |
| Codex CLI | | | | | | | | | |
| Claude Desktop | | | | | | | | | |
| VS Code / Copilot | | | | | | | | | |

## Automated evidence already on the branch

Re-run these before starting the manual pass so a failure there is not mistaken for a client bug:

```bash
dotnet test tests/SSW.TimePro.Cli.Tests/
dotnet test tests/SSW.TimePro.Cli.Integration/
TIMEPRO_MCP_SMOKE_PROJECT=<NWIND project with iterations> \
  TIMEPRO_MCP_SMOKE_TP="dotnet $PWD/src/SSW.TimePro.Cli/bin/Release/net10.0/SSW.TimePro.Cli.dll" \
  scripts/e2e/test-mcp-smoke.sh
```

Protocol versions the server accepts under 2.2.0, verified over stdio:

- `initialize` advertises `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25`. Verified by
  execution: `2025-11-25`, `2025-06-18` and `2024-11-05` each negotiate back the requested version
  and then serve 18 tools.
- `initialize` with `2026-07-28` is refused with `-32022` and a `supported` list — correct, that
  revision removed the handshake.
- `server/discover` answers `supportedVersions: ["2026-07-28"]`, after which `tools/list` and
  `tools/call` work with the per-request `_meta` envelope (`io.modelcontextprotocol/protocolVersion`,
  `clientInfo`, `clientCapabilities`; the server rejects the call when `clientCapabilities` is
  missing).
