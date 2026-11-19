# Part 06 — DS problems, metadata import & presets

**Depends on:** 05 · **Est. context:** ~65k (largest of the DS pair — split into 06a/06b if a
session starts running long: 06a = entities + presets + manual create; 06b = URL import + clients)

## Read
- PLAN §8.5 problem creation + list (353–364)
- PLAN §10.1 LeetCode metadata query (464–476), §10.2 Codeforces endpoints + cache (502–521)
- PLAN §6.4 (209–220), §6.6 (228–244) — points and recalculation
- PLAN §11 DsProblem (626–644), Setting (726–729)
- PLAN §17 Points config (1094–1098)
- PLAN §22.3 (1328–1337) — known API behaviour

## In scope
1. `DsProblem` entity + config + migration: unique `(DsSheetId, Platform, ExternalId)`,
   index `(Platform, ExternalId)`, `AssignedAtUtc` set to `now` on creation.
2. `Platform` / `Difficulty` enums.
3. `LeetCodeClient`: `question(titleSlug)` metadata query via named `leetcode` HttpClient with
   `Referer: https://leetcode.com`, realistic UA, 20s timeout, no auth.
4. `CodeforcesClient` + `CodeforcesProblemCache`: `problemset.problems` loaded at startup,
   refreshed every 24 h by a hosted service, refreshed on demand on lookup miss; process-wide
   2 000 ms `SemaphoreSlim` gate for **all** CF calls.
5. URL parsing service covering every LeetCode/Codeforces URL shape in §8.5, returning
   `Result`-based errors for invalid/unsupported URLs.
6. `/board/ds/problems/new`: paste URL → JSON preview endpoint fills title, platform id,
   canonical URL, rating/tags (CF) or topic tags + `isPaidOnly` warning (LC). Difficulty is always
   a human choice. Points show live from the preset; optional per-problem override. Duplicate
   detection blocks with a clear message.
7. Points materialisation: `problem.Points = PointsOverride ?? preset[difficulty]`.
8. Preset storage read/write (`Setting` rows) + recalculation of problem points on preset change
   (entry recalculation itself lands in Part 12 — expose the service seam and leave a `Result`
   stating 0 solves recalculated until then, or implement it if `PointEntry` already exists).
9. Board problem list: reorder, edit, delete (blocked when solves exist → hide instead),
   per-problem solver list placeholder.

## Out of scope
Sync, solves, credit rules, member solve indicators (placeholder only).

## Done when
Pasting a real LeetCode URL and a real Codeforces URL prefills the form, creates the problem with
the right points, blocks duplicates, and the CF cache survives a restart without hammering the API.

---

## Built — notes for the next part

**Routes**

| Method | Route | Notes |
|---|---|---|
| GET/POST | `/board/ds/problems/new` | Paste URL, import (`command=import`), then create (`command=save`) |
| POST | `/board/ds/problems/preview` | JSON metadata for the paste island; 400 with `{ ok:false, message }` on failure |
| GET/POST | `/board/ds/problems/{id}/edit` | Edit title, difficulty, sheet, points override |
| POST | `/board/ds/problems/{id}/delete` | Removes the problem (guarded by solves in a later part) |
| POST | `/board/ds/problems/{id}/move` | `direction=-1/1`; reorders inside the sheet |
| GET | `/board/ds/problems/{id}/solvers` | Solver placeholder (empty until sync) |

The problem list itself lives on the sheet editor page (`/board/ds/sheets/{id}/edit#problems`) —
the PLAN route table has no separate sheet-detail route, so the editor owns both.

**Two creation paths**

- No-JS: the "Fetch details" button posts the same form with `command=import`; the server parses
  the URL, fetches metadata, and re-renders the prefilled form.
- JS: `wwwroot/js/problem-import.js` posts to `/preview`, fills the (initially `hidden` + `disabled`)
  details block, enables it, and updates the points preview. The server path stays authoritative.

**Points**

- `Points = PointsOverride ?? preset[Difficulty]`, written on create and on every edit.
- Presets live in `Setting` rows `Points.Easy|Medium|Hard`, read through `ISettingsService`.
- `SavePointsPresetsAsync` was the seam Part 12 wired up: it rewrites the rows, recalculates every
  `DsProblem.Points` and every non-manual solve entry, writes `points.presets_changed`, and is now
  driven by `/board/settings`.

**Integrations**

- `LeetCodeClient` — GraphQL `question(titleSlug)` over the named `leetcode` HttpClient
  (browser UA, `Referer: https://leetcode.com`, 20 s timeout, no auth). Only metadata is read.
- `CodeforcesClient` + `CodeforcesProblemCache` — `problemset.problems` (~11.5k problems) loaded at
  startup by `CodeforcesCacheRefreshService`, refreshed every `Sync:ProblemsetCacheHours` (24 h), and
  refreshed on demand when a lookup misses, but only if the copy is older than ten minutes. The
  previous snapshot is kept when a refresh fails.
- `CodeforcesThrottle` is a singleton `SemaphoreSlim` plus timestamp that enforces
  `Sync:CodeforcesMinIntervalMs` (2000 ms) between **all** Codeforces calls, process-wide. Any new
  CF endpoint (Part 07 submissions, handle checks) must go through it.
- `DsProblemUrl` is pure and unit-tested (`DsProblemUrlTests`, 19 cases): LeetCode `/problems/{slug}`,
  Codeforces `/problemset/problem/{id}/{index}` and `/contest/{id}/problem/{index}`, optional scheme,
  `www.`/`m.` hosts, query strings and trailing segments. Canonical URLs are rebuilt from the stored
  id, never trusted from the form.

**Duplicates and assignment**

- Unique index `IX_DsProblems_DsSheet_Platform_ExternalId` plus a service check.
- Both only block a **second copy inside one sheet**. The same platform problem may sit on several
  sheets, including two sheets in the same month: a sheet is not necessarily one month long, so
  re-using a problem elsewhere is legitimate (deviation from PLAN §8.5 at the user's request).
- `AssignedAtUtc = now` on creation and never changes; Part 07 uses it as the AC cutoff.
- Deleting a problem currently always succeeds because no solve rows exist; Part 07 must add the
  "has solves → hide instead" guard here.

**For Part 07**

- `DsProblemRow.SolveCount` is hardcoded `0` in `DsProblemService.GetForSheetAsync`; replace it with
  a count over `DsSolves`.
- The member sheet (`Views/Ds/Sheet.cshtml`) already renders platform/difficulty/points/link rows and
  a `Not synced` placeholder; feed `IsSolved` per member there.
- `DsProblemSolversViewModel.Solvers` is empty on purpose - fill it from solves.
- The disabled "Sync solves" button in that view is the hook for the result toast.

**Part 07 follow-up (shipped)**

- Solve counts now come from `DsSolves`, `GetForMemberAsync` attaches the viewer's own solve, and
  the Solvers page lists credited solves with a manual-credit form.
- Deleting a problem with solves is refused (`has_solves`) by the service and the `Restrict` FK.
- The disabled sync button was replaced by the shared `_SyncButton` + `ds-sync.js`.
