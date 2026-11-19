# Part 01 — Foundation & design system (Phase 0)

**Status:** ✅ complete (2026-11-18) · **Depends on:** nothing · **Est. context:** ~45k

## Read
- PLAN §1–§2 (lines 36–81) — overall intent
- PLAN §12 (753–834) — layout, patterns, packages
- PLAN §14 (907–994) — design tokens, typography, layout, components, toasts
- PLAN §17 (1052–1105) — config skeleton
- PLAN §19 Phase 0 (1158–1161), §18 DoD (1148–1150), §22.5 (1346–1352)

## In scope
1. `OscCommittee.sln`, `src/OscCommittee.Web`, `tests/OscCommittee.Tests` with the exact folder
   skeleton from §12.1 (empty folders are fine, no placeholder classes).
2. `Program.cs`: DI, Serilog, antiforgery, static files, error handling, `TimeProvider`.
3. `appsettings.json` / `.Development.json` exactly per §17 (empty AI model, empty seed board).
   `appsettings.Development.json` gitignored value handling noted in README stub.
4. Tailwind v4: `package.json` scripts `build:css`, `watch:css`, `copy:vendor`;
   `Styles/site.css` with the full `@theme` token set from §14.1.
5. Layout: desktop 248px sidebar, top bar (month chip, points chip, avatar menu), mobile drawer,
   max width 1200px. Nav items rendered but linking to `#` until their parts land.
6. Shared partials: `_Layout`, `_ToastHost`, `_Sidebar`, `_TopBar`, `_EmptyState`, `_Pagination`.
7. Toast system: `wwwroot/js/toast.js` + TempData bridge in the layout (§14.5).
8. `confirm.js` dialog helper, `code.js` stub, `wwwroot/img/osc-logo.svg` fallback mark `OSC · S&T`.
9. Error pages (404/500, no stack traces), `/health` endpoint.
10. `AppDbContext` with no entities yet + initial empty migration + auto-migrate on startup in Development.
11. `App_Data/{submissions,logs}` created on startup, gitignored.

## Out of scope
Identity, any entity, any controller other than `HomeController` + `HealthController`,
any integration client, Markdig wiring.

## Done when
App runs, a styled empty dashboard renders with sidebar/top bar, a server action raises a toast,
confirm dialog works, `/health` returns OK, and `dotnet ef database update` applies cleanly
to a fresh LocalDB.

## Built — notes for the next part

Verified: build clean in Release (warnings as errors), 4 tests green, migration applied to
LocalDB, `/` 200, `/health` 200 JSON, `/error/404` 404 with friendly page, toast raised from a
server action via TempData, confirm dialog opens, no console errors, no vulnerable packages.

Deviations and decisions the next part needs to know:

- Solution file is `OscCommittee.slnx` (.NET 10's default format), not `.sln`.
- `System.Security.Cryptography.Pkcs` 11.0.0 is pinned in the web project to clear a transitive
  high-severity advisory from EF Core Design. Revisit when EF ships an updated dependency.
- Tailwind is invoked through `node ./node_modules/@tailwindcss/cli/dist/index.mjs`, because the
  npm `.bin` shim breaks on this repo path (`&` in `S&T MVC`). Keep that invocation in scripts.
- Tailwind v4 needs `@source` for Razor views, and custom classes used inside `@apply` must be
  declared with `@utility` (`nums` is the example).
- `/health` is a `HealthController` (JSON, 503 when unhealthy), not `MapHealthChecks`.
- The logo is rendered only when `wwwroot/img/osc-logo.svg` exists (`IWebHostEnvironment`
  file provider); otherwise the `OSC` text mark renders. Drop the SVG in and it appears.
- `HomeController.DemoToast` and the dashboard's "Development checks" panel exist only in
  Development; they are the toast/confirm proof and should be deleted in Part 15.
- Part 02 will replace the `Guest` chip and the disabled nav entries; `_Sidebar` already hides
  the Board group behind `User.IsInRole("Board")`.
- `TimeZoneServiceTests` locks the `TimeProvider` + `ITimeZoneService` pattern every later part uses.

