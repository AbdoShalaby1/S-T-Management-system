# Part 03 — Approvals & members admin (Phase 1, second half)

**Status:** ✅ complete (2026-11-18) · **Depends on:** 02 · **Est. context:** ~45k

## Read
- PLAN §5 Approval (149–154), Board accounts (156–161), Deactivation (163–167)
- PLAN §8.1 (324–328), §8.2 (330–333), §8.3 (335–341)
- PLAN §4 (100–121), §13 board routes (861–872)
- PLAN §16 relevant rows (1024–1048)

## In scope
1. `/board` dashboard shell: counters (pending approvals, active members, open month placeholder),
   quick actions, recent activity placeholder.
2. `/board/approvals`: queue of `Pending` accounts (name, email, requested date, handles if set),
   approve / reject with optional private note, reject requires confirmation. No bulk approve.
3. `/board/members`: table (name, email, status, handles, current-month points placeholder, joined,
   last sync placeholder), filters (status, handle missing), sort by points placeholder.
4. `/board/members/new-board`: create Board account (email, display name, password typed or
   generated-once-and-shown), policy enforced, `Status = Active`.
5. Deactivate / restore with confirm dialog explaining history is kept. Deactivation stamps
   `DeactivatedAtUtc` / `DeactivatedByUserId` and rotates the security stamp (kills cookies).
6. Self-protection guards: a board account cannot demote or delete itself.
7. Pending-count badge in the sidebar for board users.

## Out of scope
Audit log persistence (Part 04 — but call the audit interface if it exists, otherwise emit a
Serilog event and note it), member month-by-month detail page, point adjustment.

## Done when
Full §5 lifecycle is demonstrable end to end (register → approve/reject → deactivate → restore),
unauthorized access is blocked, and a Board user can create another Board account.

---

## Built — notes for the next part

Verified end to end against LocalDB: a second registration appears in the queue (with the sidebar
badge); approve flips the account to Active and the member's existing cookie reaches the dashboard
on the next request; reject stores the private note and the member gets "your request was not
approved"; deactivate drops the member's access and their old cookie stops working; restore lets
them sign in again; a board account created with a generated password shows the password exactly
once and can sign in; the same flow with a typed password shows no banner; duplicate emails and
weak typed passwords are rejected; a member hitting `/board/members` gets the 403 page; posting a
deactivation for your own id returns "You cannot deactivate your own account."

What exists now:

- `Services/Accounts/IMemberAdminService` + `MemberAdminService` — counts, pending queue, member
  list with filter/sort, approve, reject, deactivate, restore. Registered
  scoped in `Program.cs`.
- `Controllers/Board/BoardControllerBase` — `[Authorize(Policy = "Board")]`, `CurrentUserId`,
  `Toast(Result)`, and the `MemberSummary` → view-model mapping used by all three pages.
- `Controllers/Board/BoardController` (dashboard), `ApprovalsController`, `MembersController`.
- `ViewModels/Board/`: `BoardDashboardViewModel`, `ApprovalsViewModel`, `ApprovalRowViewModel`,
  `MembersViewModel`, `MemberRowViewModel`, `UserStatusView`
  (status label + badge class + initials).
- Views: `Views/Board/Index.cshtml` (dashboard), `Views/Approvals/Index.cshtml`,
  `Views/Members/Index.cshtml`.
- `_Sidebar` injects `IMemberAdminService` and puts the pending count on the Approvals item; the
  Approvals and Members entries are live, Months and Audit still say Soon.

Decisions and gotchas for Part 04:

- **View folders follow the controller name.** `/board` lives at `Views/Board/Index.cshtml`, but
  the sub-controllers are `Approvals` and `Members`, so their views are `Views/Approvals/` and
  `Views/Members/`. Keep that convention for Months, DS, Tasks and Audit, or they will 500.
- **Audit is not persisted yet.** Every state change calls a private `Audit(...)` in
  `MemberAdminService` that emits `Board.Audit {Action} by {BoardUserId} on {TargetUserId}
  ({TargetName}) note={Note}` through Serilog. Part 04 can replace that method body with the real
  `IAuditService` write; the call sites stay.
- **Approve deliberately does not rotate the security stamp.** The member's cookie still carries a
  `Pending` claim, and `AccountStateMiddleware` re-reads the database for any non-Active claim, so
  they get in on their next request. Reject and deactivate *do* rotate it, which signs the person
  out on the next stamp validation (5-minute interval).
- Consequence: a deactivated member holding an `Active` cookie hits the `Member` policy before the
  stamp check and sees the 403 page rather than a redirect to login. That is the correct outcome;
  do not "fix" it by widening the middleware's fast path.
- **Self-protection** is enforced in the service (`user_id == board_user_id` → fail) and the UI
  hides the button (`MemberRowViewModel.IsSelf` renders "This is you"). There is also a
  last-active-board guard, but it is unreachable defence-in-depth: the actor must itself be an
  active board account, so the count is always ≥ 2 when targeting somebody else.
- Board accounts are created by seeding only; there is no in-app "add board member" flow. The
  generated-password `TempData` handoff and its warning panel were removed with it.
- Board accounts are excluded from standings/grading by rule, not by flag — nothing marks them yet,
  so later parts must filter on the `Board` role rather than on a column.
- `MembersViewModel` points and last-sync columns are hard-coded `—` placeholders; `GetCountsAsync`
  already returns the population counts independently of the active filter.
- The sidebar's pending count is a query per board page render. Fine at committee scale; revisit if
  it ever shows up in traces.
