# Saved Jobs Slice B — UI and authentication return

Slice A verification results have not been reported. Complete the local backend,
migration, PostgreSQL and HTTP checks in `docs/SAVED_JOBS_A_VERIFICATION.md` first.
This slice adds no migration. The sandbox has no .NET SDK, so code was syntax-parsed
but build, tests, Razor, authentication and browser flows remain unverified.

```powershell
git pull --rebase origin arena/e7fc2ca3-hiregamedev
dotnet build
dotnet test
```

## Browser acceptance

- Anonymous: open a public job, choose sign in to save (or Candidate registration),
  sign in, and verify you return to that exact job. It is **not saved automatically**:
  click Save explicitly. Repeat with OAuth and, when configured, first-time OAuth
  completion. Test switching between login and registration.
- Email registration: start at job, register, open confirmation email (including a
  resend), confirm, follow its sign-in link, log in and return to the job. No automatic
  save. A direct arbitrary return URL from another site must be rejected; open redirects
  are never allowed. Verify email confirmation without a return URL also works.
- Save, refresh and revisit: correct saved state. Remove from detail and saved list;
  repeat actions and confirm no duplicates. Candidate nav opens `/candidate/saved`.
- Pause/close/expire a saved job or revoke/suspend/delete its Studio: the private list
  shows a neutral removable unavailable entry, no title, Studio or moderation details.
  Block its Studio: entry disappears entirely; unblock restores the existing save.
- Sign in as another Candidate: no other person's saves appear; forged POSTs cannot
  remove them. Studio-primary and legacy opposite-profile accounts must not save or
  access the saved list. Anti-forgery failures reject writes. Anonymous list is gated.
- Confirm private/no-store and noindex on saved pages, accessible names and keyboard
  interaction, mobile and both themes. Confirm no fake alerts/follows/applications.
- Exercise local `https://localhost:7140` and production Jobs portal when available.
  Cross-device email confirmation and resend should preserve a local job return path.

Controller tests directly invoke actions and inspect anti-forgery/authorization
attributes; they do not execute MVC filters, authentication, rendered Razor or a real
browser. Any failed checks should be fixed before Slice B or the module is accepted.
