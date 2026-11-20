# M2.2b — duplicate draft and progressive autosave

Owner confirmed revised M2.2a UI, Studio verification/Admin flow and publishing
work, then approved this slice. No migration.

## Local commands

```powershell
git pull --rebase origin arena/e7fc2ca3-hiregamedev
dotnet build
dotnet test
node scripts/test-job-autosave.cjs
```

For real PostgreSQL checks, set the local `HGD_TEST_POSTGRES` maintenance connection
as described in POSTGRES_TESTS.md, then run
`dotnet test --filter "Category=PostgresIntegration"`. There are now seven PostgreSQL
tests (the original five plus concurrent draft duplication and skill-only xmin).
Never send credentials in chat. Without the variable these tests skip, not pass.

## Duplicate

- Owner/Recruiter opens Manage → Duplicate as draft; Viewer has no button and direct
  POST is forbidden. Removed jobs cannot be copied, even by a forged POST.
- Source content/skills copy to a new ID and slug; current user is creator. Status
  is Draft, application count zero, normal default cap restored, no publication,
  expiry, closure or removal metadata carried over.
- Original job/history/ledger remain unchanged. New draft has one `draft_duplicated`
  history event referencing source ID, not a copy of original history.
- **Keep the same title intentionally**, rather than append '(copy)': cosmetic suffix
  would change fingerprint v1 and weaken duplicate/cooldown checks. UI toast explains
  the copy is a draft. Publishing checks still block duplicate/recent identical jobs.
- Draft quota applies; only one concurrent copy may take the final available draft
  slot. Closed jobs may be copied but publication cooldown remains in force.

## Autosave (browser)

- New draft: type and wait >30 seconds. No autosave request; first Save is explicit.
- After manual Save, change a field. Status: Unsaved changes → Saving → Saved. Reload
  confirms persistence; untouched forms make no autosave requests.
- Test both draft and current paused job. Active/Expired/etc cannot autosave even if
  another tab changes status while this editor stays open.
- Two tabs: save/autosave in first, then change second. Second stops with a conflict
  message, retains typed text and offers reload; it never overwrites the first.
- Repeat using only skill-required/proficiency changes. Parent xmin must advance
  even when no scalar content changes, protecting the entire skill set.
- Type while an autosave request is in flight (network throttling): newer text stays
  in inputs and remains dirty for the next save; no overlapping requests occur.
- Manual Save while autosave is in flight waits for its updated version. Save &
  manage still saves the latest current form, then navigates to management.
- Take network offline, expire session, revoke write permission, or return a 429:
  autosave stops, preserves text, shows failure, and does not blindly retry an unknown
  commit outcome. Reload warns about discarding unsaved changes.
- Leaving with unsaved changes prompts a browser warning. Normal manual Save does
  not trigger that warning. Disable JS: manual saving still works.
- Failed manual save must not restart background autosave on the error page.

POST endpoints use authenticated actor, backend membership, antiforgery, versioned
lifecycle Save and existing burst limiter. Autosave cannot create new drafts.

The Node harness passes in Arena, but mocks DOM/network/timers; it is not a real
browser test. .NET build/tests and PostgreSQL checks could not run in Arena (SDK
absent); owner local verification is required before M2.2c.
