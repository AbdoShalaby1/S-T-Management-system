# Implementation parts — index

`PLAN.md` (1361 lines, ~69 KB ≈ **18–20k tokens**) is the single source of truth.
It fits in a 200k context on its own, so the split below is **not** about the plan text —
it is about how much *work* (codebase exploration + files written + build/test output) one
session should carry so a part finishes well inside 200k.

**Session budget used for the split:** target ≤ 60k tokens of context per part, hard ceiling
120k. Every part is a self-contained, demoable slice that leaves the repo building and green.

---

## Rules that apply to every part

1. Read `00-CONVENTIONS.md` first, then the plan line ranges listed in the part file.
   Reading all of `PLAN.md` is cheap — do it if unsure.
2. Build with `dotnet build` at the end; the repo must compile and migrations must apply.
3. Do not implement anything outside the part's "In scope" list. If a dependency is missing,
   stop and report it instead of building it.
4. End the part with: build clean, `dotnet test` green (once tests exist), a one-paragraph
   summary of what was added, and the list of files created/modified.
5. No secrets, no demo data, no `TODO`-stubs left in the shipped path.

---

## Parts

| # | Part | Plan phase | Depends on | Primary scope | Est. context |
|---|---|---|---|---|---|
| 01 | [Foundation & design system](part-01-foundation.md) ✅ | 0 | — | Scaffold, Tailwind, layout, toasts, Serilog, EF, health | ~45k |
| 02 | [Auth & accounts](part-02-auth-accounts.md) ✅ | 1 | 01 | Identity, policies, seed board, register/login/pending | ~55k |
| 03 | [Approvals & members admin](part-03-approvals-members.md) ✅ | 1 | 02 | Approve/reject, member table, add board, deactivate/restore | ~45k |
| 04 | [Profile, months & audit](part-04-profile-months-audit.md) ✅ | 1–2 | 02 | Settings page, month sheets, lazy create, close/open, audit log | ~50k |
| 05 | [DS sheets (board + member)](part-05-ds-sheets.md) ✅ | 2 | 04 | DS sheet CRUD, active/hidden cap, member browse | ~50k |
| 06 | [DS problems & metadata](part-06-ds-problems.md) ✅ | 2 | 05 | URL import, LC/CF metadata clients, presets, points materialisation | ~65k |
| 07 | [DS sync & DS points](part-07-ds-sync.md) ✅ | 3 | 06 | Sync engine, dedupe, cutoff, late, manual credit | ~75k |
| 08 | [C# tasks (board + member)](part-08-csharp-tasks.md) ✅ | 2-4 | 04 | Task CRUD, statement rendering, member task list/detail | ~45k |
| 09 | [Upload pipeline & bundle](part-09-upload-bundle.md) ✅ | 4 | 08 | Multipart folder upload, validation, storage, Markdown bundle | ~65k |
| 10 | [Gemini grading](part-10-grading.md) ✅ | 4 | 09 | Gemini client, prompt builder, queue/worker, polling UI | ~70k |
| 11 | [Board review & submission admin](part-11-board-review.md) ✅ | 5 | 10 | 3-column review, score override, retry, zip, delete | ~60k |
| 12 | [Points admin & presets](part-12-points-admin.md) ✅ | 5 | 07, 11 | Bonus/adjustment, preset recalc, ledger views | ~50k |
| 13 | [Standings](part-13-standings.md) ✅ | 6 | 07, 11, 12 | 3 tabs, summary/detail, tie-breakers, month selector | ~55k |
| 14 | [Content pages](part-14-content.md) ✅ | 2 | 04 | Member content pages, board CRUD, preview, Prism | ~40k |
| 15 | [Hardening & handover](part-15-hardening.md) | 7 | all | Tests, headers/CSP, empty states, mobile, perf, README | ~90k |

Part 14 can move ahead of the remaining parts; nothing else may move before its dependency.

### Suggested execution order
`01 → 02 → 03 → 04 → 05 → 06 → 08 → 09 → 10 → 07 → 11 → 12 → 13 → 14 → 15`
(08–10 can start once 04 lands if you prefer to get the AI path working early; 07 only needs 06.)

---

## Session prompt template

Paste this into a fresh session for each part:

```text
Work on part NN only.

1. Read IMPLEMENTATION/00-CONVENTIONS.md
2. Read IMPLEMENTATION/part-NN-<name>.md
3. Read the PLAN.md sections listed in that part file (read the whole file if you need context)
4. Inspect the existing repo before writing anything
5. Implement the part's in-scope items, nothing else
6. Build, fix warnings, and report the file list + summary

Do not start part NN+1.
```
