# Part 07 — DS sync & DS points (Phase 3)

**Depends on:** 06 · **Est. context:** ~75k (hardest logic part — if tight, stop after the
service layer + tests and do the UI polish in a follow-up pass)

## Read
- PLAN §10.1 (447–493) — LeetCode, full sync algorithm, failure handling
- PLAN §10.2 (495–521) — Codeforces sync + rate limit
- PLAN §6.3 (201–207), §6.4 (209–220), §6.5 (222–226)
- PLAN §7.2 sync UX (283–287)
- PLAN §11 DsSolve (646–660), SyncState (731–733), PointEntry (706–720)
- PLAN §16 relevant rows (1022–1048)
- PLAN §18 unit bullets 1–2 (1113–1114)

## In scope
1. `DsSolve` + `PointEntry` + `SyncState` entities, configs, migration.
   Unique `(UserId, Platform, ExternalId)` on solves; unique filtered `(Category, SourceId)` on
   point entries; index `(MonthSheetId, UserId)`.
2. `DsSyncService` implementing both algorithms exactly: 20-item LC window, CF incremental walk to
   `LastSeenSubmissionId`, `AssignedAtUtc <= timestamp` cutoff, dedupe, `IsLate` from the sheet
   deadline, credit in the **completion month** (create the target `MonthSheet` lazily).
3. `DsCreditService`: creates the solve + point entry in one transaction, idempotent, and is
   reusable by board manual credit.
4. `POST /ds/sync`: requires handle (400 `needs_handle`), single-flight (409
   `sync_in_progress`), per-platform summary + per-problem results for toasts.
5. Failure handling: exponential backoff (2s, 8s) on 403/429/Cloudflare, then 503 with a friendly
   message; never retry in a tight loop; last successful state preserved; `FailureCount` /
   `LastError` updated.
6. Handle verification endpoints (`/settings/handles/verify`) using `matchedUser` and
   `user.info` — advisory only, never blocking.
7. Member DS UI: solved badge, matched submission time + language, platform link, Late badge with
   `0 pts`, sync button states (disabled/no handle, in-progress).
8. Board manual credit of a solve (for the 20-item gap) with audit entry.
9. Unit tests for: cutoff, dedupe, late, completion-month attribution, gap flag.

## Out of scope
C# tasks, standings, bonus points, preset recalculation of entries (Part 12).

## Done when
Real accounts sync, credits appear exactly once, duplicates are no-ops, late solves score 0,
pre-assignment ACs are ignored, and a repeated problem shows as solved on every sheet that lists it.

---

## Implementation notes (shipped)

- **Entities/indexes:** `DsSolves` (`IX_DsSolves_User_Platform_ExternalId`, unique) and
  `PointEntries` (`IX_PointEntries_Category_SourceId`, unique, filtered `[SourceId] IS NOT NULL`;
  `IX_PointEntries_MonthSheetId_UserId`). `DsSolves.DsProblemId` uses `DeleteBehavior.Restrict`;
  the service additionally refuses deletion with `has_solves` ("hide the sheet instead").
- **Pure rules:** `DsSyncRules` holds cutoff, late, completion month, gap and id/URL helpers;
  `DsSyncRulesTests` covers them, alongside the sync engine tests.
- **Crediting:** `DsCreditService.CreditAsync` is the single credit path for sync and board manual
  credit. It loads the problem's `Points`/`AssignedAtUtc`/sheet deadline, checks the cutoff, writes
  solve + point entry in one `SaveChanges`, and treats a unique-index race as "already credited".
- **Sync engine:** `DsSyncService` runs both linked platforms, one `DsSyncGate` slot per
  member+platform (409 `sync_in_progress`). LeetCode walks its 20-row window newest-first;
  Codeforces stops at the stored submission id. Hidden sheets are excluded from candidates. When
  several sheets carry the same platform id, the earliest assignment wins, and every sheet
  carrying that problem renders the credited solve. The cursor is kept through failures.
- **Retries:** 403/429/Cloudflare (LC also detects a non-JSON challenge page) back off 2 s then 8 s
  before reporting `rate_limited`; the endpoint answers 503 when every platform failed.
- **Endpoint shape:** `POST /ds/sync` returns 400 `needs_handle`, 409 `sync_in_progress`,
  503 all-failed, 200 otherwise. `X-Sync-Client: js` gets JSON; a plain form post
  gets a TempData toast and a local redirect.
- **Member UI:** `_SyncButton` is shared by the dashboard and sheet header; `ds-sync.js` handles
  the in-progress disable, fetch, toast and an immediate reload on success. Solved rows show time,
  language, submission link, a Late badge with 0 pts, and a manual marker; a solve credited on
  another sheet is marked "credited earlier".
- **Board UI:** the Solvers page lists credited solves and offers manual credit (member select,
  local `datetime-local`, language, reason). Verification buttons on `/settings` call
  `/settings/handles/verify`; `handle-verify.js` shows the advisory toast/status and the service
  records `LeetCodeVerifiedAtUtc`/`CodeforcesVerifiedAtUtc` only for the saved handle.
- **Audit labels:** `profile.handle_verified`, `ds_solve.credited_manually`, entities `DsSolve`
  and `PointEntry`.
- **Deviation:** completion months are created lazily through `IMonthSheetService`, so a solve in
  an older month can create a historical month sheet. Leaving sync/points totals on the dashboard
  and `/settings` as placeholders was intentional; Part 12 owns the ledger UI.
