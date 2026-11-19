# Part 05 — DS sheets (board + member)

**Depends on:** 04 · **Est. context:** ~50k

## Read
- PLAN §6.2 (187–199) — visibility/activity matrix, one-active cap
- PLAN §6.3 (201–207) — deadlines
- PLAN §7.2 (277–287) — member DS browse UX
- PLAN §8.5 sheet editor (349–352)
- PLAN §11 DsSheet (612–624)
- PLAN §13 `/ds` + `/board/ds/sheets` routes (850–851, 877–880)

## In scope
1. `DsSheet` entity + config + migration: FK `MonthSheet` cascade, `DueAtUtc` required and in the
   future at creation, `IsActive`, `IsHidden`, `SortOrder`, `Created*`.
   Indexes: `(MonthSheetId, SortOrder)`, filtered unique `MonthSheetId` where `IsActive = 1`.
2. Board sheet editor (create/edit): title, description Markdown (rendered with the shared
   `IMarkdownService`), month sheet picker, required deadline, hidden toggle, sort order.
3. Activate/deactivate with the "Deactivate *X* and activate *Y*?" confirm flow, plus a
   service-layer guard that enforces the single-active rule (never rely on the index alone).
4. Hide/unhide — existing work and points untouched.
5. Member `/ds`: sheets grouped by month newest-first, active sheet pinned, past months in an
   accordion. Each sheet page: header (title, deadline countdown, progress placeholder),
   problem list placeholder with empty state.
6. Deadline display in `App:TimeZone`, live countdown JS island (`countdown.js`).
7. Board sheet list with status pills (Hidden / Inactive / Active) and per-sheet problem counts.

## Out of scope
Problems, URL import, sync, point entries, presets.

## Done when
A board member can create, edit, hide and activate a DS sheet; only one can be active per month;
members see active + past sheets with correct deadlines/countdowns.

---

## Built — notes for the next part

**Routes**

| Method | Route | Notes |
|---|---|---|
| GET | `/ds` | Member overview, month accordions, active sheet pinned |
| GET | `/ds/{id}` | Member sheet page; hidden or unknown ids return 404 |
| GET | `/board/ds/sheets` | Board list with status, deadline, sort, problem count |
| GET/POST | `/board/ds/sheets/new`, `/board/ds/sheets` | Shared form, create |
| GET/POST | `/board/ds/sheets/{id}/edit` | Same form, edit |
| POST | `/board/ds/sheets/{id}/activate` | Swaps the month's active sheet |
| POST | `/board/ds/sheets/{id}/deactivate` | Leaves the month with no active sheet |
| POST | `/board/ds/sheets/{id}/hide` | `hidden=true/false`; hiding also clears active |

**Rules worth reusing**

- `IsActive` and `IsHidden` are independent, except that a hidden sheet can never be active.
  Hiding clears `IsActive`; activating a hidden sheet fails with `HiddenActiveMessage`.
- There is no auto-promotion: deactivating or hiding the active sheet leaves the month with
  zero active sheets, which is a legal state (dashboard shows "None").
- The one-active-per-month rule is enforced by `DsSheetService` plus the filtered unique index
  `IX_DsSheets_OneActivePerMonth`. The service calls `DeactivateOthersAsync` (a direct
  `ExecuteUpdateAsync`) **before** the tracked save.
- **Do not open user transactions.** `SqlServerRetryingExecutionStrategy` is enabled and throws
  "does not support user-initiated transactions". The ordered-statement approach above accepts a
  brief "zero active sheets" window if the save afterwards fails; that is preferable to a 500.
- `DsSheetService` builds its list from one projection (`QueryAsync`), so `FindAsync`,
  `GetVisibleAsync` and the member grouping all share the same shape.
- `ProblemCount` is hardcoded to `0` (`NoProblemsYet`) until Part 06 adds `DsProblem`.

**Form conventions**

- The deadline is a single `datetime-local` string (`yyyy-MM-ddTHH:mm`) parsed with
  `DateTime.TryParseExact`, converted through `App:TimeZone`, and stored as UTC
  (`DateTimeOffset` with offset zero). Creation requires a future deadline; editing does not.
- The month picker lists existing month sheets only; the current month is guaranteed by
  `IMonthSheetService.GetOrCreateCurrentAsync` before rendering.
- New sheets default to the current month and `SortOrder = max + 1` for that month.

**For Part 06**

- `DsSheetService.QueryAsync` is the single place to add a real problem count (join `DsProblems`).
- `Views/Ds/Sheet.cshtml` has a `Problems` card with an empty state and a disabled "Sync solves"
  button; replace that body with the problem table and keep the empty state for zero problems.
- The board list already renders a per-sheet problem count column.
- Audit labels for `ds_sheet.*` live in `ViewModels/Board/AuditViewModel.cs`; add the
  `ds_problem.*` equivalents next to them.
- `wwwroot/js/countdown.js` re-renders every `[data-countdown-to]` element every 30 s, so new
  markup only needs that attribute with an ISO-8601 UTC timestamp.
