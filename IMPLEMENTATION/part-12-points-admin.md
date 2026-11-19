# Part 12 — Points admin & preset recalculation

**Depends on:** 07, 11 · **Est. context:** ~50k

## Read
- PLAN §8.8 (386–390), §8.10 presets (400)
- PLAN §6.4 (209–220), §6.5 (222–226), §6.6 (228–244), §6.8 (259–263)
- PLAN §11 PointEntry (706–720), Setting (726–729)
- PLAN §13 `/board/points/*`, `/board/settings` (896–900)

## In scope
1. `PointsService`: totals per user/month, per category, on demand (no denormalised totals).
2. `/board/points/adjust`: bonus / adjustment modal — member, month (current or previous only,
   enforced client **and** server side), integer amount (negative allowed), optional reason.
   Writes `PointEntry` (`Bonus` with `SourceId = null`, or `Adjustment`) with `FinalPoints`.
3. Member month-by-month point detail (`/board/members/{id}`): full entry breakdown per month,
   manual entries flagged, bonuses with reasons, adjustments as separate lines.
4. Presets editor in `/board/settings`: Easy/Medium/Hard with a confirmation stating closed months
   will change.
5. Recalculation per §6.6 in a transaction: update all problems' `Points`, then all non-manual
   solve entries' `BasePoints`/`FinalPoints`; report "Recalculated N solves across M months";
   manual overrides untouched. Per-problem override change recalculates that problem only.
6. Manual "Recalculate DS points" recovery action (`/board/points/recalculate`).
7. Audit rows for every bonus, adjustment, preset change, recalculation.
8. Unit tests: preset lookup, override precedence, late → 0, bonus signage, closed months included,
   manual overrides preserved, per-problem override isolation.

## Out of scope
Standings pages, CSV export, deleting point entries (not in v1).

## Done when
The board can explain every point in the system from ledger rows alone, and preset changes rewrite
historical DS points without touching manual overrides.

## Implementation notes (as built)

- **Files:** `Services/Points/PointsService.cs` (totals, category totals, month totals,
  month-by-month ledger, bonus/adjustment writes, `PointEntryRules`), `Services/Points/DsPointsRecalculator.cs`
  (`DsPointRules`, `IDsPointsRecalculator`, audit + counts), `Controllers/Board/BoardPointsController.cs`,
  `Controllers/Board/BoardSettingsController.cs`, `ViewModels/Board/PointsViewModels.cs`,
  `Views/BoardPoints/Adjust.cshtml`, `Views/BoardPoints/Recalculate.cshtml`,
  `Views/BoardSettings/Index.cshtml`, `Views/Members/Detail.cshtml`.
- **Totals stay on demand.** `GetTotalAsync`, `GetCategoryTotalsAsync` and `GetMonthTotalsAsync`
  always sum `FinalPoints`; nothing is denormalised.
- **Bonus vs adjustment:** a bonus is a normal ledger row with `SourceId = null` and
  `IsManualOverride = false`; an adjustment sets `IsManualOverride = true`. Both bind ±999, reject
  zero, trim reasons to 400 characters, and re-check that the month is the current or previous local
  month inside the service. The form lists only those two months.
- **Preset save is one transaction in effect:** `SettingsService` writes the three setting rows,
  calls `IDsPointsRecalculator.ApplyAsync` (problems + every non-manual solve, closed months
  included, late pinned to 0) and saves once. The audit row records before/after presets plus
  `problemsRecalculated`, `solvesRecalculated` and `monthsAffected`; the toast uses
  "Recalculated N solves across M months".
- **Per-problem edits** call `ApplyToProblemAsync`, which narrows the query to that problem's solves
  before recalculating, so a single override change cannot rewrite unrelated history.
- **Manual recovery** is `GET/POST /board/points/recalculate`; the confirmation page names the closed
  months and the POST writes `points.recalculated`.
- **No schema change** - Part 12 only reads/writes existing `PointEntry`, `Setting`, `DsProblem`
  and `DsSolve` rows. `PointEntryConfiguration.SourceIndex` already permits null sources for
  bonuses/adjustments.
- **Testing:** the DB-level behaviours are covered through pure-rule tests (`DsPointRules.Plan`,
  `Materialise`, `SolvePoints`, manual preservation, per-problem isolation, closed-month inclusion,
  amount bounds/signage) because the test project has no EF InMemory/Sqlite provider. Authenticated
  smoke checks covered the adjust form, a successful bonus, the member ledger, settings save and the
  recalculation page. `dotnet test -c Release` is 179/179 green.
