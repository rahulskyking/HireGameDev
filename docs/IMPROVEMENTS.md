# HireGameDev — Improvements and Nice-to-Haves

Ordered by the phase you listed. Each item says **what**, **why it matters here** (game-dev India, not generic jobs board), and **cost**. `[P]` = product feature, `[T]` = trust/quality, `[G]` = growth, `[O]` = ops/internal.

Legend: effort **S** ≤ half a day · **M** 1–2 days · **L** 3–5 days. Impact **H/M/L**. My recommendation column: **DO** = build it with that phase, **DO-LATER** = queue it, **SKIP** = I'd argue against it.

---

## Phase 0 — Login and accounts

| # | Item | Why | Effort | Impact | Rec. |
| --- | --- | --- | --- | --- | --- |
| 0.1 | `[P]` Google **and** GitHub linking with the merge rules in `PLAN.md` §2.2 | Game devs live on GitHub; studios are reached through Gmail. Auto-link on *verified* email only, otherwise you create exactly the duplicates you're trying to avoid | M | H | **DO** |
| 0.2 | `[P]` "Sign in with GitHub" pulls public profile → headline, portfolio links, top languages as *suggestions* on the review screen | Cheapest way to make a GitHub-first user feel the product knows them; no scraping, just the API you already got a token for | M | M | **DO** |
| 0.3 | `[P]` Unlink guard + recovery flow (never zero sign-in methods; token invalidation on reset) | The failure mode of every OAuth app is a user who can no longer get in; support cost is proportional to how rarely you test this | S | H | **DO** |
| 0.4 | `[T]` HIBP breach-password check (k-anonymity range API, no password leaves the box) | Identity has a hook; free privacy-preserving check; most Indian candidates reuse a leaked password | S | M | **DO** |
| 0.5 | `[T]` Passkey registration *schema and policy* now, UI in a later module | Adding `passkeys` after `users` has 5k rows is a migration + support problem; adding the table in M0 is nothing | S | M | **DO** (schema only) |
| 0.6 | `[O]` `user_sessions` with "sign out everywhere" and device list | Trust signal on a profile-sharing product; also the enforcement point for "invalidate tokens on password reset" | M | M | **DO** |
| 0.7 | `[P]` Studio invite-to-join before the studio exists ("claim your studio" with company-email domain + GSTIN) | A recruiter arriving via a colleague's invite is the highest-intent signup you will ever get | M | M | DO-LATER (M1, with studio onboarding) |
| 0.8 | `[P]` `signup_source` UTM + referral-code capture on the user row | Every later growth claim ("this Discord drove 200 candidates") needs a column that costs nothing now | S | M | **DO** |
| 0.9 | `[G]` Magic-link email sign-in alongside passwords | Lower friction on mobile for freshers; small with an email sender already built | M | L | DO-LATER |
| 0.10 | `[P]` MFA (TOTP) for admins only, mandatory | M0.10 of `PLAN.md`; admins can read PII and change settings — this is the one place MFA pays off immediately | S | H | **DO** |
| — | ~~Device fingerprinting / bot detection~~ | Cookies + rate limits + email verification cover it; fingerprinting on an Indian-hosted job site is a privacy liability disproportionate to the gain | — | — | **SKIP** |

## Phase 1 — Candidate and studio profiles

