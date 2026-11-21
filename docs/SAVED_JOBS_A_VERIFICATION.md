# Saved Jobs — Slice A verification (backend only)

Owner approved schema and Slice A. No UI, authentication-return flow, alerts,
follows, application tracking or AI in this slice. The backend endpoints are not
linked from the public site yet. UI is a separate approval/verification gate.

## Migration

`20261114090000_CandidateSavedJobs` creates `candidate_saved_jobs` with required
candidate_id and job_id (uuid), created_at (timestamptz), primary key
(candidate_id, job_id), candidate FK cascade, job FK restrict, job_id index and
(candidate_id, created_at DESC, job_id ASC) index. Down drops this table and all
saved entries. Hand-authored migration/designer/snapshot require local EF validation;
they were not generated or executed in this sandbox. No shared/production DB used.

## Run safely on your local development database

1. Pull without overwriting local settings:

```powershell
git pull --rebase origin arena/e7fc2ca3-hiregamedev
dotnet build
dotnet test --filter 'Category!=PostgresIntegration'
```

2. Review `ConnectionStrings:DefaultConnection` locally (do not paste credentials
   into chat). Make sure it points to your own development database, not shared or
   production. Back up first and review migration operations. From repository root:

```powershell
dotnet ef migrations list --project src/HireGameDev.Infrastructure --startup-project src/HireGameDev.Web
dotnet ef database update --project src/HireGameDev.Infrastructure --startup-project src/HireGameDev.Web
```

3. Run the opt-in PostgreSQL integration suite using a **disposable local** server
   and maintenance connection as described in `docs/POSTGRES_TESTS.md`. The suite
   creates and drops unique test databases and applies migrations there; never use
   a shared or production server. Without HGD_TEST_POSTGRES these tests are skipped.
4. Verify migration/snapshot consistency with your installed matching dotnet-ef:

```powershell
dotnet ef migrations has-pending-model-changes --project src/HireGameDev.Infrastructure --startup-project src/HireGameDev.Web
```

If this reports unexpected differences, stop and share the output; don't generate or
apply a corrective migration without review. Confirm the index definitions and FK
behaviour in the local database. Do not roll back a database with saves you need.

## Backend contract

- Candidate-only authenticated endpoints: GET `/candidate/saved-jobs`, POST
  `/candidate/saved-jobs/{jobId}/save` and POST `/candidate/saved-jobs/{jobId}/remove`.
  POSTs require anti-forgery tokens. Actor is sourced from authenticated claims;
  database rechecks primary Candidate, active user and non-deleted profile.
- GET returns the owner's list only. It omits blocks before count/paging; order is
  saved time descending then job UUID ascending. Page size 1–50, default 20.
- Public eligible items use the public detail service's visibility and blocklist
  gate each time. Others return `job: null`, stable job ID for removal and saved
  time, with no title, Studio or moderation fields. Blocked Studio entries are
  omitted entirely. Reads are bounded to 50, though each row uses an additional
  public detail query (performance should be assessed with realistic traffic).
- Save rechecks current public visibility and blocks; duplicate save and remove
  are idempotent. Unique-violation handling is narrowly scoped to the composite PK.
  Removal is scoped by the resolved Candidate, even for unavailable jobs.
- Successful action responses are private/no-store, vary by Cookie and noindex.
  Direct controller tests check attributes but do not execute middleware/filters.

## Verify

Automated tests cover repeated/concurrent saves, deterministic ties, isolated
Candidate ownership, Studio-primary legacy profile denial, blocked/unblocked list,
neutral unavailable entries and removal, and never-published draft deletion after
saving a different published job. Real PostgreSQL tests cover migration/SQL/PK.
Also exercise actual authorized HTTP requests locally with antiforgery tokens:
missing tokens and Studio/anonymous accounts must fail; one Candidate cannot read
or remove another Candidate's save; pages are private/no-store; block then unblock
restores the original save; pause/expire/remove or revoke/suspend/delete the Studio
and verify neutral entries, then remove them. Confirm no unavailable title or Studio
appears in the returned JSON. Test direct routes because no UI is provided yet.

C# syntax parsing and git diff whitespace checks only were possible in sandbox;
.NET build, unit tests, PostgreSQL tests, real auth/CSRF and migration are unverified.
