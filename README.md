# HireGameDev

A hiring platform for game developers in India. Candidate and studio profiles, structured jobs, explainable matching, and anti-ghosting pressure on both sides.

## Where the plan lives

| Document | Read it when |
| --- | --- |
| [`docs/PLAN.md`](docs/PLAN.md) | **Start here.** Module map (M0–M8), Module 0 broken into 14 steps with acceptance criteria, provider-linking rules, ground rules |
| [`docs/BLUEPRINT.md`](docs/BLUEPRINT.md) | Frozen reference: Build Blueprint v2.0 — full schema, algorithms, field design, phase plans. Do not edit; deviations go to `DECISIONS.md` |
| [`docs/BLUEPRINT-FIXES.md`](docs/BLUEPRINT-FIXES.md) | 17 real problems found in the blueprint before building (constraint conflicts, a loophole in anti-ghosting, a rounding bug in the golden test). Fix these in the schema, not in the UI |
| [`docs/IMPROVEMENTS.md`](docs/IMPROVEMENTS.md) | Nice-to-have features per phase, with my recommendation on what to skip |
| [`docs/DESIGN.md`](docs/DESIGN.md) | "Volt & Obsidian" design system: tokens, themes, components, accessibility gates |
| [`docs/VERIFICATION.md`](docs/VERIFICATION.md) | What gets checked automatically per step, and the environment limits |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Every deviation from the blueprint, one line each. Append as you go |

## Stack

ASP.NET Core 8 Razor Pages · EF Core (Npgsql) · PostgreSQL 16 · SignalR · Hangfire · Bootstrap 5 + custom CSS. No JS frameworks. See `docs/PLAN.md` §0 and the verification note below.

## Working agreement

One module at a time. One step ≈ one commit. A step is not done until it is verified as far as this environment allows, and no step starts on a red build.

## ⚠️ Sandbox note

This repository is currently being driven from an Arena workspace that can install Node and Python packages but **not** the .NET SDK (`dotnet.microsoft.com` and `api.nuget.org` are unreachable). Consequence: C# here is authored and reviewed, but not compiled — while all SQL, indexes, constraints and the matching algorithm are executed against a real PostgreSQL engine in-sandbox and asserted against fixtures. Read [`docs/VERIFICATION.md`](docs/VERIFICATION.md) before accepting any step as "done".