| # | Item | Why | Effort | Impact | Rec. |
| --- | --- | --- | --- | --- | --- |
| 1.1 | `[P]` **"Preview as studio"** — server-rendered with the *same* field filters a studio sees, plus a strip listing what is hidden and why | A preview that lies is worse than none; showing "phone hidden until you're shortlisted" teaches the privacy model and reduces support | M | H | **DO** |
| 1.2 | `[P]` "Preview as candidate" (what a candidate sees when they view a studio) — put this on `/Studio/Profile/Edit` | Studios under-report themselves; seeing their own public card gets better descriptions than any tooltip | S | M | **DO** |
| 1.3 | `[P]` Completeness checklist with weights shown ("+20 for a shipped title or portfolio link") and a **next-best-action** single CTA | The blueprint's weights add up to 100 — exposing them turns a progress bar into instructions | S | H | **DO** |
| 1.4 | `[P]` Discoverability switch with a plain-language state machine: `discoverable / hidden-except-applications / hidden-from-my-current-studio` | The #1 reason good Indian devs don't complete a profile is "my lead has LinkedIn". "Hide from studios I name" is what actually unlocks them | M | H | **DO** (add the block-list variant in M1: `profile_visibility` + `hidden_from_studios`) |
| 1.5 | `[T]` Portfolio embeds that render: itch.io iframe, ArtStation/ArtStation embed, GitHub repo card, YouTube/Vimeo reel | Game dev is visual; a dead `<a>` link is what every other board shows. One oEmbed/iframe layer makes the profile *feel* different | L | H | **DO** (links in M1, embeds in M1 as `link_type` renderers) |
| 1.6 | `[P]` NDA-title handling in the preview itself (show "Under NDA · Gameplay Programmer · 2023") so candidates see their own protection working | Trust for the exact people with the best credits | S | M | **DO** |
| 1.7 | `[P]` Per-skill "evidence" nudge inline (`Add proof for C# →` deep-links to the portfolio step) | The blueprint already computes `evidence_level`; showing the gap is what moves it | S | M | **DO** |
| 1.8 | `[T]` Demo reel: 60–90s video (YouTube/Vimeo unlisted OK) with an explicit "shown to studios after first contact" toggle | For art/design/audio QA roles a reel beats a resume; unlisted keeps it out of search | M | M | **DO** |
| 1.9 | `[P]` Resume import **diff view** on re-import ("your saved profile vs this resume", 3 buckets: keep / update / new) | Second-and-third uploads are where trust is lost if we silently overwrite; also catches stale resumes | L | M | DO-LATER (M1 if time, otherwise M4) |
| 1.10 | `[P]` Fresher track in onboarding: skip "current CTC", replace shipped titles with "class projects / game jam entries" (itch.io jam page counts as proof) | The largest underserved India segment and the cheapest supply of candidates; a shipped-title-only profile feels impossible at 0 years | M | H | **DO** |
| 1.11 | `[T]` Studio verification badge UI (unverified/pending/verified/rejected + "why") | Blueprint defers the *rules*; the field, the queue and the badge should exist from day one or M5 retrofit is painful | M | H | **DO** (badges visible; "verified" only from admin action) |
| 1.12 | `[O]` Admin note + evidence snapshot on each verification decision | Disputes will happen; a decision with no recorded basis can't be reviewed later | S | M | **DO** |
| — | ~~AI profile "improver" scoring the candidate's bio~~ | In M1 it's a liability (bias, generic advice); as a *suggestion* beside the resume diff in M7 it's useful | — | — | **SKIP now → M7** |

## Phase 2 — Jobs and search

| # | Item | Why | Effort | Impact | Rec. |
| --- | --- | --- | --- | --- | --- |
| 2.1 | `[P]` Save job **+ duplicate as draft** with "what changed since the last time" diff (salary, skills, openings) | Studios re-post the same 4 roles every quarter; the duplicate is the retention feature, the diff stops stale JDs | M | H | **DO** |
| 2.2 | `[P]` Pre-publish checks as a real checklist (not a modal): contact details in text, external apply link, salary spread, missing required skill, description too thin, engine mismatch with category | The blueprint's rules exist; a visible pre-flight that lists *what to fix* converts rejection into self-service | M | H | **DO** |
| 2.3 | `[T]` "No fees, ever" studio pledge + a scam-warning strip on every job page ("never pay for training, equipment or a security deposit") | Fee-based "game dev training then job" scams are endemic in India; a pledge is cheap, the warning protects candidates and the brand at the same time | S | H | **DO** |
| 2.4 | `[P]` Job quality meter (0–100) from structural checks — real salary range, team size, tech stack, "what you'll work on", interview stages | Gives the review queue a fast path and gives studios a target; also correlates with applications, which you can then show them | M | M | **DO** |
| 2.5 | `[P]` "Interview process preview" section on the job (stages, take-home y/n + paid?, panel size) | Reduces candidate anxiety and no-shows; it's the single most-requested thing on r/gamedev-style threads and almost no Indian board has it | S | H | **DO** |
| 2.6 | `[P]` Search: "joins within 30 days" and "immediately available" quick filters (uses notice period), plus frescher-friendly | Notice period is the practical blocker in Indian hiring; surfacing it as a filter makes the field worth filling | S | M | **DO** |
| 2.7 | `[P]` Salary transparency helper in the job form: show the studio the market band for that role+city+seniority *while typing* (start with a static table, upgrade to real benchmarks in M7) | Raises range quality now, and every accepted hint becomes a `salary_datapoints` row later | M | H | **DO** (static first) |
| 2.8 | `[P]` Saved searches with frequency control (`instant / daily / weekly / off`) and a "last sent + N new" row in the sidebar | You asked for digest control; note `saved_searches.frequency` already has `instant/daily/off` — adding `weekly` and `off` visibility is the whole change | S | M | **DO** |
| 2.9 | `[P]` Job alerts dedupe: never email a job the candidate already saw, viewed or was rejected from | Alert fatigue kills the only re-engagement loop you have at launch | M | H | **DO** |
| 2.10 | `[G]` Programmatic SEO pages `/jobs/{skill}/{city}` + JobPosting JSON-LD + `sitemap.xml` (blueprint) **plus** an `llms.txt` and clean `og:image` cards generated per job | WhatsApp is where Indian jobs actually circulate; the share card *is* your top-of-funnel. LLM crawlers increasingly answer "Unity jobs in Pune" | M | H | **DO** (share cards in M2, `llms.txt` S) |
| 2.11 | `[P]` "Similar jobs" rail on closed/expired/rejected job pages and in rejection emails | Turns every dead end into a click | S | M | **DO** |
| 2.12 | `[O]` Studio-side "recruiter view" metrics: views, applied, shortlisted, median response time on each job | Shows studios the cost of ghosting before we ever punish them for it | M | M | DO-LATER (M3/M4 with the pipeline) |
| — | ~~Boost/promote jobs in search~~ | Payments and paid ranking must not touch a product whose promise is "we respond"; it's M6 and it stays a labelled, non-score change | — | — | **SKIP now** |

