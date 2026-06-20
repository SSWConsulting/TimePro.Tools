# AutoScrum Selection Logic — Design Plan

## 1. AutoScrum's Algorithm (cited to source)

AutoScrum is a Blazor app that generates daily scrum reports from Azure DevOps work items.
Source: `/tmp/autoscrum-src` (read-only reference).

### 1.1 Data Fetched

`AzureDevOpsService.GetWorkItemsForSprint` fetches all sprint work items via WIQL:

```
SELECT [State], [Title] FROM WorkItems WHERE
  [Assigned to] = @Me AND
  [System.IterationPath] = '<sprint.Path>'
ORDER BY [State] Asc, [Changed Date] Desc
```

Fields fetched per item (`AzureDevOpsService.GetQueryFields`, line 183–198):
- `System.State`, `System.WorkItemType`, `System.Parent`, `System.IterationPath`
- `System.CreatedDate`, `System.ChangedDate`, `System.Title`
- `Microsoft.VSTS.Common.StateChangeDate`
- `Microsoft.VSTS.CMMI.Blocked` (null or `"Yes"`)
- `System.AssignedTo`

### 1.2 State Classification

`WorkItem.StateType` property (`AutoScrum.Core/Models/AzureDevOps/WorkItem.cs`, lines 39–47):

| State string   | StateType    |
|----------------|--------------|
| "In Progress"  | InProgress   |
| "Done"         | Done         |
| "Committed"    | Committed    |
| "Approved"     | Approved     |
| anything else  | NotStarted   |

### 1.3 Yesterday vs Today Selection

`DailyScrumService.SetWorkItems` (`AutoScrum.Core/Services/DailyScrumService.cs`, lines 31–68):

```
yesterday = GetPreviousWorkDate(today)   // Friday if Monday; otherwise today-1
```

For every work item that is `InProgress` or `Done` AND assigned to the current user:

```
hasChangedRecently = wi.StateChangeDate > yesterday && wi.StateChangeDate < todayMidnight

if wi.StateType == InProgress:
    → Add to TODAY (always)
    → Add to YESTERDAY if wi.StateChangeDate < todayMidnight  (i.e. not started today)

else if wi.StateType == Done && hasChangedRecently:
    → Add to YESTERDAY only (completed since yesterday)
```

Key points:
- **Today**: all InProgress items assigned to the user, regardless of when they started.
- **Yesterday**: InProgress items whose state changed before today midnight (i.e. already existed yesterday), plus Done items completed in the yesterday→today window.
- `Committed` and `Approved` states are treated as "In Progress" in the **renderer** only (displayed as "In Progress") but are NOT included by the selection logic — only real `InProgress` and `Done` states trigger inclusion.
- Items without a parent are added directly; items with a parent bubble up their parent as a container (with the child nested under it).

### 1.4 Blockers

`AzureDevOpsDailyScrumGenerator.GenerateBlockersMarkdown` (`AutoScrum.AzureDevOps/Services/AzureDevOpsDailyScrumGenerator.cs`, lines 108–140):

- A blocker item = any work item in the **Today** list where `wi.IsBlocked == true` (field `Microsoft.VSTS.CMMI.Blocked == "Yes"`), assigned to the user.
- Also checks children of Today items for the same blocked condition.
- Additionally, the user can manually type a free-text blocker (`user.Blocking`).
- All blocked items appear under a **"Blocking"** section separate from Yesterday/Today.

### 1.5 Previous Work Day Calculation

`DateService.GetPreviousWorkDay` (`AutoScrum.Core/Services/DateService.cs`, lines 39–49):

```
if today is Monday  → yesterday = today - 3 (Friday)
if today is Sunday  → yesterday = today - 2 (Friday)
else                → yesterday = today - 1
```

No public holiday awareness — only weekend skipping.

### 1.6 Report Format

The renderer (`AzureDevOpsDailyScrumGenerator.GenerateDayMarkdownReport`, lines 75–106) normalises non-standard states for display:

```
if wi.State not in ("In Progress", "Done") → display as "In Progress"
```

---

## 2. Current `tp scrum` Implementation

### 2.1 What it does

`ScrumDataGatherer.BuildAsync` uses two data sources:

