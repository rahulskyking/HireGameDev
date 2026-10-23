# HireGameDev — Blueprint Fixes (issues found in v2.0 before we build)

These are not nice-to-haves. Each one is a conflict, a loophole or a default that will silently hurt the product if it ships as written. Fix them in the schema/rules, not in the UI.

---

## B-1 · `applications` UNIQUE(job_id, candidate_id) blocks the re-apply rule — **blocker**

- **Conflict:** Phase 3 says "Withdrawn applications can be re-submitted once after 7 days", but the table has `UNIQUE (job_id, candidate_id)` with no status filter. The withdrawn row still exists, so the second insert violates the constraint and throws.
- **Fix:** replace with a partial unique index and track attempts:
  ```sql
  DROP CONSTRAINT applications_job_id_candidate_id_key;
  CREATE UNIQUE INDEX ux_app_one_open ON applications (job_id, candidate_id)
    WHERE status NOT IN ('withdrawn','auto_declined');
  ALTER TABLE applications ADD COLUMN attempt smallint NOT NULL DEFAULT 1;
  ```
  Service rule: `attempt <= 2` and `now() - withdrawn_at > 7 days`. `applications_count` counts open rows only, and the applicant-cap check uses `status NOT IN ('withdrawn','auto_declined','rejected')`.
- **Same trap elsewhere:** `invites UNIQUE (job_id, candidate_id)` — a declined invite should be re-invitable after the job changes; use `WHERE status = 'pending'`.

## B-2 · Anti-ghosting clock can be reset by just clicking "viewed" — **exploitable loophole**

- **Problem:** reminders and auto-decline trigger on `last_action_at` older than 7/14 days, but `last_action_at` is updated by *any* action — including the studio marking the application `viewed`, which the blueprint itself says is **not** a real response. A studio can keep every application alive forever by opening it, never deciding, and never being auto-declined or reminded. The one feature that defines this product would be trivially disabled.
- **Fix:** drive the ghosting clock off `applied_at` and a decision flag, not off activity:
  ```sql
  -- pending_studio_decision: applied/viewed AND first_response_at IS NULL
  -- reminder   when now() - applied_at > reminder_days   AND reminder_sent_at IS NULL
  -- auto-decline when now() - applied_at > autodecline_days AND first_response_at IS NULL
  ```
  Keep `last_action_at` for display only ("last touched 3 days ago"). Also add `applications.studio_clock_paused boolean` for the legitimate case "candidate asked for 2 more weeks" — audited, and it does not pause the metric.
- **Add:** a one-click candidate **follow-up nudge** (`follow_up_sent_at`), one per application, which notifies the studio and *does not* reset any clock.

## B-3 · `open_to_remote DEFAULT false` + hard filter F1 = silently empty recommendations — **high**

- **Problem:** F1 removes a remote job entirely when `open_to_remote` is false. The column defaults to false, so every candidate who does not consciously tick a checkbox is hard-filtered out of most of India's postings (remote is a large share of game-dev work here) — and they see an empty feed with no explanation. Defaults must never be dealbreakers.
- **Fix (pick one, prefer a):**
  1. Make it a required, no-default question in onboarding step 5 (`open_to_remote boolean` nullable until answered; unanswered = treated as "any" for filtering, "unspecified" in the match breakdown tip).
  2. Replace the boolean with `work_mode_prefs text[]` (subset of onsite/hybrid/remote), default `{onsite,hybrid,remote}`; F1 fires only when the job's mode is explicitly excluded.
- **Do the same for `willing_to_relocate`:** default false is fine because its absence only costs a score, not a filter — but check F1's relocate branch uses `city_id` only, never "same state".

## B-4 · `profile_views` limit is ambiguous (per studio or per person?) and cheap to bypass

