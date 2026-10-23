# DECISIONS.md

One line per decision that deviates from, or extends, `docs/BLUEPRINT.md` (Build Blueprint v2.0). Newest at the top. If a decision changes a rule an earlier phase relies on, say so in the `Affects` column.

| ID | Date | Decision | Why | Affects |
| --- | --- | --- | --- | --- |
| D-017 | 2026-10-23 | Verification tiers T1 (real Postgres via PGlite) and T2 (fixture parity runner) run in this workspace; T3 (`dotnet build/test`) and T4 (Testcontainers) run on your machine or CI | No .NET SDK or NuGet access here, so C# cannot be compiled in-sandbox — everything that *can* be executed against a real engine is | all |
| D-016 | 2026-10-23 | Matching engine keeps `final_score_raw` (for ranking) alongside `final_score` (display, 1 decimal) | Component rounding (D-015) creates ties; ranking on the rounded value makes ordering unstable | Phase 3 |
| D-015 | 2026-10-23 | Matching: round each component (skill, experience, logistics) to 1 decimal **before** the weighted sum, then round the total | This is the only way the blueprint's own worked example reproduces as 73.7; rounding at the end yields 73.6 and fails the acceptance test (verified in `BLUEPRINT-FIXES.md` B-16) | Phase 3 |
| D-014 | 2026-10-23 | Plan docs live in `docs/` and the blueprint is copied verbatim to `docs/BLUEPRINT.md` as a frozen reference | Keeps "paste only the phase you're building" workable and makes the repo the source of truth instead of a chat log | all |
| D-013 | 2026-10-23 | DPDP purge/retention gets explicit settings keys and a nightly job (`retention.*`) | Blueprint states retention rules but had nothing enforcing them | Phase 0, 4 |
| D-012 | 2026-10-23 | `cities.metro_area` added; location score and F1 use metro area, not exact city id | Pune/NPC, Noida/Greater-Noida and MMR are the same labour market; exact-city matching mis-ranks most hybrid roles | Phase 1–3 |
| D-011 | 2026-10-23 | `match_scores` gains `expires_at`; nightly job prunes non-active jobs and sub-25-score rows | The candidate×job matrix otherwise grows without bound | Phase 3 |
| D-010 | 2026-10-23 | Salary spread validation and salary comparisons always run on normalized LPA via one `SalaryMath` helper | `pay_unit = monthly_inr` made the 1.5× rule reject sane internships and pass insane ranges | Phase 2, 3 |
| D-009 | 2026-10-23 | `ForwardedHeaders` middleware with `KnownProxies = [::1/127.0.0.1]` mandatory before any rate limiter is trusted | Behind Nginx every user looks like one IP — login lockout would have locked out the whole site | Phase 0 |
| D-008 | 2026-10-23 | Studio day budget = **distinct candidates viewed per studio per day**, shared across members | `UNIQUE(studio,candidate,date)` made the limit per-person-per-candidate, i.e. bypassable | Phase 4 |
| D-007 | 2026-10-23 | Candidate profile visibility becomes a 3-state enum (`discoverable`, `hidden_except_applications`, `hidden_from_named_studios`) with a `hidden_from_studios` join table | Employed candidates will not complete a discoverable profile without an escape hatch; single boolean is too coarse | Phase 1 |
| D-006 | 2026-10-23 | `expected_ctc_lpa`, `notice_period_days`, `availability` become nullable; the "required to apply" rule moves to the service layer | `NOT NULL` on a step-5 field is incompatible with a draft-saving wizard and with resume import | Phase 1 |
| D-005 | 2026-10-23 | TOTP MFA is **mandatory for admins** at Phase 0; passkeys/MFA for other roles stay deferred | Admins can read candidate PII and edit settings; that is where MFA pays today | Phase 0 |
| D-004 | 2026-10-23 | `passkeys` table and `user_sessions` created in Phase 0, with no user-facing UI | Adding an auth mechanism's schema after real users exist is a migration + support problem | Phase 0 |
| D-003 | 2026-10-23 | GitHub added as an external provider alongside Google; auto-link only on provider-verified emails, else explicit sign-in-then-link | User request; and GitHub emails are frequently non-deliverable noreply addresses, which is how duplicate accounts get created | Phase 0 |
| D-002 | 2026-10-23 | Anti-ghosting reminder and auto-decline clocks measured from `applied_at` with `first_response_at IS NULL`, not from `last_action_at` | Marking an application `viewed` refreshed `last_action_at`, so a studio could dodge reminders and auto-decline forever without responding | Phase 3 |
| D-001 | 2026-10-23 | `applications` loses `UNIQUE(job_id, candidate_id)` in favour of `UNIQUE … WHERE status NOT IN ('withdrawn','auto_declined')` + `attempt smallint`; same treatment for `invites` | The hard unique index contradicted the blueprint's own "withdrawn applications can be re-submitted once after 7 days" rule | Phase 2, 3, 5 |

## Still open (needs a human answer)

- **Implementation stack.** The blueprint specifies ASP.NET Core 8 + Razor Pages + EF Core. That choice is preserved until you say otherwise; it has consequences for how work is verified (see `PLAN.md` §0 and the note in this repo's README about the current sandbox).
- **Local development database target** (real Postgres 16 in Docker on your machine vs an in-process Postgres for tests).
- **Deploy environment**: VPS host/domain availability, and whether email + SMS provider keys exist yet, which decides whether Phase 0's deploy step is real or simulated.
