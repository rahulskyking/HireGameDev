# Job PostgreSQL verification gate

M2.1c must pass this gate before beginning M2.2a. These tests are in the existing
test assembly to reuse internal service access, but are tagged
`Category=PostgresIntegration` and use real PostgreSQL, not EF InMemory.

## Safety and prerequisites

Use a **disposable local PostgreSQL 16 instance**, not production or shared staging.
The login must be able to CREATE/DROP DATABASE and install the application's
`citext` and `pg_trgm` extensions. Each test creates a unique `hgd_test_<guid>`
database, applies checked-in migrations and drops only that database in cleanup.
The database named in the supplied connection is just a maintenance connection;
the tests do not migrate or drop it. A killed test process may leave a generated
database behind; inspect and remove only test databases you recognize.

Keep credentials in the local environment, never source control or chat.

## Run

```bash
git pull --rebase origin arena/e7fc2ca3-hiregamedev
dotnet build
dotnet test --filter 'Category!=PostgresIntegration'
# Set HGD_TEST_POSTGRES locally to your disposable server's maintenance connection.
dotnet test --filter 'Category=PostgresIntegration' --logger 'console;verbosity=normal'
```

PowerShell accepts the same dotnet commands; use `$env:HGD_TEST_POSTGRES` to set the
local variable. Without that variable, the PostgreSQL tests are explicitly
**skipped**, not passed. With it set, connection/migration/setup failures fail the
tests rather than falling back to a different provider.

## Coverage

- Independent contexts/connections race to publish with one remaining Active slot.
- Independent contexts race against the rolling publication quota.
- Identical drafts race to publish; only one becomes Active.
- A real `xmin` stale edit is rejected without overwriting the newer content.
- An injected exception after database save but before transaction commit rolls
  back the job, status history and publication ledger together; retry succeeds.

Races use a common start gate and separate connections. These are regression
checks, not exhaustive load/linearizability proofs. Phase 3 now adds an opt-in submission prototype test for duplicate and capacity races, stale review and an already-blocked Studio. It does not prove all block/profile edit races or load behavior.

M2.2b adds concurrent draft duplication and skill-only xmin checks; M2.3a adds
public query translation/visibility; M2.3c adds candidate blocklist translation and immediate unblock. Saved Jobs Slice A adds concurrency, privacy, block and draft deletion regression coverage.
Record the build, unit-test and integration-test outcomes before proceeding.
The Arena sandbox has no .NET SDK or PostgreSQL installed, so these results have
**not** been executed/verified there. No new migration is introduced by this suite.

Phase 3 submission prototype also includes an injected post-save rollback/retry test; it is opt-in and unexecuted unless `HGD_TEST_POSTGRES` is configured. It does not yet establish safety or acceptable performance for all concurrent writers.

Retention prototype tests cover atomic rollback, concurrent retries and basic expiry/withdrawal checks. They do not by themselves prove the lock-order races, reader safety, or production readiness. Run only on disposable PostgreSQL.

Retention race tests use PostgreSQL lock-wait observation to check both orders of renewal and deletion. The original withdrawal test uses a simulated transaction; later tests also use the withdrawal service itself. These tests do not replace broader concurrency or privacy review.

Candidate withdrawal backend tests exercise owner checks, inactive accounts, Hired finality, rollback, concurrent retries, unchanged Full status and both lock orders against the retention prototype. Tests are opt-in and have not been run in the sandbox. No UI or Studio application reader is included.

Studio application status prototype adds role, block/unblock, rollback, invalid/final status and block-before-change lock-wait tests. It has no applicant reader or routes. Additional races and load/privacy review are required before production; tests have not been run in the sandbox.

### Internal Studio applicant list prototype

`StudioApplicantListService` returns paginated application IDs, statuses and submission times for an authorized Studio member, with the same empty result for an inaccessible job. Page sizes are 1–100, and results are ordered by submission time and application ID. Offset pages can shift or duplicate items when applications or access change between requests; they are not a consistent snapshot. There is no route or UI. Authorization, block, withdrawal, candidate account and snapshot checks are evaluated in a single PostgreSQL statement. A response already in flight cannot be recalled if access changes after that statement. Request-level authorization, privacy and concurrency review are still required before exposure. Run the PostgreSQL integration tests above to verify this prototype; skipped tests are not a pass.

### Internal Studio applicant detail prototype

`StudioApplicantDetailService` reads the version 1 submission snapshot only, for authorized Owner, Recruiter and Viewer members. It hides phone unless the status is Shortlisted or Hired, rejects missing/unsupported/incomplete snapshots and checks authorization, blocks, withdrawal, purge and account state in the same query. An already-started response cannot be recalled after access changes. No route, UI or PDF access is provided. Privacy review, additional concurrency tests and request-level protection remain required before exposure; free text may contain sensitive or NDA information.

The application workflow hardening suite also covers status-versus-withdrawal ordering with the real services, and reads before and after committed withdrawal or block changes. These tests document PostgreSQL's statement-snapshot behavior: a read started before revocation can return the previously authorized data; a new read after commit must deny it. They do not establish that all races or production load are safe.
