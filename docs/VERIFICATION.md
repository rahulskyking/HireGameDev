# Verification strategy — what is checked automatically, and where the limits are

## The constraint you should know about

The sandbox this plan is being built in can install Node, Python and npm packages, and has network access only to `github.com`, `codeload.github.com`, `api.github.com`, `registry.npmjs.org`, `pypi.org`, `files.pythonhosted.org`.

That means, concretely:

| Can do | Cannot do |
| --- | --- |
| Read and write the full C# / Razor / EF Core solution | **`dotnet build`, `dotnet test`, `dotnet ef migrations add`** — no .NET SDK, and `dotnet.microsoft.com`, `api.nuget.org`, `packages.microsoft.com` are all unreachable, so there is no way to install the SDK or restore NuGet packages |
| Run a **real PostgreSQL 18** server in-process (PGlite, WASM build of Postgres) and execute the blueprint's actual SQL: `citext`, `pg_trgm`, `tsvector` + `websearch_to_tsquery`, generated `STORED` columns, `text[]`, JSONB, partial and GIN indexes, `CHECK` constraints, `gen_random_uuid()` | Reach Resend, Gemini, IGDB, Razorpay, SMS gateways, or the target VPS |
| Run Node-based checks: typecheck/lint if we add them, algorithm harnesses, SQL migration dry-runs, seed imports | Docker/Testcontainers, so integration tests that need a *server* run on your machine |
| Push commits and open PRs to GitHub | See the site rendered from a Kestrel process |

So ASP.NET code written here is **authored carefully and reviewed by eye, but never compiled here**. Anything that can be verified against a real engine — SQL, the matching algorithm, salary math, completeness scoring, evidence rules, lifecycle state machines — gets verified against real PostgreSQL and real fixtures, in this sandbox, before we call it done.

## Four-tier verification

1. **T1 — SQL ground truth (runs here, every step).** Every migration's generated SQL and every nontrivial query is executed against a real Postgres (PGlite) from a small Node harness in `dev/dbcheck/`. Seeds, indexes, `CHECK` constraints, generated columns, FTS ranking, trigram fallback and `EXPLAIN` plans are asserted. This catches the class of bug EF migrations and Postgres-only features are most likely to hide.
2. **T2 — Algorithm parity (runs here, every algorithm change).** The matching engine, completeness, evidence levels, salary normalization, anti-ghosting and entitlement rules are specified once as data: `tests/fixtures/*.json` (candidate snapshot + job snapshot + expected output). A JS reference runner in `dev/spec/` executes those fixtures; the C# xUnit tests execute the same files. Two implementations, one truth table — so a divergence shows up as a failing fixture, not as a live bug. Includes the blueprint's worked example: **73.7**.
3. **T3 — .NET build and unit tests (runs on your machine or CI).** `dotnet build`, `dotnet test`, `dotnet ef database update`. I will hand you a single `./verify.sh` (and a GitHub Actions workflow) that runs all of it, including the same fixture tests.
4. **T4 — Real integration (your machine, later steps).** Testcontainers-based Razor Page/integration tests (`dotnet test` with Docker), auth-cookie flows, SignalR, Hangfire, uploads, and anything needing an HTTP server.

Every step's commit message ends with which tiers were run: `[T1+T2 pass] [T3 pending on your side]`.

## CI

`.github/workflows/ci.yml`:

- job `sql-and-algorithms` — Node only: PGlite migration/seed/EXPLAIN checks + fixture parity runner. Runs on every push, ~1 minute, catches most schema and rules regressions without a .NET image.
- job `dotnet` — `actions/setup-dotnet` (v3 images), `dotnet restore`, `build`, `test`, plus `dotnet ef migrations script` smoke run against a `postgres:16` service container, and `dotnet format --verify-no-changes`.
- Both required to merge to `main`.

If you have a GitHub runner problem (NuGet restore blocked in CI is not expected — GitHub-hosted runners do have internet), the fallback is that the `sql-and-algorithms` job is still authoritative for schema and rules.

## What "done" means for a step, given this constraint

A step is complete when: T1 and T2 are green in this sandbox; the C# is self-consistent (every referenced type, using, and config exists in the repo; no TODO placeholders in shipped code); the migration SQL was executed against real Postgres; and T3 commands are listed for you with expected outcomes. Anything that could only be proven by compiling is stated explicitly rather than assumed.