- **Problem:** `limits.profile_views_per_studio_per_day` (100) with `UNIQUE (studio_id, candidate_id, view_date)` means one recruiter can view the *same* candidate 200 times (dedup per day) while another recruiter on the same studio gets a fresh budget. And the "views per day" ceiling for Pro (300) reads like a *distinct-candidate* budget.
- **Fix:** define it explicitly as **distinct candidates per studio per day**, counted from `profile_views` (the unique index already gives that for free), shared across all `studio_members`. Log repeat views with a separate counter for the "who looked at me" feature. Add `viewer_user_id` to the limit's audit trail so an individual recruiter spamming gets flagged.

## B-5 · `jobs.slug` permanent UNIQUE interacts badly with soft delete and re-publish

- **Problem:** soft-deleted or closed jobs keep their slug forever; the studio that wants to repost the same role gets a collision, and any recycled-slug UX (append `-2`) breaks the SEO URLs from `/Sitemap.xml`.
- **Fix:** `CREATE UNIQUE INDEX ux_jobs_slug ON jobs (slug) WHERE deleted_at IS NULL;` keep the old row's URL 301-redirecting to the new one via a `job_slug_history` table (or `redirects`) so reposts don't lose link equity. Slug format keeps the `…-a1b2c3` suffix as the collision escape.

## B-6 · `expected_ctc_lpa NOT NULL` on `candidate_profiles` breaks the wizard and resume import

- **Problem:** the profile row is created at onboarding step 1 (and by resume import), but `expected_ctc_lpa` is `NOT NULL` and first asked in step 5. Every draft save must fake a number or fail.
- **Fix:** allow `NULL` for all step-5 fields (`expected_ctc_lpa`, `notice_period_days`, `availability`) and enforce "required to apply" in the service (already covered by completeness ≥ 60 + an explicit `ApplyGuard`). Keep the CHECK as `CHECK (expected_ctc_lpa IS NULL OR expected_ctc_lpa > 0)`.

## B-7 · Money and salary units: one place to normalize, or the 1.5× spread rule is wrong for internships

- **Problem:** `job.salary_max_spread` (1.5) is validated on raw column values, but `pay_unit` can be `monthly_inr`. A 15,000–25,000/month internship has a 1.67 spread and gets rejected for being too wide, while a 3–40 LPA job is the real problem case.
- **Fix:** validate the spread on **normalized LPA** (`monthly_inr × 12 / 100000`), never on stored values; put that in one helper (`SalaryMath.ToLpa`) used by search filters, F3, logistics score and the admin review flag. Add a floor check for monthly roles (≥ ₹5,000/month) to block exploitative listings, and a floor of 1.00 LPA otherwise.

## B-8 · Rate limiting behind Nginx will bucket every user into one IP — **launch-day bug**

- **Problem:** the app sits behind Nginx on a VPS, so `HttpContext.Connection.RemoteIpAddress` is `127.0.0.1` for everyone. A 5-per-15-minutes login limiter then locks the entire site out after 5 failed logins anywhere, and `audit_logs.ip` / `consent_records.ip` become useless.
- **Fix:** enable `ForwardedHeadersMiddleware` (X-Forwarded-For / X-Forwarded-Proto) with `KnownProxies = [127.0.0.1]` only, never `KnownProxies = Any` (spoofable), and set Nginx `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;`. Add a startup test that asserts a spoofed header from a non-trusted proxy is ignored.

## B-9 · `match_scores` grows quadratically with no pruning

- **Problem:** "recompute one job against all discoverable candidates" on every publish or profile save writes N_jobs × N_candidates rows over time. At 1,000 jobs × 10,000 candidates that table is unbounded and the nightly IDF + debounce work gets slower every week.
- **Fix:** (a) delete rows for jobs that are not `active` in the same nightly job that expires postings; (b) store only rows where `passes_filters = true OR final_score >= 25` (below that the pair is never surfaced anyway); (c) add `match_scores.expires_at` and drop anything not read in 60 days; (d) index `(candidate_id, final_score DESC) WHERE passes_filters` exists — add the same partial index with `ORDER BY final_score DESC NULLS LAST` for the Best-match feed, and cap the feed query at 500 candidates per refresh.

## B-10 · Admin panel can read everything but has no second factor — pull it forward

