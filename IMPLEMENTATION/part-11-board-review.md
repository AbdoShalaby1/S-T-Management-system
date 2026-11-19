# Part 11 — Board review & submission admin (Phase 5, first half)

**Depends on:** 10 · **Est. context:** ~60k

## Read
- PLAN §8.7 (372–384) — three-column review
- PLAN §8.6 task detail (370)
- PLAN §6.7 (246–257) — override semantics
- PLAN §14.6 board review screen (973–979)
- PLAN §11 CSharpSubmission (680–704), §13 board submission routes (890–895)
- PLAN §16 rowversion conflict (1043)

## In scope
1. `/board/tasks/{id}/submissions`: all attempts (member, attempt, submitted, late, score, status),
   sortable/filterable, best-attempt highlight, links to review.
2. `/board/submissions/{id}` three-column review:
   left = statement + reference solution + rubric notes;
   centre = file tree + tabbed tree browser over the **stored files** with Prism highlighting and
   copy buttons;
   right = AI score, original `AiScore`, feedback (summary/strengths/issues), model + prompt hash,
   1–5 stepper, **Save**.
3. Score save: writes `Score`, `IsManualScore = true`, recomputes the creditable attempt and its
   point entry immediately, audit row, `rowversion` conflict → friendly "someone else edited this,
   reload" toast.
4. Overriding a late submission's score awards points with an explicit UI warning.
5. **Retry grade** (board only): resets `RetryCount`, re-enqueues, forces re-grade even for an
   identical bundle.
6. `GET /board/submissions/{id}/download`: authenticated zip of the stored folder.
7. Secure file reader used by the review page — path confined to the submission root, no
   traversal, member can never reach another member's files.

## Out of scope
Bonus/adjustment ledger, preset recalculation, standings.

## Done when
The board can correct any grade and the ledger stays explainable; a member cannot read another
member's code through any route; the review page renders real stored files.

## Implementation notes (as built)

- **Files:** `Services/Tasks/BoardSubmissionService.cs` (detail, score, retry, best-attempt
  list), `Services/Tasks/SubmissionFileService.cs` (secure tree reader + zip), rewritten
  `Services/Grading/CSharpCreditService.cs`, `Controllers/Board/SubmissionsController.cs`,
  `Controllers/Board/BoardTasksController.Submissions`, `ViewModels/Board/SubmissionReviewViewModel.cs`,
  `Views/Submissions/Review.cshtml`, `Views/BoardTasks/Submissions.cshtml`.
- **Routes:** GET/POST `/board/submissions/{id}` and `/{id}/score|retry|download`. The task
  list action is `GET /board/tasks/{id}/submissions`.
- **Manual scores are authoritative.** `CSharpCreditRules.EffectivePoints` credits a board score even
  on a late attempt; an automatic late grade still resolves to 0. `ApplyBoardEditAsync` recomputes
  the task's ledger row immediately. Automatic grading calls `ApplyAsync`, which skips
  `IsManualOverride` rows.
- **Concurrency:** the hidden rowversion is base64 and compared with
  `CryptographicOperations.FixedTimeEquals`; `DbUpdateConcurrencyException` becomes a friendly
  "reload and try again" toast rather than a 500.
- **Files:** the review page navigates with `?file=` (server-side selection, no client state), caps
  display at 120 000 characters, and shows a hint when truncated. The zip contains only `files/`,
  not the generated `bundle.md`.
- **Testing:** interaction testing stayed light per the session guidance - authenticated GETs for the
  list, review, zip and related pages, plus build/test. `dotnet test -c Release` is 156/156 green.
