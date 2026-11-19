# Shared conventions (read first, every session)

Derived from `PLAN.md` §12 and §14. These are binding for every part.

## Stack
- ASP.NET Core MVC, **.NET 10**, `net10.0`, nullable enabled, warnings as errors in Release.
- SQL Server via LocalDB in dev; EF Core 10 with migrations committed.
- Tailwind CSS v4 (`@tailwindcss/cli`, `Styles/site.css` → `wwwroot/css/site.css`), built assets committed.
- FontAwesome + Prism self-hosted from npm into `wwwroot/lib/`. **No CDN at runtime.**
- Serilog console + rolling file. Markdig for Markdown. xunit + NSubstitute + `WebApplicationFactory` + SQLite for tests.

## Solution layout (do not invent new top-level folders)
```
src/OscCommittee.Web/
  Domain/Entities, Domain/Enums
  Data/ (AppDbContext, Configurations, Migrations, Seed)
  Services/{Accounts,Points,Months,Standings,Ds,Tasks,Grading,Content,Settings,Audit,Common}
  Integrations/{LeetCode,Codeforces,Gemini}
  Controllers/ (Board/ subfolder for admin)
  ViewModels/<Area>/
  Views/<Area>/, Views/Shared/
  wwwroot/{css,js,lib,img}
  App_Data/{submissions,logs}   (gitignored)
tests/OscCommittee.Tests/
```

## Application patterns (hard rules)
- MVC controllers + Razor views only. No Razor Pages, no SPA framework, no jQuery, no Bootstrap.
- **ViewModels only** — entities never reach a view.
- Every service and HTTP client behind an interface, registered in DI. `IHttpClientFactory` with named clients `leetcode`, `codeforces`, `gemini`.
- `TimeProvider` injected everywhere — never `DateTimeOffset.UtcNow` directly in a service.
- `Result` / `Result<T>` with `ErrorCode` + `Message` for expected failures; controllers turn these into TempData toasts + redirects. Exceptions are bugs only.
- No repository layer — services query `AppDbContext` directly. All query logic lives in services.
- Authorization policies: `Member` (Active status, checked **every** request) and `Board`. Resource checks (own submission only) done in the service or controller.
- Background work: `Channel<Guid>` + `BackgroundService`. Single-server assumption.
- Concurrency: `rowversion` on `CSharpSubmission`, `MonthSheet`, `PointEntry`.
- All timestamps stored as `DateTimeOffset` in UTC; displayed in `App:TimeZone`.
- Ids: `Guid.CreateVersion7()`.
- No `Html.Raw` on user content. Markdig with `DisableHtml`.

## UI rules
- Dark only, English only, no theme toggle.
- Use the `@theme` tokens from PLAN §14.1 exclusively — no hard-coded hex in views.
- Orange focus ring on every interactive element, 44px min touch target on mobile.
- Toasts via `TempData["Toast.Success"|.Error|.Warning]` consumed by the layout; JSON endpoints return `{ ok, message, data }`.
- Copy tone: concise, second person, no exclamation marks, no shaming on late work.
- Empty states and loading skeletons for every list page.

## Data rules
- Soft delete (deactivate) for people; hard delete only where the plan says so (attempts, with audit).
- Every privileged mutation writes an `AuditLog` row (action, entity, before/after JSON).
- Nothing in `App_Data` is served statically.
- Point math always: `FinalPoints` is what totals sum, `BasePoints` preserves the computed value.

## Per-part exit checklist
- [ ] `dotnet build -c Release` clean (warnings as errors)
- [ ] migrations apply to a fresh LocalDB database
- [ ] the part's "Done when" items demonstrable
- [ ] no secret, key, or connection string in code, logs, or views
- [ ] file list + summary reported, part scope not exceeded