- **Problem:** MFA/passkeys are deferred ("later"), yet `AdminOnly` includes read-only candidate PII (phone after shortlist, resumes-adjacent data, audit log, settings). In Phase 4 view-as is added, which is more powerful than most CMS superuser roles.
- **Fix:** in M0, make **TOTP mandatory for `AdminOnly`** (schema-ready now via `passkeys`/`user_credentials` so WebAuthn replaces it without migration), require a reason string for every admin mutation (already in the blueprint), and make `audit_logs` append-only at the DB level: `REVOKE UPDATE, DELETE ON audit_logs FROM app_role;` plus a daily `pg_dump` copy to a second bucket. View-as should additionally require re-auth within the last 5 minutes.

## B-11 · `cities` matching is too literal for Indian metros

- **Problem:** `city_id` exact match decides "same city: 100". Pune job + candidate in "Pimpri-Chinchwad", or Noida job + candidate in "Greater Noida West", score as different cities and get hard-filtered for onsite work.
- **Fix:** add `cities.metro_area text` (e.g. `pune-metro`, `delhi-ncr`, `mumbai-metro`, `bengaluru`) plus `metro_rank`. Location scoring: same city 100, same metro area 85, otherwise relocate rules. Keep F1 hard-filtering on metro area, not city id.

## B-12 · Title verification: IGDB will fail most Indian candidates; add Steam/itch/Play Store

- **Problem:** Phase 5 relies on IGDB (Twitch) + store URL HTTP 200. IGDB coverage of small Indian and mobile titles is thin, and "store URL returns 200" is trivially satisfiable with a redirect farm.
- **Fix:** add two verifiers with better signal, both cheap:
  - **Steam:** resolve `store.steampowered.com/app/{id}` → `steam_appid`, then `ISteamApps/GetAppList`/appdetails `developers`/`publishers` must contain the studio's declared name. Real developer↔title link, verified by a third party.
  - **itch.io:** `game/{user}/{slug}` → the owning user's profile must list the studio name/website the candidate/studio declared; itch also gives genre/tags/engine (where available) for the relevance bonus.
  Keep Google Play via `publisher`/developer-name match on the app page (server-side fetch, SSRF-guarded as specified). Treat HTTP-200 alone as `store_link` (a weaker, separate state) — never as `title_verified`, so the badge cannot be farmed by anyone with a domain.
- Also: reject "verified" for titles where the store page is 404'd later — a nightly re-check that demotes to `link_dead` and notifies, so the badge stays honest.

## B-13 · Tests require Docker (Testcontainers) which is not available in every environment

- **Problem:** the blueprint's integration tests assume Testcontainers PostgreSQL; CI here has no Docker.
- **Fix:** keep Testcontainers for CI where Docker exists, but add a first-class **`pg-gateway`/PGlite-style local target** (or "run Postgres on the VPS, point `DATABASE_URL` at it") so `npm test`/`dotnet test` works without Docker. Encode both in `TESTING.md`: unit → in-memory, integration → real Postgres, and never mock SQL for full-text or partial-index behaviour.

## B-14 · `search_vector` ignores skills, so engine searches rank badly

