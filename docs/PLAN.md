# HireGameDev — Master Build Plan (v1.0)

Stack-agnostic planning layer that sits **on top of** [`BLUEPRINT.md`](./BLUEPRINT.md) (Build Blueprint v2.0, copied into this repo for reference). This file is the version we iterate on; the blueprint stays as the frozen reference document.

**Read order:** `PLAN.md` → `DESIGN.md` → `IMPROVEMENTS.md` → `DECISIONS.md` → the relevant blueprint phase section.

---

## 0. Ground rules (carried over from the blueprint, unchanged)

1. One module at a time. A module is "done" only when: it compiles, migrations apply to an empty database, authorization is proven (user A cannot read or change user B's data), validation errors are rendered, layout is checked at 360px, no secrets in code.
2. Never start the next step on a red build. Red = failing test, failing type-check, or failing migration.
3. Every deviation from `BLUEPRINT.md` gets one line in `DECISIONS.md` with a reason.
4. No new columns invented silently — schema changes are proposed, recorded as a decision, then implemented.
5. Tests are written for the rules *before* the UI that uses them.

---

## 1. Module map

Modules are numbered M0–M8 and map to the blueprint's phases. Your requested additions (preview profile, duplicate job, match explanations, notification inbox, scheduling, etc.) are folded in as **ADDED** items so nothing is lost.

| Module | Blueprint | Scope | Adds from your list | Est. steps |
| --- | --- | --- | --- | --- |
| **M0** | Phase 0 | Repo/solution skeleton, config, DB, Identity, Google + GitHub linking, recovery, unlink, design system, legal pages, audit log, settings, seed master data | Google+GitHub dual provider, explicit unlink + recovery flow, passkey-ready schema | 14 |
| **M1** | Phase 1 | Candidate + studio profiles, onboarding wizard, completeness, evidence levels, resume AI import, studio verification basics | "Preview as studio / as candidate", completion checklist, discoverability toggle | 10 |
| **M2** | Phase 2 | Jobs, lifecycle, admin review queue, search, filters, SEO | Save + duplicate as draft, pre-publish checks, saved-search digest frequency | 9 |
| **M3** | Phase 3 | Applications, matching v2, anti-ghosting, studio pipeline | "Why this job matches you", candidate application timeline | 11 |
| **M4** | Phase 4 | Dashboards, notifications + inbox, reports, admin panel, DPDP, launch hardening | Notification inbox, email vs in-app controls, report actions + admin review queue | 10 |
| **M5** | Phase 5 | Invites, conversations, interview scheduling, title verification, reciprocal ranking v3 | Calendar invites with clear time zones, evidence/verification steps | 12 |
| **M6** | Phase 6 | Plans, entitlements, Razorpay, invoices, boost, studio analytics | Studio plan/usage dashboard before extra payment options | 10 |
| **M7** | Phase 7 | Benchmarks, challenges/badges, layoff response, referrals, reviews, AI assistants | Salary insights, referrals, skill badges | 8 |
| **M8** | Phase 8 | ML re-rank, S3/CDN/read replica, public API, ATS sync, PWA/native, investor module | Keep public API / ATS / mobile here | 6 |

Sizing rule: **1 step ≈ 1 commit ≈ one AI session.** If a step needs more than ~8 files, split it.

---

## 2. Module 0 — Foundation and accounts (build this first)

Goal: a deployed, boring, production-grade shell where a person can sign in with Google **or** GitHub, end up on **one** account, recover access, and unlink a provider safely — with the look and feel locked before any feature work starts.

### 2.1 Steps

| # | Step | Definition of done |
| --- | --- | --- |
| 0.1 | Repo + toolchain: gitignore, editorconfig, package/project layout, Node/`.csproj` config, env-var convention, `.env.example` | Clean install; `verify` script runs typecheck + tests + migrations check in one command |
| 0.2 | Design system (see `DESIGN.md`): tokens, light/dark/auto themes, primitives (button, input, card, chip, badge, toast, dialog, table, tabs), Inter + Space Grotesk self-hosted | Story-like `/dev/ui` page with every component in every state, 360px + 1440px, keyboard-only walkthrough |
| 0.3 | Layout shell: header/nav, footer, cookie banner, theme switcher, flash messages, feedback link, 404/500/403 pages | Theme choice persists; no layout shift on toggle; nav collapses correctly |
| 0.4 | Database foundation: extensions (`citext`, `pg_trgm`), snake_case, `gen_random_uuid()`, `timestamptz`, `created_at/updated_at` trigger or ORM hook, soft-delete filter, migration runner | Migration applies on empty DB; second run is a no-op |
| 0.5 | Users + roles: `users` with `account_type`, `display_name`, `is_active`, `age_confirmed_at`, `deleted_at`; policy helpers `CandidateOnly`, `StudioMember`, `AdminOnly` | Policy tests: 403 for wrong role, redirect for anonymous |
| 0.6 | Email+password register/login: account type choice, 18+ confirm, terms+privacy consent capture, email confirmation, lockout, rate limits (5/15min per IP on login, 5/hour per IP on register) | Consent rows written; brute force locks; abuse returns 429 |
| 0.7 | **External providers (Google + GitHub) with account linking** — the real M0 feature, see §2.2 below | Same email on Google and GitHub = one user id |
| 0.8 | **Unlink + recovery**: unlink guard, verify-email-first linking, forgot-password, change-password-everywhere, session list with revoke | Cannot end up with zero sign-in methods |
| 0.9 | Passkey/MFA **readiness only** (no UI): `user_credentials`-shaped table or provider-based 2FA column, policy stub | A later module adds WebAuthn without changing `users` semantics |
| 0.10 | `app_settings` + `ISettingsService` (60s cache) seeded with every key from blueprint §Phase 0 | Admin can change a limit and see it take effect without redeploy |
| 0.11 | `audit_logs` + `IAuditLogger`; helper for actor/IP/entity/reason; `admin.view_as` shape ready | Every later admin action has one call to make |
| 0.12 | Seed master data: ~40 cities (8 hubs flagged), 130+ skills from CSV with aliases, 30–40 `skill_relations` | Re-seedable, idempotent, `--force` refresh |
| 0.13 | Legal + DPDP: Terms, Privacy, Grievance, consent-record view, download-my-data (JSON) + delete-my-account (30-day grace) skeletons | Consent versions recorded against the legal page version |
| 0.14 | Deploy + ops: Nginx+TLS, systemd/PM2, health check, Serilog/file logging with request id, nightly `pg_dump` off-site, **restore drill performed once**, CI running verify | Restored DB from a backup on a fresh instance |

### 2.2 Provider linking — the exact rules to implement

This is where duplicate accounts are born, so the rules are written out.

1. **Auto-link only on a verified provider email.** If the provider returns `verified_email = true` and that email exists on a local account that has no provider of that type → attach, log `user.provider_linked`, sign the user in. Never auto-link an unverified email (GitHub frequently returns a non-deliverable noreply address).
2. **Conflict** (email exists and already has a Google or GitHub provider) → do **not** create a second user. Show: "This email already has an account. Sign in with your password to link Google" → after password sign-in, attach the provider.
3. **New user** → create the account with `account_type` chosen on the picker; skip password setup; force email confirmation only if the provider email was unverified.
4. **Manual link from `/Account/Manage`** → OAuth flow started from a signed-in session carries a `link` state parameter; after callback, apply rules 1–2 in link mode (attach instead of sign-in).
5. **Unlink** allowed only if at least one of these remains: a password is set, another provider is linked, or passkeys exist. Otherwise show why and offer "set a password first" inline. Log `user.provider_unlinked` with reason.
6. **Email change** on the local account requires confirming the new address and clears `phone_verified_at`-style trust flags (a new email is not pre-verified). Re-linking providers is asked for after the change.
7. **Account recovery** = email reset link (15-minute single-use token, invalidate all other tokens on use) + a support/attestation path for a lost Google account: re-verify via the remaining provider, or grievance-officer email with ID proof, logged as an admin action. No phone-based recovery in M0.
8. `user_logins` unique on `(login_provider, provider_key)`, and a service-level check that one provider key never maps to two users.

### 2.3 Schema deltas to accept in M0

```
user_identities  (id, user_id FK, provider text, provider_key text, email text, email_verified bool,
                  raw_profile jsonb, linked_at, linked_by_user_id, UNIQUE(provider, provider_key))
user_sessions    (id, user_id, ip, user_agent, created_at, last_seen_at, revoked_at)   -- session list + revoke
passkeys         (id, user_id, credential_id text UNIQUE, public_key text, sign_count bigint,
                  transport text, name text, created_at, last_used_at)                  -- table only; UI in a later module
legal_documents  (id, kind text CHECK IN ('terms','privacy'), version text, body text, published_at, is_current)
consent_records  ... + (document_id int REFERENCES legal_documents)                     -- proves *which* text was accepted
```

Plus one **blocking bug fix** in the blueprint schema, detailed in `DECISIONS.md` D-001: `users.account_type` as a single enum cannot express "candidate who joins a studio as a recruiter", which is a normal situation in this market. Resolution: `account_type` becomes the *default landing surface*, real authority is `candidate_profiles` + `studio_members`, and a policy accepts either membership.

### 2.4 Acceptance for M0

- Register with Google on a Gmail address, then sign in with GitHub using the same email → one user id, one profile, no duplicate row.
- Try to unlink the only provider → blocked with an explanation; set a password → unlink succeeds.
- Brute-force login (10 attempts) → lockout + 429; attempt recorded in audit log.
- Delete account request → 30-day grace, row anonymized, files gone, login immediately impossible.
- A no-JS browser can still log in; keyboard-only user can complete registration; neon yellow is never used for body text.
- Lighthouse mobile ≥ 90 on `/` and legal pages; the whole app passes axe with zero critical violations.
- Backup file restores a fresh database in under 15 minutes (documented timings in `DECISIONS.md`).

---

## 3. Modules 1–8 (planned, to be expanded one at a time)

Each keeps the blueprint's build order and adds your items. Expanded into per-step tables at the start of each module (that is deliberate: no big-bang plan that goes stale).

- **M1 Profiles** — blueprint steps 1–7 plus: profile preview as-studio-sees-it (`/Candidate/Profile?as=studio`, server-rendered with the exact same field filters production studios get, so the preview cannot drift), completeness checklist with per-item weight and the next best action, `is_discoverable` control with a plain-language explanation of what turns off, studio-side "preview a candidate's public card".
- **M2 Jobs** — blueprint steps 1–7 plus: duplicate-as-draft, pre-publish check list (all red items must clear before submit), `saved_searches.frequency` extended with `weekly` and send-time in Asia/Kolkata, and the admin review queue promoted from M4 so job review is never blocked on the panel.
- **M3 Matching** — blueprint steps 1–7 plus: the "Why this matches" panel driven by the stored `match_breakdown` JSON (never recomputed for display), and a candidate application timeline from `application_status_history` including system events (auto-decline) with the reason stated plainly.
- **M4 Launch** — dashboard, notification inbox with per-type email vs in-app switches, reports + admin review queue, DPDP completion, load test.
- **M5 Trust** — invites, conversations, scheduling with ICS + explicit time zone labels, IGDB/store-URL verification, credit confirmation, reciprocal ranking v3 behind a flag.
- **M6 Monetization** — entitlement refactor first (no payments), then Razorpay, then a plan/usage dashboard for studios, then invoices.
- **M7 Growth** — benchmarks, badges/challenges, layoff response, referrals, reviews. One at a time, each behind a flag, each measured.
- **M8 Scale** — ML re-rank, S3/CDN, public API + ATS integrations, PWA/native, investor module.

---

## 4. Cross-cutting commitments

- **Every ranking decision is logged** (`match_events`), from M3 onward — day one of the training corpus.
- **Feature flags in DB** (`features.*`) from M0, so anything risky can be dark-launched.
- **Time zone policy:** store UTC, render Asia/Kolkata unless the user is outside India, and always label the zone on dates in emails/ICS.
- **Money:** salaries `numeric` LPA, payments `bigint` paise, never floats; one shared normalization helper, unit-tested.
- **PII:** resume text and phone numbers never logged; contact details released only after shortlist; current CTC hidden by default.
- **Perf budget:** search under 200ms at 1,000 jobs (EXPLAIN checked), TTFB under 300ms on the VPS, JS shipped under 90KB on public pages.

## 5. Verified during planning (not assumptions — executed)

Run against a real PostgreSQL 18 engine in this workspace:

| Check | Result |
| --- | --- |
| Phase 0 DDL (6 tables, 5 indexes, `citext`, `pg_trgm`, JSONB, `inet`, partial + GIN indexes, `gen_random_uuid()`) applied to an empty database | ✅ applies cleanly |
| `CHECK` constraints reject bad enum values (`skills.category`) and bad relation rows (self-reference, `weight > 1`) | ✅ both rejected as designed |
| `citext` case-insensitive lookup, `text[]` alias containment, `pg_trgm` fuzzy match for the typo "blende" → Blender (0.67) | ✅ all work |
| Phase 2 `search_vector` generated column + `websearch_to_tsquery` ranking, and the trigram title fallback for "unrity" → Unity | ✅ both work |
| Phase 3 worked example (`BLUEPRINT.md:779`) — skill 52.7, experience 100, logistics 98.7 | ✅ reproduces exactly |
| Phase 3 `fit_for_studio` | ⚠️ reproduces as 73.7 **only** with round-each-component-first; naive rounding gives 73.6 → see `BLUEPRINT-FIXES.md` B-16 / `DECISIONS.md` D-015 |

That last row is the reason D-015 exists, and the reason the golden test is written as data (`tests/fixtures/`) rather than as prose.

## 6. Open risks

| Risk | Mitigation |
| --- | --- |
| Empty marketplace at launch (supply first) | Hand-onboard 20 studios before any candidate marketing; jobs with zero applicants after 14 days get the studio a "why" email, not a dashboard |
| Studios game response metrics | Only human status changes count (blueprint); bulk-reject-without-viewing flagged in audit log |
| AI provider trains on resume data | Paid tier, no-training terms, redaction before the call, nothing stored |
| Verification promises we can't keep | Ship GST/format + email-domain checks only in M1; IGDB/store checks in M5; never call it "verified" before an admin says so |
| Scope creep toward social network | Community/jam features stay in M7+ and are cut first if hire metrics stall |