## Phase 3 — Matching and applications

| # | Item | Why | Effort | Impact | Rec. |
| --- | --- | --- | --- | --- | --- |
| 3.1 | `[P]` **"Why this job matches you"** panel from the stored `match_breakdown`: matched skills, missing skills, work-mode/city fit, salary vs expectation, and "what would raise this" with a deep link to fix it | The blueprint stores the JSON; showing it is what makes a score trustworthy instead of a black box | M | H | **DO** |
| 3.2 | `[P]` Hard-filter reasons shown *positively* ("Excluded: this job needs UnrealCpp, you have UnrealBlueprints at working level") with a **"Show stretch matches"** toggle already in the spec | A filter with no explanation reads as a bug and quietly removes your best supply | S | H | **DO** |
| 3.3 | `[P]` Application timeline: every `application_status_history` row rendered with who changed it, when, and the next expected step | This is the anti-ghosting feature from the candidate's side — visibility, not just a reminder email | M | H | **DO** |
| 3.4 | `[P]` "What happens next" promise per stage that the studio sets once ("we reply within 7 days") + a live countdown on the application | A published SLA plus a visible clock is a stronger incentive than any admin warning; and it sets honest expectations when they miss it | M | H | **DO** |
| 3.5 | `[T]` Auto-decline email that recovers the candidate: 5 similar jobs + one-click apply-all-with-current-profile | The candidate is the one who did nothing wrong; treat day 14 as a re-engagement event, not a rejection | M | H | **DO** |
| 3.6 | `[P]` Match breakdown frozen at apply time + a small "your fit changed since you applied" note if the profile improved | Stops "score went down, so I stopped applying" confusion and quietly rewards profile completion | S | M | **DO** |
| 3.7 | `[P]` Screening answer shown inline on the applicant card, not behind a click | Studio reviewers are lazy; a hidden required answer is an unanswered required answer | S | M | **DO** |
| 3.8 | `[P]` Bulk actions with a guard: shortlist/reject N, but if N ≥ 10 and none of the profiles were opened, require a reason and flag `studio.bulk_reject_flag` (blueprint) — show the studio *why* before they do it | Anti-gaming that's honest instead of punitive: you tell them the rule before you enforce it | M | H | **DO** |
| 3.9 | `[T]` Response-rate badge shown on job cards and studio pages with the raw numbers on hover ("18 of 20 answered in 14 days") | A badge with no numbers is marketing; with numbers it's accountability | S | H | **DO** |
| 3.10 | `[P]` Candidate "express interest" button (no application) for "I'd move for the right team" — creates a lead, not an application | Lets passive demand show up in a studio's inbox without polluting application counts or caps | M | M | DO-LATER (M5 with invites) |
| 3.11 | `[O]` Match quality dashboard for you: apply→shortlist rate by band, filter-warning share, per-algorithm version | The blueprint logs `match_events`; without a table to read it, nobody looks at it | M | H | **DO** (admin page, not charts — a table + CSV export) |
| — | ~~Chat inside applications in M3~~ | Belongs with invites in M5; a chat with no verified counterparty is where scams start | — | — | **SKIP now** |

## Phase 4 — Launch and admin

