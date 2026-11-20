# M2.2c — private job preview and publish confirmation

Owner approved proceeding after the M2.2b handoff. No migration. Do not mark the
whole M2.2 module Done before owner accepts this slice.

```powershell
git pull --rebase origin arena/e7fc2ca3-hiregamedev
dotnet build
dotnet test
node scripts/test-job-autosave.cjs
```

## Browser acceptance

- Edit a draft → **Save & preview** saves current fields, then opens
  `/Studio/Jobs/Preview/{id}?studioId=...`. It never previews unsaved request data.
- Manage → **Candidate-view preview** works for Owner, Recruiter and Viewer.
- Logged-out requests require login. Another Studio, Candidate, unrelated job ID
  or revoked membership must not reveal content. No anonymous/public preview link.
- Network response headers: `Cache-Control: private, no-store` and
  `X-Robots-Tag: noindex, nofollow`; HTML also includes robots meta. Check actual
  response headers, not only controller unit tests (they don't execute MVC filters).
- Candidate-view content shows saved Studio identity/verification, title, salary
  and unit, experience/seniority, work mode/city, openings/notice, relocation,
  fresher-friendly flag, description, required/nice-to-have skills/proficiencies,
  and optional screening question. Missing values stay explicitly unspecified.
- Plain text/HTML-like description is encoded, not executed. Newlines preserved.
  Inactive references display unavailable rather than inventing replacement data.
- Owner/Recruiter-only internal sidebar groups mandatory field errors, posting
  restrictions and advisory quality warnings. Field links open the correct editor
  field (skill findings open the skill section). Viewer sees no assessment/actions.
- Blocking findings disable confirmation button as guidance; forged/direct POST
  still must fail backend checks. Warnings alone must not disable the button.
- Publish/Resume/Reopen/Renew are status-appropriate, versioned, explicitly confirmed
  POSTs through the existing lifecycle service. Closed/Removed/Active have no
  activation confirmation in preview. Overdue Paused/Full/Active require Renew,
  never direct Resume; preview GET does not persist expiry or any history.
- Successful content/eligibility check is **not** a promise of publication. Studio
  quotas, capacity, lifecycle and fresh validation still run at the actual action.
- After preview, revoke verification, change the job in another tab, or hit a quota:
  confirmation must reject appropriately, not trust the old assessment/token.
- Renewal confirmation explains fresh validity/quota event. Resume/Reopen explains
  no expiry extension. Successful action redirects to fresh saved management state.
- No working Apply button, candidate count/reach estimate, AI, public-detail route,
  billing or application submission is added. Preview states these arrive later.
- Check layout on narrow desktop/mobile, light/dark themes. Existing autosave still
  waits before Save & preview if an autosave request is in flight.

Added controller regressions cover saved-content checking, privacy headers,
Studio/job boundaries, Viewer read-only access, warning-only pass, status action
selection, overdue renewal without writes, publish re-evaluation and save-before-
preview. Existing backend tests cover real checker and lifecycle revalidation.

Sandbox: C# syntax parsing and Node autosave tests only; no .NET SDK, so build,
Razor rendering, unit/PostgreSQL execution and browser verification remain local.
Security note: previously reported ImageSharp dependency advisories are separate
and remain outstanding; this slice does not suppress or resolve them.
