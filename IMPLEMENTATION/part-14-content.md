# Part 14 — Content pages

**Depends on:** 04 · **Est. context:** ~40k (fully independent — safe to run early or late)

## Read
- PLAN §7.5 (309–313)
- PLAN §8.9 (392–396)
- PLAN §10.5 (583–587)
- PLAN §11 ContentPage (722–724)
- PLAN §13 content routes (859, 898–899)

## In scope
1. `ContentPage` entity + config + migration: unique `Slug`, `Summary`, `BodyMarkdown`,
   `ExternalUrl?`, `IsPublished`, `SortOrder`, timestamps + author fields.
2. `/content`: published pages ordered by `SortOrder` — title, summary, updated date.
3. `/content/{slug}`: Markdig render (raw HTML disabled, auto identifiers), generated TOC,
   Prism highlighting, copy buttons, optional "View original on GitHub" link.
4. `/board/content`: CRUD list + editor (title, slug auto-from-title but editable and uniqueness
   checked, summary, body, external URL, published toggle, sort order).
5. `POST /board/content/preview`: server-rendered Markdig HTML for live preview.
6. Delete requires confirmation; unpublish is the prominent default. Audit rows.
7. Image support from `wwwroot/content/{slug}/` (upload action, board only) plus external URLs.

## Out of scope
GitHub API, automatic import, comments, versioning history.

## Done when
A board member can author and publish a Markdown page in under a minute, and members see it with a
working TOC and highlighted code; unpublished pages 404 for members.

## Implementation notes (as built)

- **Files:** `Domain/Entities/ContentPage.cs`, `Data/Configurations/ContentPageConfiguration.cs`,
  migration `AddContentPages`, `Services/Content/ContentRules.cs`, `ContentPageService.cs`,
  `ContentImageService.cs`, `Controllers/ContentController.cs`,
  `Controllers/Board/BoardContentController.cs`, `ViewModels/Content/ContentViewModels.cs`,
  `Views/Content/Index.cshtml`, `Views/Content/Page.cshtml`, `Views/BoardContent/Index.cshtml`,
  `Views/BoardContent/Form.cshtml`, `wwwroot/js/content-editor.js`,
  `tests/OscCommittee.Tests/Services/Content/ContentRulesTests.cs`. DI and navigation:
  `Program.cs` (`IContentPageService`, `IContentImageService`), `AppDbContext`, `Views/_ViewImports.cshtml`,
  `Views/Shared/_Sidebar.cshtml` (Content no longer "Soon", board Content link),
  `Views/Home/Index.cshtml` (Content quick link active).
- **Schema:** unique `Slug` (max 80) plus a `(IsPublished, SortOrder)` index. Both author FKs point at
  `AspNetUsers`; `CreatedBy` is `SET NULL`, `UpdatedBy` is `NO ACTION` because two SET NULL paths make
  SQL Server reject the table (multiple cascade paths). Accounts are deactivated, never deleted.
- **Rules:** `ContentRules` is pure and unit-tested: slug generation (lowercase ASCII, single hyphens,
  80 max, `page` fallback), slug validation, external URL normalisation (absolute http(s) only, 500
  max), safe upload stems and magic-byte checks for PNG/JPEG/GIF/WebP.
- **Service:** `ContentPageService` validates and normalises once (`BuildEntityAsync`), enforces slug
  uniqueness in the query and treats a `DbUpdateException` as the same slug race, and writes
  `content_page.created` / `.updated` (before/after snapshot) / `.deleted`. `UpdatedByName` comes from
  a projected user lookup so the list and editor show the editor's name.
- **Member pages:** `/content` shows published pages ordered by sort then title, with summary and
  updated date. `/content/{slug}` 404s unless the page is published, renders through
  `IMarkdownService.ToContent` (advanced extensions, `DisableHtml`, automatic ids), shows the generated
  TOC when there are two or more headings, and labels the external link "View original on GitHub" when
  the host contains `github`. Prism and copy buttons come from the shared layout scripts.
- **Board editor:** `/board/content` lists every page with Draft/Published badges and edit/view/delete
  actions. The form covers title, slug, summary, body, external URL, published toggle and sort order;
  client-side `content-editor.js` fills the slug from the title until the board edits it and keeps the
  hidden image-form slug in sync. New pages default to draft; any invalid post redisplays with the
  entered values.
- **Preview:** `POST /board/content/preview` returns `{ ok, html, headings }` for the shared
  `task-editor.js` tab UI, so the editor gets the same Markdig output members see.
- **Images:** `ContentImageService` enforces 4 MB, the four extensions, file presence and magic bytes,
  deduplicates names (`name-2.png`), writes under `wwwroot/content/{slug}/`, and audits
  `content_page.image_uploaded`. The JSON `fetch` path returns the image URL; the plain-post fallback
  stores it, sets `TempData["Content.ImageUrl"]` and redirects back to the editor. Insert writes
  `![alt](url)` at the cursor; copy uses the clipboard API with a `document.execCommand` fallback.
- **Delete:** the confirmation modal explains that delete is permanent and that unpublishing is the
  softer option. Deleting removes the database row and audit row; the image folder is left on disk so a
  page recreated with the same slug keeps its pictures (cleanup is a manual/hardening concern).
- **Testing:** 35 new tests in `Services/Content/ContentRulesTests.cs` cover slug generation/validation,
  URL handling and image signatures. `dotnet test -c Release` is **239/239 green**. Authenticated smoke
  checks covered board list/new/create, edit (title, slug change, unpublish), preview JSON, image
  upload under `wwwroot/content/{slug}/`, published member list/page with TOC and GitHub label, draft
  404 and absence from the list, and delete; temporary rows were removed from LocalDB afterwards.
