# M2.2a local acceptance checklist

No migration. Run `dotnet build` and `dotnet test` after pulling this branch.
The sandbox has no SDK, so neither compilation nor browser execution was possible there.

Open Studio profile → Jobs (or `/Studio/Jobs`).

- Owner/Recruiter: create incomplete draft, save, refresh; content and selected skill
  proficiency/required flags survive. Empty skill rows do not create associations.
- Fill all role/location/pay/hiring fields. Remote uses blank city; choose an active
  city for onsite/hybrid. Description is plain text, safely encoded by Razor.
- Save a complete job, open Manage status. Publish it: verified Studio and all checks
  required; unverified Studio remains draft. No Admin approval step.
- Invalid salary spread/duplicate/contact details block publish; warnings do not.
- Active → Pause → Edit → Save → Resume. Resume does not extend expiry. Published
  Active/Full/Expired content cannot be edited. Stale versions reject writes.
- Expired/due listing offers Renew, not Resume; explicit checkbox confirmation is
  required. Successful renewal reruns checks and assigns fresh expiry.
- Full only reopens with capacity. Close requires confirmation and is terminal.
- Delete requires confirmation and only works for never-published drafts.
- Viewer: list/management read-only. Direct POST attempts to Save/Transition/Delete
  are forbidden, not merely hidden. Unrelated Studio IDs cannot be read or mutated.
- Open edit in two tabs. Save first, then second: with PostgreSQL the second shows a
  concurrency error and must reload; it must not overwrite the first change.
- Missing antiforgery token is rejected. Excessive POST bursts return HTTP 429.
- Lists show status counts and pages of 25. Workspace links preserve Studio scope.
- Only saved content is published. No autosave, duplicate or preview feature yet.

Existing lifecycle tests cover backend RBAC/transitions/history; new list tests cover
Viewer reads, unauthorized access, filtering/counts, pagination and read-only history.
These are not HTTP integration tests: exercise the POST/antiforgery checks manually.

Security dependency note: owner's earlier restore reported ImageSharp 3.1.12
advisories. That separate dependency remediation is still outstanding; these
warnings must not be mistaken for resolved by the job-management changes.

## Reopened acceptance: UI / Publish feedback

Owner rejected the initial UI and reported Publish not showing Active. Acceptance
is reopened; M2.2b must wait. Specific publication blocker has not been reproduced.

- Verify new job list/table, grouped editor and management sidebar on mobile and
  desktop, light and dark themes.
- Change job content, click **Save & manage status**: verify the saved description
  and salary on the management page match those changes before publishing.
- Publish an incomplete draft: expect an **Action not completed** alert, Draft
  badge, individual blocking findings/codes and links to editor fields.
- Click a finding's Fix link: editor must open at that field. Skills uses the skill
  section anchor. Studio verification links must preserve workspace scope.
- Publish a valid saved draft from a verified Studio: expect "now Active", an
  Active badge, and the job under the Active list tab after navigation/refresh.
- If it remains Draft, report the exact visible finding code/message. Do not
  weaken the publishing rules to force a status change.
- Stale, malformed or unconfirmed requests must not report success. A stale
  request reloads the current saved snapshot/version before allowing a retry.
- Controller tests exercise response decisions, not browser/HTTP model binding.
  The tests now reference the Web project, so `dotnet test` also builds its views.
