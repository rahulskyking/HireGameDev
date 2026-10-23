# HireGameDev — Architecture & Scope Decisions (`DECISIONS.md`)

Primary living architecture docs, ADRs, and milestone roadmap live in **Notion (`HireGameDev (v2.0)`)**.
Per Build Blueprint v2.0 (Section 1, Rule 5), every intentional deviation or key choice from the blueprint is logged in one line below.

## Baseline Principles (from Blueprint v2.0 Section 13.F)
- **Hard filters before scoring:** Dealbreakers (work mode, engine gate, salary ceiling, notice period, availability) exclude pairs instead of hiding inside a percentage.
- **Evidence factor in skill score:** Backed (`0.90`) and verified (`1.00`) skills outweigh bare claims (`0.70`), preventing keyword stuffing.
- **Years-derived seniority:** Matching uses `years_experience`, not self-declared seniority labels.
- **Current CTC hidden by default:** Studios see `expected_ctc_lpa`; `current_ctc_lpa` stays hidden unless candidate opts in.
- **Contact details gated & no cold messages:** Phone and email release only at `shortlisted` or accepted invite.
- **Free launch first, access billing later:** Launch free through Phase 4; charge for access/capacity in Phase 6 after marketplace gates are met.
- **No free-tier AI for resumes:** PII is redacted before LLM calls, paid no-training tier only, and raw resume text is never persisted.

## Accepted Decisions & Blueprint Deviations
- **2026-10-23 (`ADR-001`):** Adopt exact C# / ASP.NET Core 8 + EF Core (`Npgsql`) + PostgreSQL 16 + SignalR + Hangfire + custom Neon Yellow & Obsidian CSS token stack.
- **2026-10-23 (`ADR-002` — Deviation from 3-project layout):** Split `HireGameDev.Core` into zero-dependency `HireGameDev.Domain` and EF-free `HireGameDev.Application` (4 projects: `Domain`, `Application`, `Infrastructure`, `Web`; no MediatR, no AutoMapper, no Generic Repository).
- **2026-10-23 (`ADR-003` — Deviation from Razor Pages):** Use **ASP.NET Core MVC** (`Controllers/`, `Views/`, `Models/`) instead of Razor Pages; controllers stay strictly thin and delegate all logic to `Application` handlers.
- **2026-10-23 (`ADR-004`):** Generate all EF Core migrations locally via `dotnet ef` (never hand-forge `ModelSnapshot.cs`), and store all enums as `snake_case` `text` via `HasConversion<string>()` with PostgreSQL `CHECK` constraints.
- **2026-10-23 (`ADR-005` — Deviation on test projects):** Start with zero test projects (`T1`) during Foundation/CRUD modules to keep velocity high and avoid mock/Docker friction; revisit pure math tests when building `MatchingEngine` in Phase 3.
- **2026-10-24 (`M0.2`):** Keep standard `IdentityUser<Guid>` columns intact on `users` for full `UserManager` store compatibility while storing real candidate phone numbers strictly on `candidate_profiles.phone` in Phase 1.
