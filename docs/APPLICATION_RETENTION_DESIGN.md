# Application retention: deletion design (draft; not implemented)

## Approved policy

- The deletion deadline for the application snapshot and separately stored phone is 12 calendar months after the earliest applicable withdrawal, closure, removal or expiry. Withdrawal immediately removes Studio access; retention is not permission to show it.
- Renewal before deletion cancels an expiry-based deadline only. It does not undo a withdrawal deadline. Hired follows the same rules. If the job was closed before a later removal, use the earlier applicable event.
- The Candidate's main profile is not affected. Preserve the application/Candidate/job link and status history while the account exists, for duplicate prevention and history. This is still personal data, not anonymous. Current foreign keys restrict account deletion; the eventual account-erasure workflow must address that separately.
- Future application-specific PDFs need the same protection and deletion, but no such attachment implementation currently exists. Backups, downloads outside the platform, legal holds, data requests and privacy-law review need separate decisions and processes.

## Current state and risks

`ApplicationRetentionRules` only calculates a deadline; nothing deletes or schedules deletion today. Application snapshots currently hold the candidate JSON, job JSON, phone, consent version and consent time in one row. Deleting the row removes the consent metadata too. Withdrawal and application-reading services are not implemented. Status history records `OccurredAt` for a withdrawal. Job expiry may be effective before the expiry scheduler marks a job expired; renewal changes its `ExpiresAt`. A worker must not rely on an old candidate list or cached deadline when making a deletion decision.

## Proposed implementation, in small steps

1. **Prepare metadata (owner approved; local verification pending):** add a nullable `snapshot_purged_at` to the application record, plus consent version and consent time on that retained record. Copy consent metadata from existing snapshots during the migration. Preserve it for evidence only if legal/privacy review approves its purpose and retention. No copy of snapshot contents or phone goes into the retained record. A migration rollback cannot restore deleted snapshots; it must not be treated as data recovery.
2. **Deletion service, initially disabled:** find a bounded batch of potentially due snapshot IDs without returning profile details to logs. Use a fresh transaction for each item. Lock the Job row first, then the application row; future withdrawal code must follow the same order. Re-read the current job, application, withdrawal history and snapshot under the locks. Recalculate using the current time and current expiry. Missing or inconsistent required data, conflicting history, legal hold, or unknown state must fail closed: do not delete and record a non-sensitive operational error.
3. **Atomic database operation:** if still due, delete the snapshot row (including JSON and phone), set `snapshot_purged_at`, and insert a minimal system audit event in the same transaction. Never log snapshots, phone, screening answers or raw SQL parameters. Keep the application and status history unchanged; do not reduce the job capacity count or automatically reopen Full jobs. A retry after commit is a no-op. Errors or cancellation before commit roll back all database changes.
4. **Reads and access:** application readers must tolerate an intentionally absent snapshot and show a neutral “details no longer available” state. Missing snapshot without a purge marker is an integrity error, not proof of valid deletion. Never expose withdrawn or blocked Candidate details; access is rechecked on every request.
5. **Scheduling and future files:** add an explicitly disabled-by-default, monitored scheduled worker only after the service and tests pass. If application PDFs are later implemented, delete them through a durable, retryable storage-deletion workflow; database and object storage are not one atomic transaction. Do not claim a file has been deleted until storage confirms it. Backups must expire under a separate approved schedule; restored backups must be reconciled before use.

A renewal and deletion compete for the same Job lock. If renewal commits first, the worker re-reads the new expiry and does not delete for the old expiry; if deletion commits first after the deadline, later renewal cannot restore deleted details. An independent withdrawal deadline still applies. No automatic deletion should be enabled against real data until the concurrency, operational and privacy gates below pass.

## Verification gates

- Unit tests: all dates/statuses, 12 calendar months, missing metadata, exact deadline, UTC and renewal versus withdrawal.
- PostgreSQL tests: migration/backfill, partial-failure rollback and idempotency, multiple workers, readers, expiry without scheduler, renewal-versus-deletion in both lock orders, withdrawal-versus-deletion, closed/removed jobs and audit without sensitive data.
- Operational: disabled-by-default configuration, dry-run counts, bounded batches, failure alerts and manual stop. Test only on disposable/local databases.
- Privacy/legal: confirm consent evidence and remaining linked record, retention purpose, Candidate requests and account erasure, legal holds, third-party exports and backup expiration. No real application submissions until these are reviewed along with load/deadlock tests.

## Decision still needed

The owner approved the metadata-only migration. The deletion service and scheduler remain separate slices after local verification and agreement. Consent-record retention remains subject to legal review, not a claim of legal compliance.
