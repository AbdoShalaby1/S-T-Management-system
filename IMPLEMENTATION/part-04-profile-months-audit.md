# Part 04 — Profile settings, month sheets & audit log

**Status:** ✅ complete (2026-11-18) · **Depends on:** 02 · **Est. context:** ~50k

## Read
- PLAN §7.6 (315–318), §5 Profile settings (169–174)
- PLAN §6.1 (179–186) — month sheet rules
- PLAN §8.4 (343–347), §8.10 audit portion (404)
- PLAN §11: MonthSheet (602–610), Setting (726–729), AuditLog (735–738)
- PLAN §10.5 (583–587) — Markdig setup (needed here as a shared service)
- PLAN §17 notes (1102–1105)

## In scope
1. `/settings` for members and board: display name, LeetCode handle, Codeforces handle,
   password change, read-only account info (email, status, member since, current-month points
   placeholder). Handle "Verify" buttons render disabled until Part 07 and say so.
2. Handle edits reset the platform `SyncState` row (create the table now, unused logic in Part 07).
3. `MonthSheet` entity + config + migration: unique `(Year, Month)`, `Status` Open/Closed,
   `OpenedAtUtc`, `ClosedAtUtc`, `ClosedByUserId?`, `rowversion`.
4. `MonthSheetService`: lazy creation for the current month on first access, `GetOrCreate`,
   label formatting (`November 2026`), close/open with idempotency.
5. Nightly/1st-of-month `BackgroundService` that closes the previous month at 00:05 in
   `App:TimeZone` (uses `TimeProvider`), plus lazy creation.
6. `/board/months`: list with status, totals placeholder, item counts placeholder; open/close with
   confirmation.
7. `AuditLog` entity + `IAuditService` + `/board/audit` list (actor, action, entity, timestamp,
   details JSON collapsed). Wire audit calls into approvals, rejections, deactivations, board
   account creation, month close/open.
8. `Setting` entity + config + seed: `Points.Easy/Medium/Hard`, `Grading.Prompt`,
   `Grading.PromptVersion`, `Uploads.MaxFiles`, `Sync.CooldownSeconds`.
9. Shared `IMarkdownService` (Markdig, `DisableHtml`, advanced extensions, auto identifiers) used
   by every later part.

## Out of scope
Point ledger, standing totals, presets editor UI, DS/task entities, real "current month points".

## Done when
Month sheet exists for the current month, closes automatically and manually, `/settings` persists
profile changes, and every privileged action from Parts 02–03 appears in the audit log.

---

## Built — notes for the next part

Verified against LocalDB: `/settings` renders the profile/password/account cards; saving handles
persists them and clears the corresponding `SyncStates` row (last sync, last seen id, gap flag,
failure count, error); changing the password to a new value and back works and refreshes the
signing-in cookie; `/board/months` lazily creates November 2026, close and reopen are idempotent and
confirmed by the toast; `/board/audit` shows the persisted rows with actor, action, entity and
collapsed before/after JSON; a restart with a still-open October sheet closed it automatically and
logged it as a System row with `"automatic":true`.

What exists now:

- Domain/enums: `MonthStatus`, `SyncPlatform`; entities `MonthSheet`, `SyncState`, `AuditLog`,
  `Setting`, each with an EF configuration and a DbSet on `AppDbContext`.
- `Services/Audit/IAuditService` + `AuditService` — persisted writes (camelCase JSON details, IP
  capture) and paged newest-first reads joined to the actor display name.
- `Services/Months/IMonthSheetService` + `MonthSheetService` — lazy `GetOrCreate`, list with closed-by
  name, close/open with audit and idempotency, `CloseElapsedAsync` for catch-up.
- `Services/Months/MonthRolloverService` — `BackgroundService`, runs once on startup then at 00:05
  local on the 1st; failures are logged and retried on the next tick.
- `Services/Accounts/IProfileService` + `ProfileService` — snapshot, profile update with handle
  normalisation, password change; both audited.
- `Services/Common/` — `IMarkdownService`/`MarkdownService` (Markdig `UseAdvancedExtensions`,
  `DisableHtml`, one shared pipeline) and `Services/Common` untouched otherwise.
