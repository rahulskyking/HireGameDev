# Phase 3 application foundation — local verification

Schema-only slice. There are no application endpoints, UI, upload, authorization, snapshot construction, consent validation, status transitions or capacity enforcement yet. JSONB checks validate shape only; privacy, append-only history and snapshot immutability require subsequent services. Phone is a separate database column, not encrypted by this migration; application access controls are not implemented yet. Do not use this schema to accept real applications.

The manually authored migration creates `applications`, `application_snapshots` and `application_status_history`. Unique `(candidate_id, job_id)` includes withdrawn rows. FKs restrict deletion. The snapshot holds consent metadata, candidate/job JSON objects and optional phone; resume storage will be designed later. PostgreSQL `xmin` is a system column and is not added by the migration. Existing jobs/counts are unchanged. Rollback drops the three new tables and all their contents. Do not run Down on databases with application data.

On a local disposable PostgreSQL database, with the .NET 8 SDK and dotnet-ef installed:

```bash
dotnet build
dotnet test
dotnet ef migrations list --project src/HireGameDev.Infrastructure --startup-project src/HireGameDev.Web
dotnet ef database update --project src/HireGameDev.Infrastructure --startup-project src/HireGameDev.Web
```

Confirm `20261115090000_ApplicationFoundation` is applied, inspect tables/constraints/indexes and verify that duplicate candidate/job pairs are rejected even when withdrawn, invalid status and invalid snapshot JSON object are rejected, and FK restrictions work. Exercise only on a disposable database. Run PostgreSQL integration tests as documented in `docs/POSTGRES_TESTS.md` where configured. No migration, build or tests were run in the sandbox (SDK/PostgreSQL unavailable). Report results before the next slice.

## Backend rules progress

Pure application lifecycle rules and exhaustive status-pair tests have been added. This is not yet an application service: no operation uses these rules to persist a transition, no Candidate can submit, and no privacy or capacity guarantee is in effect. Re-run `dotnet test` after updating the branch. The owner has authorized continued work but has not reported specific migration or test results.

A pure snapshot builder and privacy-focused unit tests have also been added. It is not wired to submission and must not be treated as evidence of consent or authorization. Required references must be loaded and collections loaded consistently by a future transaction-aware service; profile/job edits and block changes must be coordinated before submission.

The read-only eligibility service is advisory and has no route/UI. It checks Candidate-primary identity, public job/Studio visibility, blocklist, duplicates (including withdrawn), and capacity. It never reserves a slot; the future submission transaction must recheck all conditions. Run the full test suite and verify the migration locally; InMemory tests do not prove PostgreSQL translation or concurrency.

## Private review service

`IApplicationReviewService` builds a read-only draft from the Candidate's loaded profile, job and Studio after advisory eligibility checks. Relational reads use a repeatable-read transaction; no data is submitted or reserved and any later submission must revalidate reviewed data. No HTTP endpoint or upload is provided yet. Screening answers are bounded at 2,000 characters. The owner reported the prior findstr/clean/build checks passed; no specific test or migration results were reported. The new InMemory and opt-in PostgreSQL review tests were not run in the sandbox.

## Submission prototype — not production-ready

Submission uses a 15-minute protected review token, rechecks the reviewed snapshot and eligibility, and writes the application, snapshot, consent, history and job capacity/Full transition in a PostgreSQL transaction. It deliberately takes SHARE locks on **entire** account, profile, reference, blocklist and Studio tables: unrelated writes will wait until submission commits or rolls back. Deadlocks can occur with other transactions; the service attempts to return a retryable conflict for PostgreSQL deadlocks. This is a conservative prototype, not approved for production exposure or load. A row-scoped writer-coordination design and PostgreSQL race/load tests are required before exposing it. No endpoint, UI, or resume upload is present. PostgreSQL integration tests use a disposable database only; see `docs/POSTGRES_TESTS.md`. Sandbox did not run .NET or PostgreSQL tests.

### Additional verification

`ApplicationSubmissionTokenTests` checks consent, actor/job binding, malformed and expired tokens and that submission refuses the InMemory provider. The opt-in PostgreSQL suite also injects a failure after `SaveChanges` and before commit, asserting application, snapshot, histories and job capacity/Full transition roll back together, then retries. These tests do not establish production readiness or replace race/load tests for concurrent profile, account, Studio or blocklist changes. The owner replied "done" after the previous verification request; exact test counts and whether the opt-in PostgreSQL suite ran were not reported.

### Candidate-scoped locking migration

Migration `20261116090000_CandidateApplicationCoordination` adds database triggers for Candidate child records and blocklist writes. Run the migration only on your disposable/local database, never a shared or production database. Its Down removes the safeguards, so submission must not run if it is rolled back. Apply it before testing submission. Then run the full test suite and the opt-in PostgreSQL suite described in `POSTGRES_TESTS.md`; the new tests check a child edit against submission and a separate Candidate's child write while the first Candidate is locked. These tests have not been run in the sandbox.

This is only a partial improvement: users, cities, skills and Studio profiles still have broad locks. Other races, deadlocks, load and privacy/retention still need review before real applications are accepted.

### Account and Studio locking

Submission now locks the relevant Candidate account and Studio rows rather than their whole tables. It still uses broad locks for cities and skills. The PostgreSQL tests include account/Studio changes during submission and verify that unrelated account/Studio updates can proceed while submission waits. Run the opt-in PostgreSQL tests after pulling this change. These tests are not a complete deadlock, load or privacy review; do not enable real application submission yet. No additional migration is included in this step.
