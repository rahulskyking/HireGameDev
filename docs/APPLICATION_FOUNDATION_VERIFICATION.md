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