- `Data/Seed/DefaultGradingPrompt` + `ISettingsSeeder`/`SettingsSeeder` — seeds the seven keys
  (`Points.Easy/Medium/Hard`, `Grading.Prompt`, `Grading.PromptVersion`, `Uploads.MaxFiles`,
  `Sync.CooldownSeconds`) once; existing values are never overwritten.
- Controllers: `SettingsController` (`/settings`, `/settings/profile`, `/settings/password`),
  `Board/MonthsController` (`/board/months`, `.../{id}/close`, `.../{id}/open`),
  `Board/AuditController` (`/board/audit?page=`), and `BoardController` extended with month status
  and recent activity.
- ViewModels: `ViewModels/Settings/SettingsViewModel` (profile, password, account facts),
  `ViewModels/Board/MonthsViewModel`, `AuditViewModel` + `AuditActionView` label maps.
- Views: `Views/Settings/Index.cshtml`, `Views/Months/Index.cshtml`, `Views/Audit/Index.cshtml`;
  dashboard tiles and recent-activity feed are live; sidebar Settings/Months/Audit lost their
  "Soon" state; the topbar Settings link is live.
- Migration `AddProfileMonthsAudit` (AuditLogs, MonthSheets, Settings, SyncStates).

Decisions and gotchas for Part 05:

- **Two forms, two POST endpoints.** `/settings` cannot post both cards at once: the profile form
  posts to `/settings/profile` and the password form to `/settings/password`, each with
  `[Bind(Prefix = "Profile")]` / `"Password"`. On a validation failure the action sets
  `ViewData["SettingsForm"]` so only that card renders its summary — otherwise both summaries appear.
- **Audit write failures never fail the mutation.** `AuditService.WriteAsync` detaches the row and
  logs an error if `SaveChanges` throws. A successful approval must not become a 500 because the
  audit insert failed.
- **`MemberAdminService` no longer logs `Board.Audit` to Serilog.** Its private helper now delegates
  to `IAuditService` with before/after details (`statusBefore`/`statusAfter`, rejection note).
  Call sites are unchanged in intent; the persisted rows are the record of truth.
- **Month close is idempotent and concurrency-guarded.** Closing a closed month returns an Ok result
  ("already closed"), reopening an open one likewise; `MonthSheet.RowVersion` turns a lost update
  into the "Somebody else changed that month" failure instead of an exception.
- **`CloseElapsedAsync` only closes months strictly older than the current local month**; the
  current month is never auto-closed by the catch-up. The rollover job creates the current sheet as
  well as closing, which is what makes the 1st work even if nobody visits a page.
- **Handle normalisation:** trim, drop a leading `@`, blank → `null`. A change resets the platform's
  `SyncState` row in place (fields cleared, row kept) and clears the verified timestamp. Part 07
  owns the offsets/cursors; do not delete the row.
- **Settings seeding is insert-only.** Editing `appsettings.json` later does not change existing
  rows — the board owns them from first run. `Grading.PromptVersion` starts at `1` and the prompt
  text comes from `Data/Seed/DefaultGradingPrompt`, which is also the "Reset to default" target
  Part 10/15 will need.
- **All three new board views follow the controller-name rule** (`Views/Months/`, `Views/Audit/`),
  matching Parts 03's note. `BoardControllerBase` now also owns `ToAuditRow`.
- **Members and board both use `/settings`**; the `Member` policy already admits board accounts.
  The verified timestamps are displayed but "Verify" stays disabled until Part 07 supplies real
  checks.
- **Placeholders that later parts replace:** current-month points on `/settings` and the dashboard,
  Items/Points columns on `/board/months`. They render `—` and say so.
- **Smoke-testing over plain HTTP in Release fails silently.** `Cookie.SecurePolicy` is
  `Always` outside Development, so a scripted http login gets a `Secure` cookie and every later
  request is anonymous. Run the app with `ASPNETCORE_ENVIRONMENT=Development` for scripted checks,
  or drive a browser over https.
- `MonthSheets` for the current month are created by opening `/board/months` or any board page that
  calls `GetOrCreateCurrentAsync`; later parts that attribute points can rely on the service.
