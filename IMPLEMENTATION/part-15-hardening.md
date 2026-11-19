# Part 15 — Hardening & handover (Phase 7)

**Depends on:** all · **Est. context:** ~90k (natural split: 15a tests, 15b polish/perf/docs)

## Read
- PLAN §15 (998–1018), §16 (1022–1048)
- PLAN §18 (1109–1150) — unit, integration, manual QA script, DoD
- PLAN §19 Phase 7 (1193–1196)
- PLAN §22.4 (1339–1344), §22.5 (1346–1352), §22.6 (1354–1361)
- PLAN §14.8 (992–994)

## In scope
1. **Unit tests** for every bullet in §18 — points, recalculation, C# credit, months, bundle,
   URL parsing, normalisation, standings, DST/deadline math.
2. **Integration tests** (`WebApplicationFactory`): register → pending → approve → access;
   deactivated login blocked; member blocked from board routes; member cannot download another
   member's folder; upload limit rejections; grading worker with stubbed Gemini (success, 429 then
   success, permanent failure, retry); sync against captured JSON fixtures (new, duplicate, late,
   pre-assignment, 20-row gap); EF model tests on SQLite in-memory.
3. Security pass: CSP (`self` only), `X-Content-Type-Options`, `Referrer-Policy`,
   `X-Frame-Options: DENY`, HSTS in prod, antiforgery on every POST including JSON headers,
   upload rate limit (10/hour/member), no secrets in logs, `dotnet list package --vulnerable` clean.
4. Polish pass: every empty state, error state, skeleton loader, keyboard navigation, visible focus
   rings, `prefers-reduced-motion`, mobile tables-as-cards, toast `aria-live`.
5. Performance check: seed 50 members × 12 months of data and confirm standings/dashboard render
   acceptably; indexes verified against actual query plans.
6. `--seed-demo` development flag (§22.4) for screenshots/QA.
7. README: setup, seed, migrations, config keys, deploy, backup note for `App_Data` + SQL,
   single-server assumption, how to rotate the Gemini key.
8. Run the full §18 manual QA script and fix everything it finds.

## Out of scope
Anything in §21 (email, teams, social login, 2FA, GitHub API, CSV export, realtime, mobile apps).

## Done when
`dotnet build -c Release` clean with warnings as errors, all tests green, migrations apply to a
fresh DB, the manual QA script passes end to end, and a second developer can run the project from
the README alone.

## Implementation notes (as built)

### 15a - test suite

- **Project:** `tests/OscCommittee.Tests` (xUnit 2.9.3, `Microsoft.NET.Test.Sdk`,
  `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9). It references
  the web project; `SQLitePCLRaw.bundle_e_sqlite3` 2.1.14 is pinned to clear NU1903.
- **EF tests:** `Data/ModelConfigurationTests.cs` builds the real model on SQLite in-memory and
  checks the unique month, content slug, one-active-sheet, solve, point-source and attempt-number
  constraints, the cascade/restrict rules and one named index. `Data/MigrationSmokeTests.cs`
  asserts the model matches the latest migration snapshot against SQL Server, then applies all
  migrations to a fresh LocalDB database and deletes it. LocalDB is optional: when it is not
  installed the apply test returns early and the rest of the suite still runs.
- **Unit tests:** month lazy creation/close/open/rollover/order (`MonthSheetServiceTests`), deadline
  math across Cairo DST (`DeadlineMathTests`), and sync fixtures for new, duplicate, late,
  pre-assignment, 20-row gap/19-row no-gap and Codeforces accepted-only (`DsSyncServiceTests`).
  Together with the part 7/12/13 rule tests every §18 unit bullet has direct coverage.
- **Integration tests:** `Integration/OscAppFactory.cs` is a `WebApplicationFactory<Program>` over a
  temp SQLite file with the real routing, Identity, policies and antiforgery. Startup seeders are
  no-ops, the grading/rollover/Codeforces workers are removed, LeetCode/Codeforces/Gemini are
  stubbed, uploads go to a temp folder, and the auth cookie accepts plain HTTP because the test
  server has no TLS. `WebTestExtensions` logs in/registers with real antiforgery tokens.
  `AccountAccessTests` covers register → pending → approve → access, deactivated login, member
  refusal on board routes and board dashboard access. `SubmissionAccessTests` covers another
  member's submission (404) and folder download (403 for members, ZIP for the board).
  `UploadLimitTests` covers the per-file size cap, the file-count cap and a folder without a `.cs`
  file. `GradingWorkerTests` drives the real worker against a queue of stubbed Gemini answers:
  success, 429 then success, permanent failure after the configured retries, and a non-retryable
  error.
- **SQLite adaptation:** besides generating `rowversion` values, `SqliteModelCustomizer` maps every
  `DateTimeOffset` to UTC ticks so `ORDER BY` and comparisons work. SQLite cannot sort its default
  text mapping, which surfaced as a 500 in the dashboard standings. The production model is
  unchanged; the app only writes UTC values.
- **State:** `dotnet build -c Release` is warning-free and `dotnet test -c Release` is **286/286
  green**. The security-header, antiforgery and rate-limit assertions land with the 15b security
  pass.
