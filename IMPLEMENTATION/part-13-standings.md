# Part 13 — Standings (Phase 6)

**Depends on:** 07, 11, 12 · **Est. context:** ~55k

## Read
- PLAN §9 (408–441) — tabs, views, rules, ties
- PLAN §7.4 (304–307)
- PLAN §6.5 (222–226)
- PLAN §14.6 standings sketch (981–986)
- PLAN §11 PointEntry (706–720)

## In scope
1. `StandingsService`, computed on request: totals per member per month by category
   (DS / tasks / bonus), derived by grouping `PointEntry`.
2. `/standings?tab=all|ds|tasks&view=summary|detail&member={id}&month={yyyy-MM}`:
   - Tabs All / DS / Tasks, default All.
   - **Summary** = open month only; columns rank, member, DS points, task points, bonus, total;
     context strip with active DS sheet (viewer progress) and active C# task (viewer status);
     ranking follows the selected tab.
   - **Detailed** = member selector + month table (DS solved count, DS points, tasks solved count,
     task points, bonus, total), per-DS-sheet `solved / total` counts (no problem list!), per-task
     line (title, score, points, late; board gets attempt links, members only results), bonuses with
     reasons.
3. Tie-breakers: same total → more DS solves → earlier registration.
4. Deactivated members excluded; board filter to include them.
5. Board accounts excluded from standings entirely and ungradable.
6. Empty-month state, month selector, pagination for large cohorts.
7. Unit tests: tie-breakers, category filters, deactivated exclusion, totals equal ledger sums.
8. Member dashboard (`/`) upgraded to real data: greeting, month total, active task card, active
   DS sheet progress, rank chip, "needs attention" banners (missing handle, failed grading,
   improvable attempt), quick links.

## Out of scope
Solution/code visibility in standings (never), CSV/PDF export, caching.

## Done when
Totals match the ledger exactly for every member and month (verified by hand), and no member can
see another member's code, attempts, or problem-level detail through any standings route.

## Implementation notes (as built)

- **Files:** `Services/Standings/StandingsRules.cs` (pure parsing, `Tally`, eligibility, ranking),
  `Services/Standings/StandingsService.cs`, `Controllers/StandingsController.cs`,
  `ViewModels/Standings/StandingsViewModels.cs`, `Views/Standings/Index.cshtml`,
  `Views/Standings/_Summary.cshtml`, `Views/Standings/_Detail.cshtml`,
  `Controllers/HomeController.cs`, `ViewModels/Home/DashboardViewModel.cs`, `Views/Home/Index.cshtml`.
  DI and navigation: `Program.cs` (`IStandingsService`), `Views/Shared/_Sidebar.cshtml`
  (Standings no longer "Soon"), `Views/_ViewImports.cshtml`.
- **One computation path.** `GetSummaryAsync` fetches the month's `PointEntry` rows once, groups by
  user and folds them through `StandingsRules.Tally`. `GetMemberDetailAsync` does the same per
  member. Nothing is cached or stored.
- **Category mapping:** every `DsSolve` entry counts as a DS solve (late ones are still solves, worth
  0), while a `CSharpSubmission` counts as a solved task only when `FinalPoints > 0`. `Bonus` and
  `Adjustment` share the single bonus column so the row columns always add up to the total;
  adjustments keep their manual label in the detail list.
- **Eligibility:** active members are always included, even at zero; deactivated members only when a
  board member sets `includeDeactivated=true` (the flag is forced off for non-board requests).
  Board accounts are removed via `AspNetUserRoles` + `Roles.Board`, so they are neither ranked nor
  selectable in the detail view.
- **Ranking:** `total → DS solves → earlier registration → user id`, using the selected tab's column
  for the first key. Ranks are assigned after the full sort, so page 2 continues the numbering and
  fully identical records remain stable.
- **Views:** summary shows the context strip (active DS sheet progress, active task status), the
  ranked table, tie-break footnote, empty-month state and pagination (25 rows; ranks global). Detail
  shows the member header, GET member/month selectors, a month table (months that have ledger rows,
  newest first, selected row highlighted), the four month totals, per-sheet `solved / total` counts
  with points, task lines and bonus/adjustment reasons.
- **Privacy:** sheets never carry a problem list, and `AttemptHref` is only populated for board
  requests (`/board/submissions/{id}`); members receive `null` and see results only. Hidden sheets and
  tasks are excluded unless the viewer is board.
- **Fallbacks:** an unknown/ineligible member resolves to the viewer (board: first eligible member);
  an unknown month resolves to the open month, then the newest sheet. With no open month the summary
  shows "Standings are not open yet".
- **Dashboard:** `GetDashboardAsync` reuses the summary (rank/cohort/total) and context queries, then
  adds alerts for missing handles, the newest failed submission and an improvable active-task score
  (below 10, on time, deadline still ahead). The page renders the greeting, month total, rank chip,
  task/sheet cards, alert list and quick links; board accounts show "Board accounts are not ranked".
- **No schema change** — Part 13 only reads existing tables.
- **Testing:** 25 pure-rule tests in `Services/Standings/StandingsRulesTests.cs` cover category
  columns, late-solve vs zero-point-task counting, tab filters, all tie-break levels, eligibility and
  query parsing. `dotnet test -c Release` is **204/204 green**. Authenticated smoke checks (board and
  member) covered summary tabs, detail with month/sheet/task/bonus sections, tie order, deactivated
  inclusion, member fallback, absent board links and the dashboard; the temporary rows were removed
  from LocalDB afterwards.