1. **TimePro timesheets** — today's real (non-suggested, non-leave) timesheets determine which projects are active.
2. **GitHub CLI (`gh`)** — `gh pr list --author @me` against the mapped repo for each project.

**Yesterday selection:**
- Walks back up to 14 calendar days skipping Sat/Sun.
- Finds the most recent day where at least one timesheet exists for the same project(s) as today.
- Adds merged PRs from a 7-day window (up to and including that day) as "Done" items.

**Today selection:**
- Adds open PRs (state = `OPEN`) by the current user in the mapped repo as `PBI` items.
- Timesheet notes are captured as metadata (not rendered as bullets).

**Blockers:** Not implemented — no blocked items are surfaced.

### 2.2 The Gap vs AutoScrum

| Capability | AutoScrum | Current `tp scrum` |
|---|---|---|
| Yesterday = last working day (skip weekends) | Yes (DateService) | Yes (walks back skipping Sat/Sun) |
| Yesterday window | Items changed since previous work day midnight | All merged PRs in last 7 days |
| Today = in-progress items | All InProgress work items in sprint | Only OPEN PRs |
| Done items shown as yesterday | Items completed since yesterday | PRs merged in 7-day window |
| Committed/Approved items (planned not started) | Not surfaced (NotStarted fallthrough) | Not applicable |
| Blockers | Explicit `IsBlocked` field on work items | Not surfaced |
| State reasoning | `StateChangeDate` field from ADO | None — only PR open/merged state |
| Hierarchy | Parent/child grouping | Flat list |
| User assignment filtering | `AssignedToEmail == user.Email` | Implicit (gh `--author @me`) |

Key gaps:
1. **No blocker detection** — blocked items (issues/PRs with "blocked" labels or status) are never surfaced.
2. **Today includes only open PRs** — assigned GitHub issues or work items in a "Committed/In Progress" state are not included even when mapped.
3. **Yesterday window is too wide** — using a rolling 7-day window can surface stale merged PRs unrelated to the previous working day.
4. **No state-change-date reasoning** — `tp scrum` doesn't consider when a PR/issue changed state, only whether it's open or merged.
5. **GitHub issue assigned items missing** — `gh issue list --assignee @me --state open` is available in `GhCli.ListMyAssignedIssues` but never called from `ScrumDataGatherer`.

---

## 3. Proposed Improved Selection Logic

### 3.1 Goals

- Mirror AutoScrum's three-bucket model: Yesterday / Today / Blockers.
- Remain grounded in TimePro data (timesheets as the source of truth for which project is active).
- Use GitHub as the work-item layer: PRs + issues, with state-change-date equivalents (mergedAt, updatedAt).
- Keep the logic pure and unit-testable (no IO, injected date).

### 3.2 Algorithm

#### Previous Work Day

```
PreviousWorkDay(today):
  candidate = today - 1
  while candidate.DayOfWeek in [Saturday, Sunday]:
    candidate -= 1
  return candidate
```

