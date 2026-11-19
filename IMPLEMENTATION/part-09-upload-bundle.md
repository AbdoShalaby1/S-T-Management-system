# Part 09 — Upload pipeline & Markdown bundle

**Depends on:** 08 · **Est. context:** ~65k

## Read
- PLAN §10.4 (565–581) — client + server pipeline
- PLAN §7.3 upload flow (292–302)
- PLAN §11 CSharpSubmission (680–704) — fields, indexes
- PLAN §22.2 (1288–1326) — bundle format
- PLAN §15 upload abuse / path traversal (1008–1010), §16 edge rows (1037–1039)
- PLAN §17 `Uploads` config (1079–1087)

## In scope
1. `CSharpSubmission` entity + config + migration (all fields, `rowversion`, indexes
   `(TaskId, UserId)`, `(UserId, SubmittedAtUtc)`, `(Status)`, unique `(TaskId, UserId, AttemptNo)`).
2. `wwwroot/js/upload.js`: drag-drop + `<input type="file" webkitdirectory multiple>`, client-side
   pre-checks mirroring server limits, progress bar.
3. `POST /tasks/{id}/submit` (multipart): size/count/per-file checks, path normalisation
   (`\`→`/`, strip drive/leading `/`, reject `..`, dedupe, depth ≤ 20, length ≤ 260), allowlist
   filter (`*.cs`, `appsettings*.json`, `*.csproj`, `*.sln`, `README*`), directory exclusions
   (`bin/ obj/ .git/ .vs/ node_modules/ .idea/`), require ≥1 `.cs`.
4. Storage under `App_Data/submissions/{userId}/{submissionId}/files/...` — generated paths only,
   never under `wwwroot`, never statically served.
5. `MarkdownBundleService`: 4-backtick fencing, path-ordered files, manifest header per §22.2,
   truncation markers `<!-- truncated: N chars omitted -->` (largest files first, cap
   `Ai:MaxInputChars`), counts for kept/ignored/chars/truncated.
6. Compute `IsLate` from the task deadline; `AttemptNo` per `(member, task)`; status `Uploaded`
   then `Grading` placeholder.
7. `/tasks/submissions/{id}` page: attempts table (attempt #, submitted, files, late, status,
   score, points, feedback placeholders) and a "grading in progress" state driven by a polling
   stub (`poll.js`) that will be completed in Part 10.
8. Rejection toasts with the exact copy tone from §14.7 for: no kept files, > 300 files, > 30 MB,
   > 2 MB single file, path escaping root.
9. Unit tests for the bundle builder and path normalisation (allowlist, exclusions, duplicates,
   truncation, fencing).

## Out of scope
Gemini, scoring, queue workers, board review, zip download.

## Done when
A real folder with `bin/obj` noise uploads, is filtered and stored, produces the exact bundle from
§22.2, and every rejection case returns the specified error code + toast.

## Implementation notes

- Paths that are too deep, too long, or illegal on Windows are **rejected** with a clear toast.
  Truncating silently could drop a code file, so the upload is refused and the member retries from
  a shallower folder.
- The folder picker's shared first segment is stripped before storage and bundling, so the tree in
  `bundle.md` starts at the project root.
- Bundle text is normalised to `\n` so character counts and truncation limits are identical on
  every OS. Files are sorted by path; the largest files are halved first when over the cap and each
  shortened file gets a `<!-- truncated: N chars omitted -->` marker.
- Duplicate paths keep the last copy (browser order), count inside `IgnoredFileCount`, and raise a
  separate warning toast alongside the success toast.
- Status stays `Uploaded` in this part; Part 10 moves it to `Grading` and then `Graded`/`Failed`.
- The upload POST is rate-limited per signed-in member (`uploads`, 10/hour) and uses the claim, not
  the campus IP, which is why authentication runs before the rate limiter.
- Deviations: `.slnx` covers `.sln` input through the same extension allowlist; `Uploads:AllowedFileNames`
  is `README*` so `README.md`, `README.txt` and a bare `README` all pass.
