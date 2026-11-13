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
local variable. Without that variable, the five PostgreSQL tests are explicitly
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
checks, not exhaustive load/linearizability proofs. They do not cover Phase 3
application-cap enforcement, which has not been implemented yet.

Record the build, unit-test and five integration-test outcomes before proceeding.
The Arena sandbox has no .NET SDK or PostgreSQL installed, so these results have
**not** been executed/verified there. No new migration is introduced by this suite.
