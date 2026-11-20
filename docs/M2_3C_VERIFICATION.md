# M2.3c — candidate blocklist, SEO and accessibility verification

Owner approved extending their existing Studio blocklist to signed-in Candidate job
browsing. No migration. This is a personal viewing preference, not global privacy
or access control: anonymous visitors and other accounts still see eligible jobs.
The candidate's authenticated Identity user ID is passed by the server, never a
form/query parameter; the query rechecks the current primary account type, active
account and non-deleted candidate profile. Legacy opposite profiles do not count.
Search and detail apply the exclusion before pagination and before canonical slug
redirect; a blocked job returns the same 404 as a missing/hidden job. Unblock takes
effect on the next request. Existing explicit-application exception remains for Phase 3 implementation.

## Local build and automated checks

```powershell
git pull --rebase origin arena/e7fc2ca3-hiregamedev
dotnet build
dotnet test
```

Run the optional PostgreSQL tests as documented in `docs/POSTGRES_TESTS.md` against
an isolated maintenance connection with CREATEDB permissions. A new opt-in test
exercises the candidate/blocklist query translation and immediate unblock. Unit tests
also cover unrelated viewers, legacy Studio accounts, neutral direct 404 and session
identity forwarding. These tests were not executed in the sandbox (no .NET SDK).

## Owner acceptance

- Publish an eligible job in a verified Studio and open it anonymously. Sign in as
  a Candidate, use Candidate Settings to block that Studio. Search (including paged
  and filtered search) must omit it and update total count; direct URL must show the
  same neutral 404 as a nonexistent slug, including uppercase slug. It must not reveal
  that a block exists. Unblock and repeat: the job reappears without a restart.
- A second Candidate, a Studio-primary account (including any legacy Candidate
  role/profile), and an anonymous/private browser still see the eligible listing.
  Query-string attempts to supply viewerUserId must have no effect. Logout/login
  and shared production cookie domains should be tested on actual deployment hosts.
- Inspect real network responses, including redirects and 404s: no-store; signed-in
  responses private, no-store, Vary: Cookie, X-Robots-Tag noindex. Missing/blocked
  detail 404 has noindex,nofollow. Check proxies do not override cache headers.
- Anonymous unfiltered page and detail have one appropriate title, description,
  canonical; filtered/paginated/error pages are noindex,follow. Canonical links
  point to Jobs host in production and bare localhost locally. No JobPosting schema
  while applying is unavailable; no fake features. Test actual rendered HTML.
- Keyboard-only: navigate header, filters, native multi-select and results; open/
  close filters, submit invalid values and confirm error summary is announced. Test
  screen reader labels, focus visibility, zoom 200%, narrow viewport, long Studio/
  job names and light/dark/system. Confirm settings preference link works.
- Complete prior M2.3b acceptance checklist as well. Automated controller tests do
  not execute MVC filters, middleware, Razor rendering, real auth or a browser.

No salary filters, engagement features or applications are included. Milestone is
pending owner acceptance; do not mark Done until checks pass.
