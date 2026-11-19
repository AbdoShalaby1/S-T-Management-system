# Part 02 — Auth & accounts (Phase 1, first half)

**Status:** ✅ complete (2026-11-18) · **Depends on:** 01 · **Est. context:** ~55k

## Read
- PLAN §4 (100–121) — role/permission matrix
- PLAN §5 (125–154) — account states, registration, approval semantics
- PLAN §11 Identity (595–600) — `ApplicationUser` fields
- PLAN §13 member auth routes (842–848), §15 Security (998–1018)
- PLAN §19 Phase 1 (1163–1166)

## In scope
1. `ApplicationUser : IdentityUser<Guid>` with every field from §11, `UserStatus` enum
   (`Pending, Active, Rejected, Deactivated`).
2. Identity wiring: roles `Board` / `Member`, PBKDF2 defaults, cookie per §15
   (HttpOnly, SameSite=Lax, Secure in prod, 14-day sliding, `SecurityStamp` validation).
3. Policies: `Member` = authenticated **and** `Status == Active` on every request;
   `Board` = role `Board`. Unauthorized → `/account/login`, forbidden → 403 page.
4. Board seeder from `Seed:Board` — idempotent, runs when no Board user exists, never overwrites.
5. `/account/register` (+ validation: min 8 chars, letter + digit, confirm, case-insensitive
   email uniqueness), `/account/login`, `/account/logout`, `/account/pending`.
6. Pending/Rejected/Deactivated behaviour exactly per §5 States table; generic invalid-credentials
   message for deactivated and unknown accounts; no account enumeration on login.
7. EF `Configurations` for `ApplicationUser`, migration, unique email index.
8. Rate limiting: login 10/15 min per IP, register 5/hour per IP.
9. Redirect rules: authenticated Active → `/`, Pending → `/account/pending`, Board → `/board` (stub page).

## Out of scope
Approval queue UI, member table, add-board form, deactivate/restore, profile settings,
handle fields/verification, month sheets.

## Done when
Register → Pending screen; seeded board can log in; a Pending user is blocked from member routes;
a Deactivated user gets the generic invalid-credentials message; rate limits return 429.

## Built — notes for the next part

Verified end to end on LocalDB: registration lands on `/account/pending`; a pending user is
redirected back to that screen from `/` and `/board`; the seeded board account (email matched
case-insensitively) lands on `/board`; `deactivated` and `unknown` both return the generic
invalid-credentials copy while `Rejected` returns "your request was not approved"; the 10th login
attempt inside the window returns 429 with the friendly error page; an account flipped to Active
in the database reaches the dashboard immediately.

What exists now:

- `Domain/Entities/ApplicationUser` (`IdentityUser<Guid>`, every §11 field) and
  `Domain/Enums/UserStatus` (Pending/Active/Rejected/Deactivated).
- `AppDbContext` derives from `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`.
  `ApplicationUserConfiguration` adds the display-name limit, status index, and makes Identity's
  `EmailIndex` unique (filtered on non-null) so email uniqueness is case-insensitive.
- `Services/Accounts`: `AccountService` (+ `IAccountService`), `BoardAccountSeeder`
  (+ `IBoardAccountSeeder`), `SeedOptions`, `Roles`, `AppUserClaimsPrincipalFactory`
  (`display_name` + `account_status` claims), `ActiveUserRequirement` +
  `ActiveUserAuthorizationHandler`, `AccountStateMiddleware`.
- `Services/Common/Result` / `Result<T>` — the expected-failure carrier every later part uses.
- `Policies`: `Member` (authenticated + Active, checked per request) and `Board` (same + role).
  `HomeController` uses `Member`; `Controllers/Board/BoardController` uses `Board`.
- Views: `_AuthLayout`, `Account/Login`, `Account/Register`, `Account/Pending`, `Board/Index`
  (stub), updated `_TopBar` (real name, initials, board badge, POST sign-out) and `_Sidebar`
  (Board home live, phase label).
- `AddIdentity` migration applied; `dotnet test` still 4/4.

Decisions and gotchas for Part 03:

- Sign-in validates credentials in `AccountService` (`CheckPasswordAsync`) and then lets the
  controller call `SignInManager.SignInAsync`. Identity's lockout counter is therefore unused;
  brute force is bounded by the per-IP rate limit instead. Revisit if board tooling needs lockout.
- `AccountStateMiddleware` runs after `UseAuthentication`. It trusts the `account_status` claim as
  a fast path for Active users; for any other claim it re-reads the user, redirects Pending to
  `/account/pending`, and signs out Rejected/Deactivated cookies. Approve/deactivate in Part 03
  should call `SignInManager.RefreshSignInAsync` or update the security stamp so the cookie claims
  refresh sooner than the 5-minute stamp interval.
- The seeded board account is created only when no user has the `Board` role. Empty `Seed:Board`
  logs a warning and starts normally (so a fresh clone still boots).
- `appsettings.json` stays placeholder-only; verification used `Seed__Board__*` env vars.
- `HomeController.DemoToast` + the dev panel survive (Development only, removed in Part 15) and are
  now behind the `Member` policy.
- A 429 branch was added to `ErrorController`; rate limiter rejection re-executes through
  `UseStatusCodePagesWithReExecute`.
- Dev Serilog now suppresses EF Core Debug logs (`Microsoft.EntityFrameworkCore` → Warning) to
  keep startup output readable.
- Test accounts used during verification were deleted; the dev database holds only the seeded
  board account.