| # | Item | Why | Effort | Impact | Rec. |
| --- | --- | --- | --- | --- | --- |
| 4.1 | `[P]` Notification inbox with per-type **email vs in-app vs both** controls and a global "email digest: daily / weekly / off" | You asked for this; it also makes opt-out granular, which reduces full churn vs one big unsubscribe | M | H | **DO** |
| 4.2 | `[P]` Report this job/profile/studio with reason codes + a status page for the reporter ("we paused this job on 12 Nov") | Closing the loop is what makes reporting worth doing; a queue that never replies gets no reports and a bad reputation anyway | M | H | **DO** |
| 4.3 | `[T]` Auto-pause rules: fee/training language, contact details in the description, duplicate-from-banned-studio → immediate `pending_review` (not `active`) + studio notified | A launch-day scam story costs more than every false positive combined | M | H | **DO** |
| 4.4 | `[O]` Admin review queues (jobs, verifications, reports) as one page with keyboard triage (j/k, approve/reject + reason, saved views) | You will spend hours here during launch weeks; 3 extra days of UI pays for itself | M | M | **DO** |
| 4.5 | `[O]` `admin.view_as` read-only with reason, 15-min expiry, POST-blocked, banner (blueprint) — **plus** an expiry countdown and a "report myself" button | Debugging "I can't apply" tickets is impossible otherwise; the countdown prevents the scary lingering session | M | M | **DO** |
| 4.6 | `[P]` System status page (`/status`) + maintenance banner + feature-flag visibility | On a single VPS, an honest status page is cheaper than a support thread | M | M | DO-LATER |
| 4.7 | `[G]` Launch-only "reply-by" streak leaderboard for studios (private, opt-in, no naming-and-shaming) | Positive framing of the same metric we already compute | S | L | DO-LATER |
| 4.8 | `[P]` Feedback widget on every page capturing page URL + consent to be contacted | Blueprint requires it; wiring the page URL turns anecdotes into a searchable list | S | M | **DO** |
| 4.9 | `[T]` DPDP: consent versions (`legal_documents`), data export, 30-day purge job, breach checklist file in repo | The export and delete jobs are cheap now and painful to retrofit after real PII exists | L | H | **DO** |
| — | ~~Full BI dashboards (Metabase etc.)~~ | Postgres + `match_events` + SQL views is enough until there's real volume; ship a table with CSV export | — | — | **SKIP** |

## Phase 5 — Trust and two-way

| # | Item | Why | Effort | Impact | Rec. |
| --- | --- | --- | --- | --- | --- |
| 5.1 | `[P]` Interview scheduling with proposed slots, ICS attachments for both sides, explicit zone labels ("14:30 IST · your 14:30") and a reschedule flow | Time-zone confusion is a real problem for remote-India + overseas-studio roles; ICS generation is ~half a day | M | H | **DO** |
| 5.2 | `[T]` No-show tracking both directions + a visible history after 3 interviews | Symmetric accountability; the metric that makes "professional" measurable | M | M | **DO** |
| 5.3 | `[T]` Evidence steps for shipped titles: Steam/itch.io/Play Store verifiers **in addition to** IGDB (see `BLUEPRINT-FIXES.md` B-12) | IGDB alone will fail most Indian mobile and NDA titles, so the badge would only exist for AAA-adjacent candidates | L | H | **DO** |
| 5.4 | `[T]` Credit confirmation via a `claim-studio` path (one studio account confirms many credits in a bulk review screen) | The bottleneck of coworker-confirmation is studio laziness; a bulk UI for the owner turns it into 5 minutes of work | L | H | **DO** (bulk screen is the feature) |
| 5.5 | `[P]` Invites with a reason the studio must give ("we saw your shader work on X") and a candidate-visible accept/decline with a "why not" optional reason | Generic invites are LinkedIn spam with extra steps; requiring one sentence filters the worst and teaches you what studios want | M | H | **DO** |
| 5.6 | `[P]` Conversation with **structured** first message (attachment = profile snapshot, not a file) + a link to the job, never free-form contact details | Keeps contact release rules enforceable while chat exists; a chat that can paste a phone number makes the whole privacy model decorative | M | H | **DO** |
| 5.7 | `[P]` Time-zone-aware availability from the candidate ("available for calls 18:00–22:00 IST") so slots arrive pre-filtered | Students and employed devs can only interview evenings; this is a match-rate feature for the India segment | M | M | DO-LATER (5.1 if time) |
| 5.8 | `[T]` Reference/exit note: after a hire, ask both sides for a 2-question review (see 7.6) | Turns completed hires into the trust data no competitor has | M | M | DO-LATER |
| — | ~~Video interviews inside the product~~ | Meetings links are fine; owning WebRTC on a single VPS is a support nightmare with zero hiring value | — | — | **SKIP** |

## Phases 6–8 — Monetization, growth, scale