- **Problem:** the generated column covers `title` + `description` only. "unreal blueprint jobs" ranking depends on whether someone wrote the word in prose; the structured `job_skills` rows — the most reliable field in the schema — are invisible to ranking.
- **Fix:** build the vector from title, description **and the skill names**, maintained by a trigger or in `IJobService` (a generated column can't reach a join table). Weights: title A, skill names B, description C. Re-run `EXPLAIN` to confirm `ix_jobs_search` is still used.

## B-15 · DPDP: retention is stated but not enforceable

- **Problem:** "audit logs and application history have a defined retention period" with no column, job or limit behind it, and `deleted_at` grace of 30 days has no sweeper in the build order.
- **Fix:** in M0 add `retention` config keys (`retention.audit_logs_days`, `retention.match_events_days`, `retention.deleted_accounts_grace_days`), a nightly purge job, and a `users.purge_after_at` set at deletion time so the sweep is a single indexed query. Record the export format version so a future lawyer/audit can read it.

## B-16 · The 73.7 golden test only reproduces if components are rounded *before* weighting — **verified here, will fail if implemented "naturally"**

I implemented the Phase 3 algorithm exactly as written and ran the blueprint's own worked example (`BLUEPRINT.md` line 779) against it:

```
skill_score   = 100 x (2.0x0.81 + 1.6x0.70 + 1.6x0) / 5.2 = 52.7   ✅ matches
experience_fit = 100                                      ✅ matches
logistics      = 0.4x100 + 0.4x100 + 0.2x93.3 = 98.7      ✅ matches
fit_for_studio = 0.55x52.7 + 0.20x100 + 0.25x98.7 = 73.66 -> 73.7  (blueprint)
```

But the raw values are `skill = 52.69230…` and `logistics = 98.6666…`. If the engine keeps full precision through the weighted sum and rounds once at the end — the normal engineering instinct — the result is **73.6474 → 73.6, and the acceptance test "the worked example returns 73.7" fails** for no visible reason.

- **Root cause:** the spec writes `fit_for_studio = 0.55 x skill_score + …` without saying whether `skill_score` here is the rounded display value or the raw value. The example's arithmetic only closes if each component is rounded to 1 decimal first.
- **Fix (adopt as the rule, D-015):** round each component score to 1 decimal, then apply the weights, then cap at 100, then round the total to 1 decimal. Make `Round1` the single helper, and assert `MatchRoundPolicy` in a test so nobody "optimizes" the rounding away.
- **Knock-on:** ranking must use the unrounded value (or `final_score` plus `applied_at` as the tiebreaker), because two candidates can now land on identical rounded scores 0.05 apart in reality. The blueprint already breaks ties on `applied_at` — keep that, and store both `final_score` (display) and `final_score_raw` (rank) so the two uses can never drift.
- Also note the IDF effect the example illustrates: required C# at idf 0.8 carries weight 1.6 — exactly equal to a *rare* nice-to-have skill. That is intended by IDF design, but it means one uncommon nice-to-have can outweigh a core requirement; keep an eye on it in the admin metrics (a nice-to-have should never beat all required skills combined — optionally clamp `SUM(w_nice) <= SUM(w_required)`).

## B-17 · Small but real

| # | Issue | Fix |
| --- | --- | --- |
| a | `resume_parse_logs` rate limit counts rows, not successful parses → failures burn the user's quota | count only `status IN ('ok','rejected')`, let `failed` (our error) be free |
| b | `saved_jobs`/`saved_searches` reference `candidate_profiles`, so users can't save anything before step 1 is saved | reference `user_id` for saves; keep candidate-scoped tables for matching only |
| c | `jobs.applications_count` + cap race: increment and status flip must be in the same locked transaction, and decrement on withdrawal must not reopen a `full` job while a studio is mid-review | single transaction with `SELECT … FOR UPDATE` on the job row (already required); add a test that withdraws then re-applies |
| d | `notification_preferences` has no row until the user toggles something → "email off by default for marketing" needs `NOT EXISTS` logic everywhere | insert default preference rows at registration |
| e | `skills.slug` unique + `aliases text[]` means "UE5" and "Unreal Engine 5" can both be typed and both be valid rows | resolve aliases at write time (skill resolver is shared with resume import); admin merge tool must rewrite child rows, not just deactivate |
| f | `app_settings.features.*` = false for everything → new modules are dark on deploy and someone forgets to flip them | per-feature "rollout" default in seed (`true` for the phase you're building), plus a startup banner listing every flag state |
| g | `websearch_to_tsquery` with user text: fine, but empty query must not become an `OR` of stopwords | short-circuit when trimmed query < 2 chars |
| h | Expiry job "extends by one click if they have responded" is one rule with two meanings (responded to *any* applicant vs *this* job's applicants) | define per-job: extend only if ≥ 60% of that job's applications have a first response |
