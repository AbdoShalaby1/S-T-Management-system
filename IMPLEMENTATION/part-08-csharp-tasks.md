# Part 08 — C# tasks (board + member)

**Depends on:** 04 · **Est. context:** ~45k

## Read
- PLAN §7.3 task page + list (289–292)
- PLAN §8.6 (366–370)
- PLAN §11 CSharpTask (662–678)
- PLAN §10.5 (583–587) — Markdown rendering, Prism, TOC
- PLAN §13 `/tasks`, `/board/tasks` routes (853–857, 886–889)
- PLAN §6.2 (187–199) — one-active cap for tasks

## In scope
1. `CSharpTask` entity + config + migration: FK `MonthSheet`, `ReferenceSolution` (nvarchar max) +
   filename, `RubricNotes`, `ShowReferenceToMembers` (default false), required future `DueAtUtc`,
   `IsActive`/`IsHidden`/`SortOrder`, filtered unique `MonthSheetId` where `IsActive = 1`.
2. Board task editor: all fields above, reference solution as code block **or** file upload,
   Markdown preview, hidden/active toggles with the same single-active confirm flow as DS sheets.
3. Member `/tasks`: list with deadline, points (max 5), my best score placeholder, state
   (Available / Submitted / Graded / Late / Grading — placeholders until Parts 09–10).
4. Member `/tasks/{id}`: statement rendered via `IMarkdownService` with TOC, deadline countdown,
   reference-solution block honouring `ShowReferenceToMembers`, attempts table placeholder.
5. Prism wiring (`wwwroot/lib/prism`, C#/JSON/Markdown/Bash) + copy button + line numbers.
6. Board task detail shell with an attempts placeholder.

## Out of scope
Upload, storage, bundle, Gemini, grading, scores, review, retry.

## Done when
Board can publish a complete task with a reference solution; members see the rendered statement,
correct deadline, and reference visibility; only one task can be active per month.

---

## Implementation notes (shipped)

- **Entity/indexes:** `CSharpTasks` with `IX_CSharpTasks_MonthSheetId_SortOrder` and the filtered
  unique `IX_CSharpTasks_OneActivePerMonth` (`[IsActive] = 1`). `ReferenceSolution` is
  `nvarchar(max)` text plus a leaf file name; `RubricNotes` is board-only Markdown.
- **Service:** `CSharpTaskService` mirrors `DsSheetService` — hidden implies inactive, activation
  swaps the month's active task with a direct `UPDATE`, and every mutation writes
  `csharp_task.created|updated|activated|deactivated|hidden|unhidden` to the audit log.
- **Board UI:** `/board/tasks` (stat tiles + table), `/board/tasks/new`, `/board/tasks/{id}/edit`
  and `/board/tasks/{id}`. The editor is multipart so the reference solution can be pasted **or**
  uploaded; text extensions only, 256 KB cap, a file wins over pasted text. Statements get a
  Write/Preview tab that posts to `/board/tasks/preview` and renders through `IMarkdownService`.
- **Member UI:** `/tasks` pins the active task and groups the rest by month; `/tasks/{id}` shows the
  statement with a TOC, the deadline countdown, the 5-point maximum, and the attempts table shell.
  Reference solutions only render when `ShowReferenceToMembers` is on.
- **Markdown:** `IMarkdownService.ToContent` adds heading extraction (level, text, auto id) on top of
  the existing pipeline; `MarkdownServiceTests` covers TOC ids, inline markup stripping, raw HTML
  and empty input.
- **Prism:** self-hosted Prism (core + C#/JSON/Markdown/Bash) and the line-numbers plugin load from
  the layout; `prism-setup.js` wraps every Markdown fence in the same `.code-shell` header as the
  shared `_CodeBlock` partial, adds line numbers and a copy button, then highlights.
- **States are placeholders:** no submissions exist yet, so member state is Available/Late and the
  best score renders `—`. Part 09 stores attempts; Part 10 fills scores and feedback.