| # | Item | Why | Effort | Impact | Rec. |
| --- | --- | --- | --- | --- | --- |
| 6.1 | `[P]` **Studio plan & usage dashboard** (jobs used, profile views, invites, seats, response rate vs peers) *before* adding payment methods | You asked for this, and it's right: a usage page creates the desire to upgrade long before a payment screen does | M | H | **DO first in M6** |
| 6.2 | `[P]` Job credits that expire in 30 days but *pause* rather than vanish on downgrade (never delete a studio's job) | Trust-preserving billing; the dunning rule in the blueprint already says this — keep it literal | S | M | **DO** |
| 6.3 | `[P]` Candidate Premium that never touches ranking fairness: "who viewed me", alert early-access, salary insights, profile feedback | Pay for information, not for position — if candidates can buy rank, studios stop trusting scores and the marketplace dies | M | H | **DO** |
| 6.4 | `[P]` Free Pro for any studio that hires through the platform | Converts the one moment of maximum goodwill into retention, and makes "we got you a hire" the deal | S | M | **DO** |
| 6.5 | `[O]` Invoice + GST PDF and a Razorpay webhook replay test | Accounting reality in India; webhook idempotency is the acceptance criterion in the blueprint | M | M | **DO** |
| 7.1 | `[G]` Salary benchmarks page (bucket with ≥5 datapoints, show ranges only, per city+role+seniority) | The highest-traffic SEO page a job board can own; also the reason candidates return monthly | L | H | DO-LATER |
| 7.2 | `[G]` Skill badges from a real check (a 15-min timed Unity/GDScript quiz; or a "shipped a playable build" review) | Verification that adds rank value without adding bias; keep it *not* required for any job | L | M | DO-LATER |
| 7.3 | `[G]` Layoff-response mode (opt-in flag + prioritized verified-studio list + a real human writing the list) | Game-industry layoffs arrive in clusters; a pre-built response is the moment this product earns loyalty | M | H | **DO the schema hook now**, UI when needed |
| 7.4 | `[G]` "Team preview" on the job (members who accepted a consent request) | Strongest small-studio differentiator; consent-gated so it can't become surveillance | M | M | DO-LATER |
| 7.5 | `[G]` Referral link with a credit after a *confirmed* hire | Cheap, but reward only on outcomes or it becomes a signup-farm vector | M | L | DO-LATER |
| 7.6 | `[T]` Two-question interview-experience review per application, published only at ≥3 reviews per studio | Best trust content per unit of effort; and it's feedback studios actually change behaviour over | M | M | DO-LATER (start collecting in M5) |
| 8.1 | `[T]` ML re-ranking behind the rule-based score (blueprint's thresholds are sane: ~5k shortlist events) | Keep the displayed percentage rule-based — explainability is the product | L | M | DO-LATER |
| 8.2 | `[P]` Public API + ATS import (Greenhouse/Lever/Ashby job feeds) for studios that already have an ATS | Real adoption lever for 50+ person Indian studios; keep read-mostly and scoped | L | M | DO-LATER |
| — | ~~Native mobile apps~~ | A responsive PWA covers India's mobile majority at a fraction of the cost; two app stores are a distraction until hiring is proven | — | — | **SKIP → PWA** |
| — | ~~Investor/publisher module~~ | Keep it separate and last exactly as the blueprint says; securities exposure needs a lawyer first | — | — | **DEFER (unchanged)** |

---

## The five I'd insist on, if you want a short list

1. **`[T]` Anti-scam surface** — no-fees pledge, scam strip on every job, auto-pause on fee language (2.3, 4.3). This is the difference between "useful Indian job board" and "another LinkedIn mess".
2. **`[P]` Interview-process preview on jobs** (2.5) — one extra textarea, disproportionately what candidates say they want.
3. **`[P]` Hide-my-profile-from-my-current-studio** visibility mode (1.4) — unlocks employed mid/senior supply, which is what studios actually want to hire.
4. **`[P]` Response SLA with a countdown + candidate-visible status timeline** (3.3, 3.4) — makes the anti-ghosting promise felt instead of merely enforced.
5. **`[G]` Share cards + `llms.txt`** (2.10) — distribution through WhatsApp and answer engines is the free growth channel at launch.

And one **structural** suggestion: build the matching engine (M3) with a **fixture-driven test harness** — `tests/fixtures/*.json` containing candidate-snapshot + job-snapshot pairs with expected scores. The blueprint's golden-test idea, but as data files, so non-engineers (you) can add cases without touching code, and so the algorithm can be re-verified after every weight change from `app_settings`.
