# M2.3b — public discovery and job detail UI

Approved after owner completed M2.3a local handoff. No migration. Public browsing is
anonymous; no bookmarks/alerts/follows/application submission or salary filter.

## Run locally

```powershell
git pull --rebase origin arena/e7fc2ca3-hiregamedev
dotnet build
dotnet test
```

Browse **https://localhost:7140/jobs**, then open a published job. Keep bare
localhost locally. In production, public /jobs and /jobs/{slug} canonicalize to
the configured Jobs portal; that host's root now redirects to /jobs, replacing
the old placeholder. Query filters survive root/portal redirects.

## UI acceptance (logged out first)

- No login requirement for browsing/detail; existing home/navbar Jobs links open
  the actual board. Confirm route /jobs is not ambiguous with HomeController.
- Responsive job cards: title, Studio/public profile link, verification, publication
  date, work mode/location, experience, job type, salary with correct LPA/month unit.
- Keyword and all approved filters work independently and in combination. Search
  is GET; share/reload the URL and keep selected filters. Submitting filters resets
  page. Previous/Next preserves keyword, type, mode, city, experience, all skills,
  fresher flag and page size. Clear all returns unfiltered results.
- Remote + city intentionally yields no matches; explanatory hint prompts clearing
  city. Skills uses native multi-select (Ctrl/Cmd on desktop), no invented matching.
- Unknown selected city/skill IDs remain visible as unavailable choices, not a
  misleading All cities/skills state. No results gives useful reset guidance.
- Invalid enum/number/bounds returns a visible validation summary with HTTP 400,
  not an exception or a false zero-result count. No catalog query on binding errors.
- Mobile filters collapse via native details/summary; keyboard labels and focus
  remain usable. Test both light/dark/system themes and narrow screens.
- Detail displays description as encoded plain text with newlines; title, Studio
  name and screening question are encoded too. Try HTML-like content in a draft,
  preview or test fixture: public output must not execute it. No Html.Raw usage.
- Detail shows all salary/experience/seniority/location/relocation/openings/notice/
  fresher/skills/proficiency/question fields, plus publication/expiry dates and
  Studio link. No internal findings, application counters, creator IDs or notes.
- Applications explicitly unavailable; no fake Apply/Save/Follow/Alert buttons.
- Anonymous CTA links to the existing Candidate registration with a safe fixed
  jobs return path; copy promises only existing profile/discoverability value.
  Signed-in users (especially Studio primary accounts) see no opposite-account
  registration prompt. No forced signup modal or content paywall.

## Visibility and public safety

- Pause/close/expire a listing, revoke verification, suspend/delete its Studio:
  list and direct URL both hide it on a fresh request, no scheduler dependency.
- Missing and hidden direct URLs show the SAME neutral HTTP 404 page; no draft
  title, Studio, moderation reason or status disclosure. Robots noindex/nofollow.
- Valid uppercase slug redirects only after a successful visible lookup. Public
  detail canonical URL uses the public slug on the Jobs portal.
- Confirm actual network response has no-store, not long-lived public caching.
  URLs never point to sandbox localhost services or require login to render jobs.
- Search/detail have basic canonical/description metadata; filtered/error result
  pages noindex, follow. No JobPosting JSON-LD while applying is unavailable.

Controller/helper tests cover anonymous routes, query forwarding, model/service
validation, neutral 404, no-store, host/slug canonicalization and encoded filter
pagination. Direct controller tests DO NOT execute MVC filters/model binding or
render views; browser/build acceptance is required.

Sandbox: C# syntax parsing only; .NET/Razor/build/tests/browser not executed due
missing SDK. M2.3c remains gated on owner acceptance and separate explicit decision
about candidate blocklist behavior. Salary filters remain deferred. Security:
previous ImageSharp advisory remediation remains separate and outstanding.