(Identical to AutoScrum's DateService — no public holiday awareness needed at this level; TimePro data naturally has no entries on public holidays.)

#### Yesterday Items

A GitHub PR or issue qualifies as "yesterday" if:
- Its most recent state change (mergedAt for PRs, closedAt for issues) falls in the half-open interval `[previousWorkDayMidnight, todayMidnight)`.
- **OR** it was already in an in-progress state (open PR / open assigned issue) before today AND it also matches a timesheet logged on the previous working project day.

```
yesterdayMidnight = previousWorkDay at 00:00 local
todayMidnight     = today at 00:00 local

Item qualifies for Yesterday if:
  (item.MergedAt >= yesterdayMidnight && item.MergedAt < todayMidnight)   // PR merged yesterday
  OR
  (item.ClosedAt >= yesterdayMidnight && item.ClosedAt < todayMidnight)   // issue closed yesterday
  OR
  (item.State == "OPEN" && item.UpdatedAt < todayMidnight && hadMatchingTimesheetOnPreviousDay)
```

The third rule mirrors AutoScrum's rule that InProgress items with StateChangeDate < todayMidnight appear in Yesterday.

#### Today Items

A GitHub PR or issue qualifies as "today" if:
- Its state is `OPEN` (PR) or open (issue assigned to me).
- It belongs to a repo mapped to a project that has timesheets today.

```
Item qualifies for Today if:
  item.State == "OPEN" || item.IsOpenIssue
```

This extends the current `tp scrum` by also including assigned open issues (not just open PRs).

#### Blockers

An item qualifies as a blocker if:
- It is a GitHub issue with a label containing "blocked" (case-insensitive).
- OR it is a PR with a label containing "blocked".
- OR the PR has a draft state (treated as implicitly soft-blocked for surfacing purposes).

```
Item qualifies as Blocker if:
  item.Labels.Any(l => l.Contains("blocked", OrdinalIgnoreCase))
  OR item.IsDraft == true
```

Blockers appear in their own section and are also included in the Today list (consistent with AutoScrum showing blockers from the Today set).

#### Deduplication

- An item already shown as a Blocker is not repeated in the Today or Yesterday bullet lists.
- An item already in Yesterday is not repeated in Today.

### 3.3 Component Design

Following the `CheckEvaluator` / `WeekCoverageService` split pattern:

```
Features/Scrum/
  ScrumItemSelector.cs      ← pure static class, no IO
  ScrumDataGatherer.cs      ← existing, gains --smart path
  ScrumCommand.cs           ← gains --smart flag
```

`ScrumItemSelector` is a pure class that takes plain data structures (no `gh` or HTTP calls) and returns bucketed `ScrumItem` lists. All IO stays in `ScrumDataGatherer`.

### 3.4 Wire-up

`ScrumDataGatherer.BuildAsync` gains an optional `bool smartSelection = false` parameter (exposed via `--smart` on `ScrumCommand`). When true, it:
1. Calls `GhCli.ListMyAssignedIssues` in addition to `ListMyPullRequests`.
2. Passes the combined data to `ScrumItemSelector.Select(...)`.
3. Replaces the existing PR-loop logic with the selector's output.

When false (default), existing behaviour is unchanged.

---

## 4. What's Left for Follow-up

- **Public holiday awareness**: today the logic skips weekends; public holidays are not yet excluded from the "previous work day" calculation. The `CheckEvaluator.LeaveDay` pattern could be reused.
- **GitHub label fetch**: `gh pr list` and `gh issue list` do not include labels by default. The GhCli wrapper needs a `--json labels` field added.
- **Draft PR detection**: `--json isDraft` field not yet in `GhCli.PullRequest`.
- **Configurable states**: "blocked" label name is hardcoded; could be a config entry.
- **Hierarchy**: AutoScrum groups child tasks under their PBI parent. With GitHub, the natural grouping is issue → linked PR. A follow-up could group open PRs under their linked issue.
- **Azure DevOps work item integration**: if the project has ADO work items (not just GitHub), the selector could consume them via the MCP ADO tools.

## 5. Refinement — 14-day lookback + configurable cutoff

Two changes addressing the yesterday/today picking AutoScrum never got right:

- **14-day in-progress lookback.** An open item counts as "yesterday" when its last
  activity falls in `[today - YesterdayLookbackDays, cutoff)` (default 14 days),
  instead of requiring a timesheet on the *literal* previous work day. This keeps
  ongoing work visible across gaps and — crucially — on the **first day of a new
  sprint**, when the previous day inside that sprint is empty (-1 day). The old
  `hadPreviousProjectDay` gate is removed.
- **Configurable cutoff (`ScrumConfig.CutoffTime`, default null = midnight).** The
  cutoff is the local yesterday/today boundary. Completed (merged/closed) work
  **before** it → Yesterday (done); **after** it (this morning) → Today (done) —
  so a PR merged at 09:30 before a 10:00 stand-up is never lost. Set e.g.
  `"cutoffTime": "09:00:00"` in `~/.config/timepro-cli/config.json`.

Selector signature: `Select(today, previousWorkDay, prs, issues, cutoff? = null, inProgressLookbackDays = 14)`.
The merged/closed yesterday window remains `[previousWorkDay 00:00, cutoff)` and still
spans the weekend (Monday picks up Saturday/Sunday). Covered by
`ScrumItemSelectorYesterdayTodayTests`.
