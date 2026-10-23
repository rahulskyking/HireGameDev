# HireGameDev – Build Blueprint v2.0

Database design · Matching and anti-ghosting algorithms · Field design · Phased build plan for AI-assisted development

Stack: ASP.NET Core 8 Razor Pages · EF Core (Npgsql) · PostgreSQL 16 · SignalR · Hangfire · Bootstrap 5 + custom CSS. This document supersedes Project Specification v1.0 and has the fixes from the spec review built in.

## 1. How to Use This Document With AI

1. Build one phase at a time. Every phase (Section 4 onward) is self-contained: tables, rules, pages, services and acceptance checks.
2. For each AI session paste Section 2 (overview), Section 3 (conventions) and only the phase you are building. Never paste the whole document.
3. Inside a phase, follow its Build Order list. Each numbered step is one good prompt.
4. After every step: run the app, run tests, commit. Do not start the next step on a red build.
5. Keep a DECISIONS.md in your repo. Whenever you or the AI deviate from this document, add one line there.

**Master prompt template**

```text
You are a senior ASP.NET Core 8 engineer helping build HireGameDev, a hiring platform for game developers in India.
Stack: Razor Pages, EF Core + Npgsql, PostgreSQL, ASP.NET Identity, Bootstrap 5 + custom CSS. No JS frameworks.
Follow these conventions exactly: [paste Section 3]
Product context: [paste Section 2]
Current task: Phase N, step M - [paste the step]
Relevant schema: [paste the phase tables]
Rules: write EF Core entities + configurations (snake_case), a migration, services behind interfaces, Razor Pages with server-side validation, and xUnit tests.
Authorize every page and check resource ownership. Ask me before inventing fields that are not in the schema.
Output: list of files first, then the code for each file.
```

**Definition of done for every step:** compiles; migration applies on an empty database; authorization verified (user A cannot read or change user B's data); validation errors are shown; layout checked at 360px width; no secrets in code.

## 2. Product Overview

**Roles:** Candidate · Studio member (owner, recruiter, viewer) · Admin. An Investor role is a later, separate module (Phase 8).

**Design principles (from the review):**

- Hard filters first, scores second. Dealbreakers (engine, work mode, salary ceiling, notice period) remove a job from recommendations instead of lowering its score.
- Skills are tied to evidence. A skill backed by a shipped title, portfolio item or badge counts more than a bare claim.
- Studios earn their response metrics. Only real human status changes count as a response.
- Contact details (phone, email) are released only after mutual interest or shortlist.
- Current CTC is hidden from studios by default. Studios see expected CTC.
- Launch free for studios. Payments arrive only after real hires happen (Phase 6).
- Every ranking decision is logged so the algorithm can be tuned and later trained.

**Phase map**

| Phase | Name | Timing | Outcome |
| --- | --- | --- | --- |
| 0 | Foundation | Weeks 1–2 | Deployed skeleton: auth, layout, settings, audit log |
| 1 | Profiles and Resume AI | Weeks 3–4 | Candidates and studios create complete profiles |
| 2 | Jobs and Search | Weeks 5–6 | Studios post jobs, candidates search and filter |
| 3 | Applications and Matching | Weeks 7–8 | Apply flow, match score, anti-ghosting |
| 4 | Dashboards, Notifications, Admin, Launch | Weeks 9–13 | Public free launch |
| 5 | Trust and Two-Way | Months 4–6 | IGDB, credit verification, invites, chat, scheduling, reciprocal ranking |
| 6 | Monetization | Months 7–9 | Plans, Razorpay, boost, pay-per-job, analytics |
| 7 | Growth | Months 10–15 | Salary benchmarks, skill badges, layoff response, team preview, referrals, reviews |
| 8 | Scale and Ecosystem | Months 16+ | ML ranking, API, mobile, investor module |

## 3. Architecture and Conventions

**Solution layout**

```text
HireGameDev/
├─ src/
│  ├─ HireGameDev.Web/            Razor Pages, Hubs, ViewComponents, wwwroot (css/js)
│  │   ├─ Pages/ (Account, Candidate, Studio, Jobs, Applications, Admin, Legal, Shared)
│  │   ├─ Hubs/NotificationHub.cs
│  │   └─ wwwroot/css/ (tokens.css, base.css, components.css, pages/*.css)
│  ├─ HireGameDev.Core/           Entities, enums, service interfaces, Matching engine (no EF, no HTTP)
│  └─ HireGameDev.Infrastructure/ DbContext, migrations, services, Resend, Razorpay, Gemini, IGDB clients
└─ tests/
   ├─ HireGameDev.Core.Tests/     matching + rules unit tests
   └─ HireGameDev.Web.Tests/      integration tests (Testcontainers PostgreSQL)
```

**NuGet packages:** Npgsql.EntityFrameworkCore.PostgreSQL · EFCore.NamingConventions · Microsoft.AspNetCore.Identity.EntityFrameworkCore · Microsoft.AspNetCore.Authentication.Google · Hangfire.AspNetCore + Hangfire.PostgreSql · UglyToad.PdfPig · SixLabors.ImageSharp · FluentValidation · Serilog.AspNetCore · Razorpay (Phase 6) · xUnit + Testcontainers.PostgreSql.

**Database conventions**

- PostgreSQL 16, snake\_case via `UseSnakeCaseNamingConvention()`. Identity tables are renamed to `users`, `roles`, `user_roles`, `user_logins`, `user_claims`, `user_tokens`, `role_claims`.
- Primary keys: `uuid` (default `gen_random_uuid()`) for business tables so ids in URLs are not guessable. `int` identity for lookup tables (cities, skills). `bigint` identity for high-volume logs.
- Enums are stored as `text` with a CHECK constraint and mapped in C# with `HasConversion<string>()`. This keeps migrations simple and data readable.
- All timestamps are `timestamptz`, stored in UTC. Every business table has `created_at` and `updated_at` (set in `SaveChanges` override).
- Money: salaries are `numeric(6,2)` in LPA. Payments are `bigint` paise (never floats).
- Soft delete (`deleted_at`) on candidate\_profiles, studio\_profiles and jobs. Global query filter in EF. Erasure under DPDP means anonymizing the row and removing files, not keeping the data.
- Extensions: `citext`, `pg_trgm`.

**Application conventions**

- Razor Pages with PageModel per page, thin handlers, logic in services (`ICandidateService`, `IJobService`, ...). No business logic in `.cshtml`.
- Authorization policies: `CandidateOnly`, `StudioMember`, `StudioRecruiter`, `AdminOnly`. Every handler that loads a record by id must also check ownership (candidate owns profile; studio member belongs to the studio that owns the job).
- Validation: FluentValidation + data annotations, always server-side. Client validation is a convenience only.
- Anti-forgery on all POST handlers. Rate limiting with ASP.NET Core `AddRateLimiter` on login, register, apply, resume-parse.
- Uploads: whitelist extensions, check magic bytes, 2 MB max for PDF, 1 MB for images (resized with ImageSharp), random file names, stored outside wwwroot and served through an authorized endpoint.
- Logging: Serilog to file with a request id. Never log tokens, passwords, resume text or phone numbers.
- Configuration: secrets in environment variables (systemd `EnvironmentFile`), never in the repo.

## 4. Phase 0 – Foundation (Weeks 1–2)

**Goal:** an empty but production-grade app deployed on the VPS with login working.

### Tables introduced

```sql
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- users: created by ASP.NET Identity (ApplicationUser : IdentityUser<Guid>). Extra columns:
--   account_type text NOT NULL CHECK (account_type IN ('candidate','studio','admin'))
--   display_name text NOT NULL
--   is_active boolean NOT NULL DEFAULT true
--   age_confirmed_at timestamptz            -- 18+ confirmation (DPDP)
--   last_login_at timestamptz
--   created_at timestamptz NOT NULL DEFAULT now()
--   deleted_at timestamptz
-- Phone number is NOT stored on users; it lives on candidate_profiles (see Phase 1).

CREATE TABLE cities (
  id          int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  name        citext NOT NULL,
  state       text   NOT NULL,
  country_code char(2) NOT NULL DEFAULT 'IN',
  is_hub      boolean NOT NULL DEFAULT false,   -- Pune, Bangalore, Hyderabad, Mumbai, Chennai, Noida, Kochi, Ahmedabad
  sort_order  smallint NOT NULL DEFAULT 100,
  UNIQUE (name, state)
);

CREATE TABLE skills (
  id          int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  name        citext NOT NULL UNIQUE,
  slug        text   NOT NULL UNIQUE,
  category    text   NOT NULL CHECK (category IN ('engine','language','art_tool','design_audio_tool','devops','genre','platform','role')),
  aliases     text[] NOT NULL DEFAULT '{}',      -- 'UE5','Unreal Engine 5'
  idf_weight  numeric(4,2) NOT NULL DEFAULT 1.00, -- recomputed nightly (Phase 3)
  is_active   boolean NOT NULL DEFAULT true,
  created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_skills_category ON skills (category) WHERE is_active;
CREATE INDEX ix_skills_name_trgm ON skills USING gin (name gin_trgm_ops);

-- Partial-credit graph. Meaning: a candidate who HAS related_skill_id earns `weight` credit
-- for a job that requires skill_id. Examples: (UnrealBlueprints <- UnrealCpp 0.70), (Maya <- Blender 0.50), (Unity <- Godot 0.35)
CREATE TABLE skill_relations (
  skill_id         int NOT NULL REFERENCES skills(id),
  related_skill_id int NOT NULL REFERENCES skills(id),
  weight           numeric(3,2) NOT NULL CHECK (weight > 0 AND weight <= 1),
  PRIMARY KEY (skill_id, related_skill_id),
  CHECK (skill_id <> related_skill_id)
);

CREATE TABLE app_settings (
  key         text PRIMARY KEY,
  value       jsonb NOT NULL,
  description text,
  updated_by  uuid REFERENCES users(id),
  updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE audit_logs (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  actor_user_id uuid REFERENCES users(id),
  actor_type    text NOT NULL DEFAULT 'user' CHECK (actor_type IN ('user','admin','system')),
  action        text NOT NULL,          -- 'job.approve','user.suspend','admin.view_as'
  entity_type   text NOT NULL,
  entity_id     text NOT NULL,
  reason        text,                   -- mandatory for admin actions
  before_data   jsonb,
  after_data    jsonb,
  ip            inet,
  created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_audit_entity ON audit_logs (entity_type, entity_id);
CREATE INDEX ix_audit_actor  ON audit_logs (actor_user_id, created_at DESC);

CREATE TABLE consent_records (
  id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  user_id      uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  consent_type text NOT NULL CHECK (consent_type IN ('terms','privacy','cookies','ai_processing','marketing')),
  version      text NOT NULL,
  accepted_at  timestamptz NOT NULL DEFAULT now(),
  ip           inet
);
CREATE INDEX ix_consent_user ON consent_records (user_id, consent_type);
```

### Seed data

- **cities:** about 40 Indian cities, with the eight hubs flagged. Add a `Remote (India)` pseudo-entry only in filter UI, not as a city row.
- **skills:** the 130+ skill master list (engines, languages, art tools, design/audio tools, devops, genres, platforms, role categories) with aliases. Import from a CSV so admins can re-seed.
- **skill\_relations:** start with 30–40 hand-picked pairs.
- **app\_settings** keys to seed (all editable in the admin panel without a deploy):

| Key | Default | Meaning |
| --- | --- | --- |
| limits.apply\_per\_day | 15 | Applications per candidate per day |
| limits.max\_active\_applications\_per\_job | 50 | Applicant cap per job (job closes to new applications when reached) |
| limits.free\_active\_jobs | 3 | Free studio tier job limit |
| limits.resume\_parse\_per\_day | 5 | AI resume extractions per user per day |
| limits.profile\_views\_per\_studio\_per\_day | 100 | Studio candidate-profile views per day |
| job.expiry\_days | 30 | Auto-expiry |
| job.salary\_max\_spread | 1.5 | Max salary\_max / salary\_min |
| antighost.reminder\_days | 7 | Reminder to studio |
| antighost.autodecline\_days | 14 | Auto-decline |
| match.algo\_version | v2 | Logged with every score |
| features.\* | false | Feature flags per phase (invites, chat, payments, boost) |

### Pages

`/Account/Register` (choose Candidate or Studio, 18+ checkbox, terms/privacy consent) · `/Account/Login` · `/Account/ExternalLogin` (Google) · `/Account/ConfirmEmail` · `/Account/ForgotPassword` · `/Account/Manage` (change password, **delete my account**, download my data) · `/Legal/Terms` · `/Legal/Privacy` · `/Legal/Grievance` · `/Error` · layout with header, footer, cookie banner.

### Build order

1. Create the three-project solution, Serilog, appsettings structure and EF Core with PostgreSQL. Add `ApplicationUser` and Identity with renamed tables and snake\_case.
2. Registration with account type, email confirmation (Resend client behind `IEmailSender`), login, Google OAuth, lockout and rate limiting.
3. Entities, configurations, migration and seed for cities, skills, skill\_relations, app\_settings (seed from CSV/JSON files in the repo).
4. `IAuditLogger` service plus `ISettingsService` (cached read of app\_settings with 60-second refresh).
5. Base layout, CSS tokens (colors, spacing, radius, type scale), responsive navbar, cookie banner, legal pages.
6. Deploy: Nginx + Let's Encrypt, systemd service, PostgreSQL on localhost, nightly `pg_dump` to off-site storage, UptimeRobot check. **Test a restore once** before moving on.

### Acceptance

Register, confirm email, log in with email and Google on the live domain over HTTPS. Migrations apply cleanly. Settings change takes effect without redeploy. A backup file restores into a fresh database.

## 5. Phase 1 – Profiles and Resume AI (Weeks 3–4)

**Goal:** candidates and studios can build complete, structured profiles. This is the data every later algorithm depends on, so get the fields right.

### Tables introduced

```sql
CREATE TABLE candidate_profiles (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id            uuid NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
  full_name          text NOT NULL CHECK (char_length(full_name) BETWEEN 2 AND 80),
  headline           text NOT NULL CHECK (char_length(headline) BETWEEN 3 AND 80),   -- 'Unity Gameplay Programmer'
  primary_category   text NOT NULL CHECK (primary_category IN ('programming','art','design','audio','qa','production')),
  photo_path         text,
  phone              text,                       -- E.164, never shown to studios before shortlist
  phone_verified_at  timestamptz,                -- OTP verified
  city_id            int NOT NULL REFERENCES cities(id),
  seniority          text NOT NULL CHECK (seniority IN ('fresher','junior','mid','senior','lead')),
  years_experience   numeric(3,1) NOT NULL CHECK (years_experience BETWEEN 0 AND 50),
  current_ctc_lpa    numeric(6,2) CHECK (current_ctc_lpa >= 0),
  show_current_ctc   boolean NOT NULL DEFAULT false,
  expected_ctc_lpa   numeric(6,2) NOT NULL CHECK (expected_ctc_lpa > 0),
  notice_period_days smallint NOT NULL CHECK (notice_period_days IN (0,15,30,45,60,90)),
  open_to_remote     boolean NOT NULL DEFAULT false,
  willing_to_relocate boolean NOT NULL DEFAULT false,
  availability       text NOT NULL DEFAULT 'actively_looking' CHECK (availability IN ('actively_looking','open_to_offers','not_looking')),
  bio                text CHECK (char_length(bio) <= 1500),
  completeness       smallint NOT NULL DEFAULT 0 CHECK (completeness BETWEEN 0 AND 100),
  is_discoverable    boolean NOT NULL DEFAULT true,
  boosted_until      timestamptz,                -- used in Phase 6
  last_active_at     timestamptz NOT NULL DEFAULT now(),
  created_at         timestamptz NOT NULL DEFAULT now(),
  updated_at         timestamptz NOT NULL DEFAULT now(),
  deleted_at         timestamptz
);
CREATE INDEX ix_cand_discover ON candidate_profiles (primary_category, city_id, seniority)
  WHERE is_discoverable AND deleted_at IS NULL;

CREATE TABLE candidate_skills (
  candidate_id   uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  skill_id       int  NOT NULL REFERENCES skills(id),
  proficiency    smallint NOT NULL CHECK (proficiency BETWEEN 1 AND 4),  -- 1 beginner, 2 working, 3 strong, 4 expert
  years_used     numeric(3,1) CHECK (years_used >= 0),
  last_used_year smallint CHECK (last_used_year BETWEEN 1990 AND 2100),
  evidence_level smallint NOT NULL DEFAULT 0 CHECK (evidence_level IN (0,1,2)), -- 0 claimed, 1 backed, 2 verified
  is_primary     boolean NOT NULL DEFAULT false,                         -- max 5 primary skills
  PRIMARY KEY (candidate_id, skill_id)
);
CREATE INDEX ix_candskills_skill ON candidate_skills (skill_id, proficiency);

CREATE TABLE candidate_experiences (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  candidate_id uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  company_name text NOT NULL,
  title        text NOT NULL,
  start_date   date NOT NULL,
  end_date     date,
  is_current   boolean NOT NULL DEFAULT false,
  description  text CHECK (char_length(description) <= 1500),
  CHECK (end_date IS NULL OR end_date >= start_date),
  CHECK (NOT (is_current AND end_date IS NOT NULL))
);

CREATE TABLE candidate_education (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  candidate_id uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  institution  text NOT NULL,
  degree       text NOT NULL,
  field        text,
  start_year   smallint,
  end_year     smallint
);

-- Shipped titles: the core credibility signal. Verification columns are added now, used in Phase 5.
CREATE TABLE shipped_titles (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  candidate_id        uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  game_name           text NOT NULL,
  studio_name         text NOT NULL,
  role                text NOT NULL,                         -- 'Gameplay Programmer'
  platforms           text[] NOT NULL DEFAULT '{}',          -- pc, android, ios, console, web
  engine_skill_id     int REFERENCES skills(id),
  release_year        smallint CHECK (release_year BETWEEN 1990 AND 2100),
  store_url           text,                                  -- Steam / Play Store / App Store / itch.io
  igdb_id             bigint,
  verification        text NOT NULL DEFAULT 'none' CHECK (verification IN ('none','title_verified','credit_verified')),
  verified_at         timestamptz,
  is_nda              boolean NOT NULL DEFAULT false,        -- hide game name publicly, show only 'Under NDA' + role
  sort_order          smallint NOT NULL DEFAULT 0
);

CREATE TABLE portfolio_links (
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  candidate_id uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  link_type    text NOT NULL CHECK (link_type IN ('github','itchio','artstation','behance','youtube','website','linkedin','other')),
  url          text NOT NULL,
  title        text,
  skill_ids    int[] NOT NULL DEFAULT '{}'                   -- skills this item evidences
);

CREATE TABLE studio_profiles (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  owner_user_id      uuid NOT NULL REFERENCES users(id),
  name               text NOT NULL CHECK (char_length(name) BETWEEN 2 AND 100),
  slug               citext NOT NULL UNIQUE,
  logo_path          text,
  website            text,
  description        text CHECK (char_length(description) <= 3000),
  city_id            int REFERENCES cities(id),
  size_band          text NOT NULL CHECK (size_band IN ('1-10','11-50','51-200','200+')),
  studio_type        text NOT NULL CHECK (studio_type IN ('indie','game_studio','outsourcing','publisher','other')),
  founded_year       smallint,
  culture_tags       text[] NOT NULL DEFAULT '{}',           -- max 6, from a fixed list
  gstin              text CHECK (gstin ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$'),
  company_email_domain text,                                 -- from owner email; free-mail domains rejected
  verification_status text NOT NULL DEFAULT 'unverified'
                      CHECK (verification_status IN ('unverified','pending','verified','rejected')),
  verified_at        timestamptz,
  verified_by        uuid REFERENCES users(id),
  verification_notes text,
  -- cached responsiveness metrics, refreshed nightly (Phase 3)
  apps_90d           int NOT NULL DEFAULT 0,
  response_rate_90d  numeric(5,2),
  median_first_response_hours numeric(6,1),
  metrics_updated_at timestamptz,
  created_at         timestamptz NOT NULL DEFAULT now(),
  updated_at         timestamptz NOT NULL DEFAULT now(),
  deleted_at         timestamptz
);

CREATE TABLE studio_members (
  studio_id   uuid NOT NULL REFERENCES studio_profiles(id) ON DELETE CASCADE,
  user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  member_role text NOT NULL CHECK (member_role IN ('owner','recruiter','viewer')),
  invited_at  timestamptz NOT NULL DEFAULT now(),
  accepted_at timestamptz,
  PRIMARY KEY (studio_id, user_id)
);   -- the owner row is created with the studio; all studio authorization checks use this table

CREATE TABLE studio_titles (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  studio_id   uuid NOT NULL REFERENCES studio_profiles(id) ON DELETE CASCADE,
  game_name   text NOT NULL,
  release_year smallint,
  platforms   text[] NOT NULL DEFAULT '{}',
  store_url   text,
  igdb_id     bigint
);

CREATE TABLE studio_skills (            -- engines, genres, platforms the studio works with
  studio_id uuid NOT NULL REFERENCES studio_profiles(id) ON DELETE CASCADE,
  skill_id  int  NOT NULL REFERENCES skills(id),
  PRIMARY KEY (studio_id, skill_id)
);

CREATE TABLE resume_parse_logs (        -- rate limiting + cost tracking; stores NO resume text
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  user_id    uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  source     text NOT NULL CHECK (source IN ('pdf','linkedin_pdf')),
  status     text NOT NULL CHECK (status IN ('ok','failed','rejected')),
  input_chars int,
  duration_ms int,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_parse_user_day ON resume_parse_logs (user_id, created_at DESC);
```

### Field design – candidate onboarding (5 steps)

| Step | Field | Control | Required | Validation / notes |
| --- | --- | --- | --- | --- |
| 1 Basics | Full name | text | Yes | 2–80 chars |
| 1 | Headline | text | Yes | 3–80 chars, placeholder shows an example |
| 1 | Primary category | segmented buttons | Yes | Programming, Art, Design, Audio, QA, Production |
| 1 | City | searchable select | Yes | from cities |
| 1 | Phone | tel + OTP | No at signup, required before first apply | E.164; OTP verification sets phone\_verified\_at |
| 2 Experience | Years of experience | number step 0.5 | Yes | 0–50. Seniority is **suggested** from years (see below), user may change by one level only |
| 2 | Seniority | select | Yes | fresher, junior, mid, senior, lead |
| 2 | Work history | repeating card | No (fresher) | company, title, dates, description |
| 2 | Education | repeating card | No | institution, degree, years |
| 3 Skills | Skills | typeahead chips | Yes, 3–25 | proficiency 1–4 per skill, up to 5 marked primary |
| 4 Proof | Shipped titles | repeating card | No | game, studio, role, platforms, engine, year, store URL, NDA toggle |
| 4 | Portfolio links | repeating row | No | https only; tag the skills each link proves |
| 5 Preferences | Expected CTC (LPA) | number | Yes | > 0; helper text shows market range once benchmarks exist |
| 5 | Current CTC | number | No | hidden from studios unless show\_current\_ctc is ticked |
| 5 | Notice period | select | Yes | 0, 15, 30, 45, 60, 90 days |
| 5 | Open to remote / willing to relocate | toggles | Yes | used by hard filters |
| 5 | Availability | select | Yes | actively looking, open to offers, not looking |

**Seniority suggestion from years:** under 1 = fresher; 1 to under 3 = junior; 3 to under 7 = mid; 7 to under 12 = senior; 12 and above = lead. The matching engine uses the years-derived level, not the self-declared label, so inflating seniority does not help.

**Evidence level (candidate\_skills.evidence\_level), recomputed by `IEvidenceService` on every profile save:**

- 2 verified: skill is the engine/skill of a title with `credit_verified` (Phase 5), or a passed skill challenge (Phase 7).
- 1 backed: skill is the engine of any shipped title, appears in a portfolio link's skill\_ids, or appears in a work-experience description.
- 0 claimed: everything else.

**Profile completeness (0–100):** photo 5 · headline 5 · bio 10 · at least 5 skills with proficiency 15 · at least 1 experience 15 (freshers get this for education) · education 5 · at least 1 shipped title or portfolio link 20 · expected CTC and notice period 10 · phone verified 10 · city 5. Applying is allowed from 60 and above; candidate search by studios only shows profiles at 70 and above.

### Field design – studio onboarding

| Field | Control | Required | Validation / notes |
| --- | --- | --- | --- |
| Studio name | text | Yes | 2–100 chars; slug auto-generated and unique |
| Studio type and size | selects | Yes | fixed lists |
| City | searchable select | Yes | HQ city |
| Website | url | Yes | https only |
| Description | textarea | Yes | 100–3000 chars |
| Engines, genres, platforms | chips from skills | Yes, at least 1 engine | studio\_skills |
| Shipped titles | repeating card | Recommended | game, year, platforms, store URL |
| Culture tags | chips | No | max 6 from fixed list (Remote-first, Learning budget, Flexible hours, Crunch-free, ...) |
| GSTIN | text | Required to verify | regex above |
| Work email | read-only from account | Yes | must be company domain (reject gmail, outlook, yahoo, etc.) |
| Logo | image upload | No | 1 MB, resized to 256x256 |

**Studio verification (basic, Phase 1):** studio submits GSTIN and website; system checks the GSTIN format, that the work-email domain matches the website domain, and that the domain is not free-mail. Status moves to `pending`; an admin reviews within 24 hours (verify the GSTIN on the public GST portal, confirm the site is live). Unverified studios can build a profile and drafts but **cannot publish jobs or see candidate contact details**.

### Resume AI extraction (pipeline)

1. Input: PDF upload only (2 MB). A LinkedIn profile is imported by uploading LinkedIn's own **Save to PDF** export. Do not fetch LinkedIn URLs: LinkedIn blocks scraping and its terms forbid it.
2. Extract text with PdfPig. Reject if under 200 characters (scanned image) with a friendly message.
3. **Redact PII before the LLM call:** remove emails, phone numbers and URLs with regexes. The model never receives contact details.
4. Call Gemini on the **paid tier** (or another provider with no-training terms) with a strict JSON schema and temperature 0. Do not use the free tier for resume data.
5. Validate the JSON in C# (types, lengths, dates, years range). Match skill names to the `skills` table using name, alias and trigram similarity; unmatched skills are shown as suggestions the user can add.
6. Show a **review screen** with everything pre-filled and editable. Nothing is saved until the user confirms.
7. Log to resume\_parse\_logs (no text). Enforce `limits.resume_parse_per_day`. Discard the text from memory afterwards.

**JSON the model must return:**

```json
{
  "headline": "string",
  "years_experience": 0.0,
  "skills": [{"name": "string", "years_used": 0.0, "last_used_year": 2025}],
  "experiences": [{"company": "string", "title": "string", "start": "YYYY-MM", "end": "YYYY-MM or null", "description": "string"}],
  "education": [{"institution": "string", "degree": "string", "field": "string", "start_year": 2018, "end_year": 2022}],
  "shipped_titles": [{"game": "string", "studio": "string", "role": "string", "year": 2023}]
}
```

The prompt must say: extract only what is stated in the text, never infer skills, return null for unknown fields, return JSON only.

### Pages

`/Candidate/Onboarding` (5-step wizard) · `/Candidate/Profile` (preview as studios see it) · `/Candidate/Profile/Edit` · `/Candidate/Resume/Import` · `/Candidate/Settings` (privacy, visibility, CTC) · `/Studio/Onboarding` · `/Studio/Profile/Edit` · `/Studio/Verification` · `/Studios/{slug}` (public) · `/Admin/Studios/Verification` (review queue).

### Build order

1. Entities, configurations, migration for all Phase 1 tables.
2. `ICandidateProfileService`: create/update, seniority suggestion, completeness calculation, evidence recalculation, with unit tests for every rule above.
3. Candidate onboarding wizard (server-side, one Razor Page per step, draft saved after each step).
4. Photo upload (ImageSharp resize, authorized file endpoint) and phone OTP (SMS provider behind `ISmsSender`; log-only implementation for development).
5. Resume import pipeline with a fake `IResumeLlmClient` first, then the real Gemini client.
6. Studio onboarding, `studio_members` owner row, public studio page.
7. Verification flow and admin review queue with audit log entries.

### Acceptance

A new candidate can reach 100 percent completeness. A resume import fills the review screen without storing resume text. Candidate A cannot open or edit candidate B's profile by changing the id in the URL. An unverified studio cannot publish. Free-mail work emails are rejected.

## 6. Phase 2 – Jobs and Search (Weeks 5–6)

**Goal:** verified studios publish structured jobs; candidates search and filter them. Matching is added in Phase 3, so Best match sorting stays disabled here.

### Tables introduced

```sql
CREATE TABLE jobs (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  studio_id          uuid NOT NULL REFERENCES studio_profiles(id),
  posted_by_user_id  uuid NOT NULL REFERENCES users(id),
  title              text NOT NULL CHECK (char_length(title) BETWEEN 5 AND 100),
  slug               text NOT NULL UNIQUE,                    -- unity-developer-pune-a1b2c3
  category           text NOT NULL CHECK (category IN ('programming','art','design','audio','qa','production')),
  job_type           text NOT NULL CHECK (job_type IN ('full_time','contract','internship','part_time')),
  seniority_min      text NOT NULL CHECK (seniority_min IN ('fresher','junior','mid','senior','lead')),
  seniority_max      text NOT NULL CHECK (seniority_max IN ('fresher','junior','mid','senior','lead')),
  min_years          numeric(3,1) NOT NULL DEFAULT 0,
  max_years          numeric(3,1),
  description        text NOT NULL CHECK (char_length(description) BETWEEN 100 AND 8000),
  work_mode          text NOT NULL CHECK (work_mode IN ('onsite','hybrid','remote')),
  city_id            int REFERENCES cities(id),
  relocation_assistance boolean NOT NULL DEFAULT false,
  fresher_friendly   boolean NOT NULL DEFAULT false,
  pay_unit           text NOT NULL DEFAULT 'lpa' CHECK (pay_unit IN ('lpa','monthly_inr')),  -- internships use monthly
  salary_min         numeric(10,2) NOT NULL,
  salary_max         numeric(10,2) NOT NULL,
  max_notice_days    smallint CHECK (max_notice_days IN (0,15,30,45,60,90)),   -- null = no limit
  openings           smallint NOT NULL DEFAULT 1 CHECK (openings BETWEEN 1 AND 50),
  screening_question text CHECK (char_length(screening_question) <= 300),
  status             text NOT NULL DEFAULT 'draft'
                     CHECK (status IN ('draft','pending_review','active','paused','full','expired','closed','rejected')),
  rejection_reason   text,
  application_cap    smallint NOT NULL DEFAULT 50,
  applications_count int NOT NULL DEFAULT 0,
  views_count        int NOT NULL DEFAULT 0,
  published_at       timestamptz,
  expires_at         timestamptz,
  closed_at          timestamptz,
  search_vector      tsvector GENERATED ALWAYS AS (
                       setweight(to_tsvector('english', coalesce(title,'')), 'A') ||
                       setweight(to_tsvector('english', coalesce(description,'')), 'B')) STORED,
  created_at         timestamptz NOT NULL DEFAULT now(),
  updated_at         timestamptz NOT NULL DEFAULT now(),
  deleted_at         timestamptz,
  CHECK (salary_min > 0 AND salary_max >= salary_min),
  CHECK (work_mode = 'remote' OR city_id IS NOT NULL)
);
CREATE INDEX ix_jobs_search   ON jobs USING gin (search_vector);
CREATE INDEX ix_jobs_title_tr ON jobs USING gin (title gin_trgm_ops);
CREATE INDEX ix_jobs_active   ON jobs (published_at DESC) WHERE status = 'active' AND deleted_at IS NULL;
CREATE INDEX ix_jobs_filter   ON jobs (category, work_mode, city_id) WHERE status = 'active';
CREATE INDEX ix_jobs_studio   ON jobs (studio_id, status);

CREATE TABLE job_skills (
  job_id          uuid NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
  skill_id        int  NOT NULL REFERENCES skills(id),
  is_required     boolean NOT NULL DEFAULT true,
  min_proficiency smallint NOT NULL DEFAULT 2 CHECK (min_proficiency BETWEEN 1 AND 4),
  PRIMARY KEY (job_id, skill_id)
);

CREATE TABLE saved_jobs (
  candidate_id uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  job_id       uuid NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
  saved_at     timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (candidate_id, job_id)
);

CREATE TABLE saved_searches (          -- job alerts
  id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  candidate_id uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  name         text NOT NULL,
  filters      jsonb NOT NULL,
  frequency    text NOT NULL DEFAULT 'daily' CHECK (frequency IN ('instant','daily','off')),
  last_sent_at timestamptz,
  created_at   timestamptz NOT NULL DEFAULT now()
);
```

### Field design – job post form

| Field | Control | Required | Validation / notes |
| --- | --- | --- | --- |
| Title | text | Yes | 5–100 chars; no all-caps; no phone numbers or emails |
| Category | select | Yes | six categories |
| Job type | select | Yes | full time, contract, internship, part time |
| Seniority range | two selects | Yes | min <= max |
| Years of experience | two numbers | Yes | min required; max optional |
| Skills | chips | Yes | 3–12 skills, at least 1 required; mark each required or nice-to-have; set minimum proficiency |
| Work mode | segmented | Yes | onsite, hybrid, remote |
| City | select | If not remote |  |
| Salary | two numbers + unit | **Yes** | max >= min and max <= min x job.salary\_max\_spread (1.5); contracts and internships may use monthly INR. Values far outside benchmarks are flagged for admin review |
| Max notice period | select | No | blank = any |
| Fresher friendly | toggle | No |  |
| Relocation assistance | toggle | No |  |
| Openings | number | Yes | 1–50 |
| Description | textarea | Yes | 100–8000 chars; block contact details and external apply links |
| Screening question | text | No | one question, 300 chars; answered at apply time |

### Rules

- **Lifecycle:** draft → pending\_review → active → (paused | full | expired | closed). Rejected jobs return to draft with a reason.
- **Review:** a studio's first 3 jobs go through admin review. After 3 approved jobs, new jobs go straight to active, but the content filters (no contact details, salary spread) still run, and any failure sends the job back to pending\_review.
- **Expiry:** `expires_at = published_at + job.expiry_days`. A nightly job marks expired jobs and notifies the studio 3 days before. Studios can extend by one click if they have responded to applicants.
- **Applicant cap:** when `applications_count` reaches `application_cap`, status becomes `full` and the job stops accepting applications until the studio reviews applicants (then it reopens).
- **Free limit:** `limits.free_active_jobs` active jobs per studio.

### Search specification

- **Filters:** category, skills (any of), engine, work mode, city, job type, seniority, minimum salary (normalized to LPA), fresher friendly, posted within 24 hours / 7 days / 30 days.
- **Text search:** `websearch_to_tsquery('english', :q)` against `search_vector`, ranked with `ts_rank`. When fewer than 3 results come back, fall back to trigram similarity on title (handles typos such as unrity).
- **Sorting:** newest (default), salary high to low, closest to expiry. Best match is added in Phase 3.
- **Pagination:** keyset pagination on (published\_at, id), not OFFSET.
- **Salary normalization:** monthly\_inr x 12 / 100000 = LPA, used only for filtering and matching.
- **SEO:** public job pages at `/jobs/{slug}` with JobPosting JSON-LD, plus generated landing pages such as `/jobs/unity-developer/pune` for every (skill, hub city) pair with at least 3 active jobs, listed in a sitemap.

### Pages

`/Jobs` (search + filters) · `/Jobs/{slug}` (detail) · `/Jobs/Saved` · `/Candidate/Alerts` · `/Studio/Jobs` (list with status tabs) · `/Studio/Jobs/Create` · `/Studio/Jobs/Edit/{id}` · `/Admin/Jobs/Review` · `/Sitemap.xml`.

### Build order

1. Entities, configurations, migration; check the full-text index is used with `EXPLAIN`.
2. `IJobService`: create, validate (salary spread, content filters), submit, approve/reject, pause, close, expire, with unit tests for the lifecycle.
3. Studio job create/edit pages with the skill chip picker.
4. Search query builder (filters + FTS + keyset paging) with integration tests on seeded data (1,000 fake jobs).
5. Public job list and detail pages with JSON-LD and the sitemap.
6. Saved jobs and saved searches; Hangfire daily job that emails alert digests.
7. Admin review queue and nightly expiry job.

### Acceptance

A verified studio publishes a job that appears in search within a minute of approval. A typo search still returns results. A job with salary 3–40 LPA is rejected. A 1,000-job search returns in under 200 ms on the VPS. Studio B cannot edit studio A's job by changing the id.

## 7. Phase 3 – Applications, Matching and Anti-Ghosting (Weeks 7–8)

**Goal:** one-click apply with a transparent match score, a studio applicant pipeline, and an anti-ghosting system that is hard to game. This phase holds the core algorithms, so build it with unit tests first.

### Tables introduced

```sql
CREATE TABLE applications (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  job_id             uuid NOT NULL REFERENCES jobs(id),
  candidate_id       uuid NOT NULL REFERENCES candidate_profiles(id),
  source             text NOT NULL DEFAULT 'search' CHECK (source IN ('search','recommended','invite','alert','external')),
  status             text NOT NULL DEFAULT 'applied'
                     CHECK (status IN ('applied','viewed','shortlisted','interview','offer','hired','rejected','withdrawn','auto_declined')),
  match_score        numeric(5,2) NOT NULL,
  match_breakdown    jsonb NOT NULL,                -- full explanation, see below
  algo_version       text NOT NULL,
  cover_note         text CHECK (char_length(cover_note) <= 1000),
  screening_answer   text CHECK (char_length(screening_answer) <= 1000),
  applied_at         timestamptz NOT NULL DEFAULT now(),
  viewed_at          timestamptz,
  first_response_at  timestamptz,                   -- first HUMAN decision (shortlist/interview/offer/hired/reject), NOT 'viewed'
  last_action_at     timestamptz NOT NULL DEFAULT now(),
  reminder_sent_at   timestamptz,
  rejection_reason   text CHECK (rejection_reason IN ('position_filled','skills_mismatch','salary_mismatch','experience_mismatch','not_a_fit_now','other')),
  contact_released_at timestamptz,                  -- set when status reaches shortlisted
  UNIQUE (job_id, candidate_id)
);
CREATE INDEX ix_app_job_rank  ON applications (job_id, status, match_score DESC);
CREATE INDEX ix_app_cand      ON applications (candidate_id, status);
CREATE INDEX ix_app_ghost     ON applications (last_action_at) WHERE status IN ('applied','viewed');

CREATE TABLE application_status_history (
  id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  application_id  uuid NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
  from_status     text,
  to_status       text NOT NULL,
  changed_by_user_id uuid REFERENCES users(id),     -- NULL = system (auto-decline)
  changed_by_type text NOT NULL CHECK (changed_by_type IN ('studio','candidate','system','admin')),
  note            text,
  created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_ash_app ON application_status_history (application_id, created_at);

-- Cached scores so search and recommendations do not recompute on every request
CREATE TABLE match_scores (
  candidate_id        uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  job_id              uuid NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
  passes_filters      boolean NOT NULL,
  filter_reason       text,                          -- 'engine_mismatch','salary_ceiling','notice_period','work_mode'
  fit_for_studio      numeric(5,2) NOT NULL,         -- how well the candidate fits the job
  appeal_for_candidate numeric(5,2),                 -- how attractive the job is to the candidate (Phase 5)
  final_score         numeric(5,2) NOT NULL,
  breakdown           jsonb NOT NULL,
  algo_version        text NOT NULL,
  computed_at         timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (candidate_id, job_id)
);
CREATE INDEX ix_ms_job  ON match_scores (job_id, final_score DESC) WHERE passes_filters;
CREATE INDEX ix_ms_cand ON match_scores (candidate_id, final_score DESC) WHERE passes_filters;

-- Every impression and decision. This is your future ML training data.
CREATE TABLE match_events (
  id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  event_type     text NOT NULL CHECK (event_type IN ('impression','click','save','apply','view','shortlist','interview','offer','hire','reject','withdraw')),
  candidate_id   uuid NOT NULL,
  job_id         uuid NOT NULL,
  rank_position  int,
  score          numeric(5,2),
  algo_version   text,
  context        jsonb,                              -- sort order, filters used, page
  created_at     timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_me_type_time ON match_events (event_type, created_at DESC);
CREATE INDEX ix_me_job ON match_events (job_id, event_type);
```

### The matching algorithm (version v2)

The engine lives in `HireGameDev.Core` as a pure function with no database or HTTP access, so every rule is unit-testable: `MatchResult Evaluate(CandidateSnapshot c, JobSnapshot j, MatchSettings s)`. Weights and thresholds come from `app_settings` (`match.weights`) so you can tune them without a deploy.

**Step 1 – Hard filters.** Each failed filter sets `passes_filters = false` with a reason. Filtered pairs are excluded from recommendations, Best match sorting and studio candidate search. **Manual applying is still allowed**: the candidate sees a warning and the studio sees the flag.

| Filter | Rule | Reason code |
| --- | --- | --- |
| F1 Work mode | Job is onsite or hybrid, candidate city differs from job city and candidate is not willing to relocate; or job is remote and candidate is not open to remote | work\_mode |
| F2 Engine gate | Job has a required skill of category engine and the candidate lacks that skill at proficiency 2 or more, with no related skill at relation weight 0.80 or more | engine\_mismatch |
| F3 Salary ceiling | Candidate expected pay (normalized to the job's unit) is more than 30 percent above the job maximum | salary\_ceiling |
| F4 Notice period | Job sets max\_notice\_days and the candidate's notice period is longer | notice\_period |
| F5 Availability | (Studio-side search only) candidate availability is not\_looking | availability |

Candidates can switch on **Show stretch matches** to see jobs that failed F2 or F3 only, clearly labelled.

**Step 2 – Skill score (0–100).** Computed over the job's skills, so listing extra skills on a profile can never raise a score.

```text
For each job skill i:
  w_i = (is_required ? 2.0 : 1.0) x idf_i              -- idf from skills.idf_weight
  direct_i  = prof_factor(candidate proficiency) x evidence_factor(evidence_level)
  related_i = max over candidate skills r of ( relation_weight(i, r) x 0.6 x prof_factor(r) x evidence_factor(r) )
  c_i = min(1, max(direct_i, related_i))
  if candidate proficiency < job min_proficiency: c_i = c_i x 0.7

skill_score = 100 x SUM(w_i x c_i) / SUM(w_i)

prof_factor:     1 -> 0.50 | 2 -> 0.75 | 3 -> 0.90 | 4 -> 1.00
evidence_factor: claimed (0) -> 0.70 | backed (1) -> 0.90 | verified (2) -> 1.00
```

**IDF note:** `idf_i = clamp(ln((N + 1) / (df_i + 1)) + 1, 0.5, 2.0)` where N is the number of active jobs and df\_i the number requiring skill i. Recomputed nightly. While N is under 100 active jobs, use idf = 1.0 for every skill (too little data to be meaningful).

**Step 3 – Experience fit (0–100).** Uses years of experience, not the self-declared seniority.

```text
y = candidate years; lo = job.min_years; hi = job.max_years, or lo + 4 if null
if job.fresher_friendly and y < 1       -> 100
else if lo <= y <= hi                   -> 100
else if y < lo                          -> max(0,  100 - 25 x (lo - y))     -- underqualified: steep
else (y > hi)                           -> max(30, 100 - 10 x (y - hi))     -- overqualified: gentle
```

**Step 4 – Logistics score (0–100)** = 0.40 x location + 0.40 x salary + 0.20 x notice.

| Part | Rule |
| --- | --- |
| Location | Same city: 100. Remote job and candidate open to remote: 90. Different city and willing to relocate: 60 (70 if job offers relocation assistance) |
| Salary | Expected within \[min, max\]: 100. Expected below min: 95. Above max by up to 10 percent: 70; up to 20 percent: 45; up to 30 percent: 20 (beyond 30 percent is filtered by F3) |
| Notice | If the job sets a maximum and the candidate meets it: 100. If no maximum: 100 - (notice\_days / 90) x 20 |

Salary values are normalized first: monthly\_inr x 12 / 100000 = LPA.

**Step 5 – Fit for studio.**

```text
fit_for_studio = 0.55 x skill_score + 0.20 x experience_fit + 0.25 x logistics_score      (cap 100, round to 1 decimal)
```

Phase 5 adds a shipped-title relevance bonus (up to +8: same engine, same platform, same genre) before the cap. Phase 3 stores `appeal_for_candidate = NULL` and sets `final_score = fit_for_studio`.

**Step 6 – Display and ranking.**

- Label bands: 80–100 Strong match, 60–79 Good match, 40–59 Partial match; below 40 is hidden from recommendations (still visible if the candidate searches directly).
- Candidate-facing Best match order: `rank = final_score + freshness + studio_quality`, where freshness = 5 x max(0, 1 - days\_since\_posted / 14) and studio\_quality = verified +2, response\_rate\_90d >= 80 percent +2 (>= 50 percent +1).
- Studio-facing applicant order: `match_score` descending, ties broken by earlier `applied_at`.
- The displayed percentage is always `final_score`. Rank adjustments never change it.
- Candidates with an active profile boost (Phase 6) get **+3 rank points only** in candidate search and never a higher displayed score. They are labelled Boosted.

**Step 7 – Explanation JSON** stored in `match_breakdown` and shown in a Why this match panel:

```json
{
  "final": 73.7, "label": "Good match", "passes_filters": true, "flags": [],
  "skills": { "score": 52.7, "matched": ["Unity", "C#"], "missing": ["Shader Graph"], "related": [],
              "unbacked": ["C#"], "tip": "Add a project or portfolio link that proves C# to raise this score" },
  "experience": { "score": 100, "years": 4, "job_range": [3, 6] },
  "logistics": { "score": 98.7, "location": 100, "salary": 100, "notice": 93.3 }
}
```

**Worked example (use as unit test #1).** Job: Unity Developer in Pune, 3–6 years, salary 10–14 LPA, no notice limit. Skills: Unity (required, min 3, idf 1.0), C# (required, min 3, idf 0.8), Shader Graph (nice-to-have, min 2, idf 1.6). Candidate: Pune, 4 years, expects 12 LPA, notice 30 days; Unity proficiency 3 backed; C# proficiency 4 claimed; no Shader Graph.

- Weights: Unity 2.0, C# 1.6, Shader Graph 1.6 (total 5.2). Credits: Unity 0.90 x 0.90 = 0.81; C# 1.00 x 0.70 = 0.70; Shader Graph 0.
- skill\_score = 100 x (2.0 x 0.81 + 1.6 x 0.70) / 5.2 = 52.7. experience\_fit = 100. logistics = 0.4 x 100 + 0.4 x 100 + 0.2 x 93.3 = 98.7.
- fit\_for\_studio = 0.55 x 52.7 + 0.20 x 100 + 0.25 x 98.7 = **73.7** (Good match).
- If the candidate proves C# with a portfolio link (evidence becomes backed), C# credit rises to 0.90 and the score rises to about 77. This is the incentive that rewards real evidence.

**Cache refresh:** recompute `match_scores` (a) for one job against all discoverable candidates when a job is published or its skills, salary, location or notice change; (b) for one candidate against all active jobs when the profile is saved. Both run as Hangfire background jobs, debounced by 60 seconds. Always recompute at apply time and store the result in the application row.

### Application rules

- **Preconditions to apply:** profile completeness >= 60, phone verified, job is active, no existing application, candidate under `limits.apply_per_day`, job not full. Withdrawn applications can be re-submitted once after 7 days.
- **Race safety:** applying runs in a transaction that locks the job row (`SELECT ... FOR UPDATE`), inserts the application, increments `applications_count`, and sets the job to `full` when the cap is reached.
- **Contact release:** studios see name, headline, skills, titles, expected CTC and (if allowed) current CTC. Phone and email are released only when status reaches shortlisted (`contact_released_at`).
- **Status flow:** applied → viewed → shortlisted → interview → offer → hired, with rejected, withdrawn and auto\_declined as exits. Every change writes `application_status_history`. Rejecting requires a reason code.
- **Candidate can withdraw** at any stage before offer.

### Anti-ghosting algorithm

A Hangfire recurring job runs daily at 09:00 IST.

1. **Reminder (day 7):** applications in applied or viewed with `last_action_at` older than `antighost.reminder_days` and no reminder sent. Send one digest per studio (for example, 12 applicants waiting), then set `reminder_sent_at`.
2. **Auto-decline (day 14):** applications in applied or viewed with `last_action_at` older than `antighost.autodecline_days` move to `auto_declined` (system history row). The candidate is told the studio did not respond, sees up to 5 similar active jobs from `match_scores`, and nothing counts against the candidate.
3. **Studio metrics (nightly):** for each studio, over the last 90 days, consider applications at least 14 days old. `response_rate_90d` = share with `first_response_at` set within 14 days of `applied_at`. `median_first_response_hours` = median of those response times. Only shortlist, interview, offer, hired and rejected-with-reason count. **Viewed does not count.** Show the badge only once `apps_90d` is 10 or more; before that show New studio.
4. **Gaming guards:** a rejection needs a reason code. Ten or more rejections by one user within 60 seconds without opening the applicant detail pages are written to `audit_logs` as `studio.bulk_reject_flag` and treated as non-responses for the metric.
5. **Consequences:** response rate shown on every job card and studio page. A studio under 30 percent for two consecutive months with at least 20 applications gets an admin warning; in Phase 6 it also takes a rank penalty of 5 points.

### Pages

`/Jobs/{slug}/Apply` (confirm, optional note, screening answer, warnings) · `/Applications` (candidate list with status timeline) · `/Applications/{id}` · `/Candidate/Recommended` (Best match feed) · `/Studio/Jobs/{id}/Applicants` (ranked list, filters, bulk shortlist/reject) · `/Studio/Applications/{id}` (candidate detail with Why this match panel and status actions) · `/Admin/Studios/Metrics`.

### Build order

1. Entities, configurations, migration for applications, history, match\_scores, match\_events.
2. **`IMatchingEngine` in Core with unit tests first:** the worked example, each hard filter, the related-skill path, evidence boost, overqualified vs underqualified, salary bands, fresher-friendly, stuffing resistance (adding 20 irrelevant skills does not change the score).
3. `IMatchScoreService` plus Hangfire refresh jobs and the nightly IDF job.
4. Apply flow (`IApplicationService`) with the transaction, caps and warnings.
5. Candidate recommended feed and Best match sort in `/Jobs` using the cached scores; log impressions to `match_events`.
6. Studio applicant pipeline with status changes, reason codes, contact release.
7. Anti-ghosting Hangfire jobs and the nightly studio metrics job; show response-rate badges.

### Acceptance

The worked example returns 73.7. A candidate cannot exceed the daily apply limit or apply twice. 51 simultaneous applications to a 50-cap job result in exactly 50 rows. A studio cannot see a phone number before shortlisting. An application untouched for 14 days becomes auto\_declined and the candidate receives similar jobs. Match scores update within two minutes of a candidate editing skills.

## 8. Phase 4 – Dashboards, Notifications, Admin, Legal and Launch (Weeks 9–13)

**Goal:** everything needed for a safe public launch. Payments are intentionally not in this phase: launch studios get free access.

### Tables introduced

```sql
CREATE TABLE notifications (
  id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  type        text NOT NULL,      -- application_status, new_applicant, applicant_reminder, auto_decline, job_expiring,
                                  -- job_approved, job_rejected, studio_verified, alert_digest, report_update
  title       text NOT NULL,
  body        text,
  link_url    text,
  payload     jsonb,
  is_read     boolean NOT NULL DEFAULT false,
  read_at     timestamptz,
  emailed_at  timestamptz,
  created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_notif_user ON notifications (user_id, is_read, created_at DESC);

CREATE TABLE notification_preferences (
  user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  type    text NOT NULL,
  in_app  boolean NOT NULL DEFAULT true,
  email   boolean NOT NULL DEFAULT true,
  PRIMARY KEY (user_id, type)
);

CREATE TABLE profile_views (                 -- one row per studio, candidate and day
  id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  studio_id      uuid NOT NULL REFERENCES studio_profiles(id) ON DELETE CASCADE,
  viewer_user_id uuid NOT NULL REFERENCES users(id),
  candidate_id   uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  view_date      date NOT NULL DEFAULT current_date,
  source         text NOT NULL CHECK (source IN ('search','application','invite','direct')),
  created_at     timestamptz NOT NULL DEFAULT now(),
  UNIQUE (studio_id, candidate_id, view_date)
);
CREATE INDEX ix_pv_candidate ON profile_views (candidate_id, created_at DESC);

CREATE TABLE reports (
  id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  reporter_user_id uuid NOT NULL REFERENCES users(id),
  entity_type      text NOT NULL CHECK (entity_type IN ('job','studio','candidate','message')),
  entity_id        uuid NOT NULL,
  reason           text NOT NULL CHECK (reason IN ('scam_or_fee','fake_profile','misleading_job','harassment','discrimination','spam','other')),
  details          text CHECK (char_length(details) <= 1000),
  status           text NOT NULL DEFAULT 'open' CHECK (status IN ('open','reviewing','actioned','dismissed')),
  resolved_by      uuid REFERENCES users(id),
  resolved_at      timestamptz,
  created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE broadcasts (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  title      text NOT NULL,
  body       text NOT NULL,
  audience   text NOT NULL CHECK (audience IN ('all','candidates','studios')),
  created_by uuid NOT NULL REFERENCES users(id),
  sent_at    timestamptz,
  created_at timestamptz NOT NULL DEFAULT now()
);
```

### Features

- **Candidate dashboard:** profile completeness meter with the next best action, applications by status, top 5 recommended jobs, saved jobs, alert summary.
- **Studio dashboard:** active jobs, applicants waiting (red when older than 7 days), response rate and median response time, funnel counts per job (views, applications, shortlisted, interviews, hires).
- **Studio candidate search (verified studios only):** filters like job search plus skills, seniority, city, notice period, availability. Only profiles with completeness 70 or more, `is_discoverable`, and availability not equal to not\_looking appear. Results show the headline, skills, titles and expected CTC but **no contact details**. Every profile open writes `profile_views`; limit `limits.profile_views_per_studio_per_day` (100). Inviting comes in Phase 5.
- **Notifications:** one `INotificationService.Send(userId, type, ...)` writes the row, pushes it over SignalR (`NotificationHub`, one group per user) and queues the email through Hangfire with retries. Respect `notification_preferences`. With a single server no Redis backplane is needed. The bell polls every 60 seconds as a fallback.
- **Admin panel:** users (search, suspend, read-only view-as), studio verification queue, job review queue, reports queue, settings editor for `app_settings`, broadcasts, audit log viewer, skills manager (add, merge, set aliases, set relations), platform metrics. **View-as** is read-only: it requires a written reason, shows a banner, expires after 15 minutes, blocks every POST, and writes an `admin.view_as` audit entry.
- **Reporting:** a Report button on jobs, studios and candidate profiles feeds the reports queue; scam or fee reports trigger an immediate job pause for admin review.

### Legal and privacy (DPDP)

Have a lawyer review these before launch; this document is an engineering checklist, not legal advice.

- Privacy notice that names every processor (hosting, email, SMS, AI provider) and states that some are outside India. Record consent in `consent_records` including the `ai_processing` consent before any resume import.
- 18+ confirmation at signup. Cookie banner with a choice.
- Grievance officer page with name and email, and a 30-day response commitment.
- **Download my data** (JSON export of the profile, applications and messages) and **Delete my account** (30-day grace period, then anonymize: replace personal fields, delete uploaded files, keep applications as Deleted user so studio statistics stay intact).
- Retention rules: invoices and payment records are kept for the period your accountant requires; audit logs and application history have a defined retention period; resume text is never stored.
- A written breach-response checklist (who decides, who is notified, within what time).

### Launch checklist

1. 20 studios onboarded by hand, 30–50 real jobs live, 200 candidates from communities before the public announcement.
2. Error tracking and uptime alerts; a feedback button on every page.
3. Privacy-friendly analytics events for onboarding completion, apply, shortlist.
4. Load test with k6 (200 concurrent users on search and apply).
5. Backup restore tested again on the production setup. Security review: authorization tests for every Razor Page, file upload tests, rate limits verified.
6. Support email, terms and privacy final, sitemap submitted.

### Build order

1. Notifications service, preferences, SignalR hub, bell UI, email templates.
2. Candidate and studio dashboards.
3. Studio candidate search with profile\_views and daily limits.
4. Reports and the admin panel pages (build the audit log viewer first).
5. DPDP features: consent capture, data export, account deletion job.
6. Hardening, load test, launch checklist.

### Acceptance

A studio sees an applicant notification within 5 seconds and by email within 2 minutes. A studio cannot exceed its daily profile-view limit. An admin view-as session cannot change data. A deleted account disappears from search immediately and is fully anonymized after 30 days.

## 9. Phase 5 – Trust and Two-Way Hiring (Months 4–6)

**Goal:** make credentials believable and let both sides start the conversation, without opening the door to spam.

### Tables introduced

```sql
CREATE TABLE title_credit_confirmations (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  shipped_title_id   uuid NOT NULL REFERENCES shipped_titles(id) ON DELETE CASCADE,
  confirmer_type     text NOT NULL CHECK (confirmer_type IN ('studio','coworker')),
  confirmer_user_id  uuid NOT NULL REFERENCES users(id),
  confirmer_studio_id uuid REFERENCES studio_profiles(id),
  status             text NOT NULL DEFAULT 'requested' CHECK (status IN ('requested','confirmed','declined')),
  created_at         timestamptz NOT NULL DEFAULT now(),
  responded_at       timestamptz,
  UNIQUE (shipped_title_id, confirmer_user_id)
);

CREATE TABLE invites (
  id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  studio_id          uuid NOT NULL REFERENCES studio_profiles(id),
  job_id             uuid NOT NULL REFERENCES jobs(id),
  candidate_id       uuid NOT NULL REFERENCES candidate_profiles(id),
  invited_by_user_id uuid NOT NULL REFERENCES users(id),
  message            text CHECK (char_length(message) <= 500),
  fit_at_invite      numeric(5,2) NOT NULL,
  status             text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','accepted','declined','expired','withdrawn')),
  created_at         timestamptz NOT NULL DEFAULT now(),
  responded_at       timestamptz,
  expires_at         timestamptz NOT NULL DEFAULT (now() + interval '7 days'),
  UNIQUE (job_id, candidate_id)
);

CREATE TABLE conversations (
  id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id    uuid NOT NULL UNIQUE REFERENCES applications(id) ON DELETE CASCADE,
  studio_id         uuid NOT NULL REFERENCES studio_profiles(id),
  candidate_id      uuid NOT NULL REFERENCES candidate_profiles(id),
  studio_unread     int NOT NULL DEFAULT 0,
  candidate_unread  int NOT NULL DEFAULT 0,
  last_message_at   timestamptz,
  is_closed         boolean NOT NULL DEFAULT false,
  created_at        timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE messages (
  id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  conversation_id uuid NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
  sender_user_id  uuid NOT NULL REFERENCES users(id),
  body            text NOT NULL CHECK (char_length(body) BETWEEN 1 AND 2000),
  sent_at         timestamptz NOT NULL DEFAULT now(),
  read_at         timestamptz
);
CREATE INDEX ix_msg_conv ON messages (conversation_id, sent_at);

CREATE TABLE interview_slots (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id      uuid NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
  proposed_by_user_id uuid NOT NULL REFERENCES users(id),
  starts_at           timestamptz NOT NULL,
  duration_min        smallint NOT NULL CHECK (duration_min IN (15,30,45,60,90)),
  mode                text NOT NULL CHECK (mode IN ('video','phone','onsite')),
  location_or_link    text,
  status              text NOT NULL DEFAULT 'proposed'
                      CHECK (status IN ('proposed','confirmed','declined','completed','no_show_candidate','no_show_studio','cancelled')),
  confirmed_at        timestamptz,
  created_at          timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_slots_app ON interview_slots (application_id, starts_at);

CREATE TABLE application_notes (
  id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id uuid NOT NULL REFERENCES applications(id) ON DELETE CASCADE,
  author_user_id uuid NOT NULL REFERENCES users(id),
  body           text NOT NULL CHECK (char_length(body) <= 2000),
  created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE rejection_templates (
  id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  studio_id   uuid REFERENCES studio_profiles(id) ON DELETE CASCADE,   -- NULL = system template
  reason_code text NOT NULL,
  subject     text NOT NULL,
  body        text NOT NULL
);
```

### Title verification (two levels)

- **Title verified** means the game exists and plausibly matches. IGDB lookup (Twitch OAuth client credentials, cache the token): search by normalized name; accept when name similarity is 0.85 or more and either the studio name matches an involved company or the release year is within 1 year. Also accept a store URL on an allowlist (Steam, Google Play, App Store, itch.io) that returns HTTP 200. **Fetch URLs server-side with SSRF protection** (block private and loopback IPs, 3-second timeout, no redirects to other hosts). Titles that fail both checks stay unverified and go to an admin queue.
- IGDB does not cover many Indian mobile and NDA titles, so store links and the studio route below are the main path for those.
- **Credit verified** means this person really worked on it. Two routes: (a) **Studio confirmation**: the candidate requests it, the verified studio that owns the title gets a one-click confirm of name, role and years; (b) **Coworker confirmation**: 2 coworkers on the same title who are themselves title-verified, have phone-verified accounts, and have profiles older than 30 days confirm the role. One confirmation per person per title; confirmers cannot confirm each other reciprocally in the same week (anti-collusion).
- NDA titles show Under NDA, the role and the studio, and can still be studio-confirmed. Badges: Title verified, Credit verified. The old Blockbuster 1M+ badge is dropped because no reliable sales source exists.
- Evidence effect: engine skill of a credit-verified title becomes evidence level 2; of a title-verified title becomes level 1.

### Shipped-title relevance bonus (matching v2.1)

Add up to +8 points to `fit_for_studio` before capping at 100: for the best-matching shipped title, +4 if its engine equals a required job engine, +2 if its platform overlaps the job's studio platforms, +2 if genres overlap. Unverified titles count half; credit-verified count in full.

### Invites and conversations

- A studio can invite a candidate only when `passes_filters` is true for that job and `fit_for_studio` is at least 60. Daily limit `limits.invites_per_studio_per_day` (default 20 once payments exist, 5 during the free launch).
- A candidate can have at most 5 pending invites per week; further invites wait until older ones are answered. Invites expire after 7 days and count toward the studio responsiveness metric the same way as applications.
- Accepting an invite creates an application with source invite, status shortlisted, contact released, and opens a conversation. A conversation is also opened whenever an application reaches shortlisted. **There are no cold messages.**
- Chat: `ChatHub` (SignalR), 2000 characters, no attachments, unread counters, rate limit 20 messages per minute, report button on every message.
- Interview scheduling: the studio proposes up to 3 slots (Asia/Kolkata by default, stored UTC); the candidate confirms one; both get an ICS file and reminders 24 hours and 1 hour before; either side can mark a no-show. Show candidates a studio no-show count and studios a candidate no-show count after 3 or more interviews.
- Pipeline view for studios: kanban board (drag between statuses with a plain JS drag-and-drop), internal notes, rejection templates, team members with recruiter and viewer roles.

### Reciprocal ranking (algorithm v3, behind a feature flag)

```text
appeal_for_candidate = 0.40 x salary_attract + 0.25 x work_pref + 0.20 x studio_response + 0.15 x skill_alignment
  salary_attract  = 100 if job_max >= expected; else max(0, 100 - 300 x (expected - job_max) / expected)
  work_pref       = 100 if work mode and city fit the candidate's stated preference; 60 if relocation needed; 80 otherwise
  studio_response = response_rate_90d (use 60 for studios with fewer than 10 applications)
  skill_alignment = 100 x (candidate's primary skills that appear in the job's skills) / (number of primary skills, max 5)

final_score = sqrt( fit_for_studio x appeal_for_candidate )
```

Candidate feeds rank by `final_score`; studio applicant lists still rank by `fit_for_studio`. Roll out with a deterministic hash of the candidate id into two groups and compare apply-to-shortlist rate between v2 and v3 for 4 weeks before switching everyone.

### Build order

1. Entities, configurations, migration for all Phase 5 tables.
2. IGDB client + store URL verifier with tests (including SSRF cases), admin queue for failed checks.
3. Credit confirmation flows and evidence recalculation; add the relevance bonus to the engine with new unit tests.
4. Invites with limits; inbox for candidates.
5. Conversations, `ChatHub`, notifications for messages.
6. Interview scheduling with ICS and reminders.
7. Kanban pipeline, notes, templates, studio team roles.
8. Reciprocal ranking v3, feature flag, experiment logging.

### Acceptance

A candidate cannot get credit-verified with accounts they control. A studio cannot message a candidate who has not applied or accepted an invite. A candidate with 5 pending invites receives no more until they respond. The v2 and v3 experiment groups are logged and comparable.

## 10. Phase 6 – Monetization (Months 7–9)

**Gate before you build this phase:** at least 100 active jobs, 30 studios active in the last 30 days, and 15 hires confirmed by both sides. If these are not met, fix supply and response rates instead of adding billing.

**Principles:** charge for access and capacity, not for a hire event the studio can hide by moving to WhatsApp. Hires are still recorded (with both-side confirmation) for statistics, benchmarks and a future hire guarantee.

### Plans (all prices are settings, editable in admin)

| Plan | Price | What it unlocks |
| --- | --- | --- |
| Studio Free | 0 | 3 active jobs, 100 profile views per day, 5 invites per day |
| Job credit | about 1,499 per job (30 days) | One extra active job slot; suits small studios that hire in bursts |
| Studio Pro | about 4,999 per month | Unlimited jobs, 300 profile views and 20 invites per day, 5 team seats, analytics, priority review |
| Candidate Free | 0 | Everything core, including applying within caps |
| Candidate Premium | about 299 per month | Profile boost (+3 rank, labelled), see which verified studios viewed you, salary insights, early job alerts |

### Tables introduced

```sql
CREATE TABLE plans (
  id           int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  code         text NOT NULL UNIQUE,         -- studio_free, studio_pro, job_credit, candidate_premium
  audience     text NOT NULL CHECK (audience IN ('studio','candidate')),
  name         text NOT NULL,
  price_paise  bigint NOT NULL CHECK (price_paise >= 0),
  billing      text NOT NULL CHECK (billing IN ('month','year','one_time')),
  limits       jsonb NOT NULL,               -- { active_jobs, views_per_day, invites_per_day, seats }
  is_active    boolean NOT NULL DEFAULT true
);

CREATE TABLE subscriptions (
  id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  owner_user_id            uuid NOT NULL REFERENCES users(id),
  studio_id                uuid REFERENCES studio_profiles(id),
  plan_id                  int NOT NULL REFERENCES plans(id),
  status                   text NOT NULL CHECK (status IN ('trialing','active','past_due','cancelled','expired')),
  trial_ends_at            timestamptz,
  current_period_start     timestamptz,
  current_period_end       timestamptz,
  cancel_at_period_end     boolean NOT NULL DEFAULT false,
  razorpay_subscription_id text UNIQUE,
  created_at               timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE payments (
  id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id             uuid NOT NULL REFERENCES users(id),
  studio_id           uuid REFERENCES studio_profiles(id),
  subscription_id     uuid REFERENCES subscriptions(id),
  purpose             text NOT NULL CHECK (purpose IN ('subscription','job_credit','boost')),
  amount_paise        bigint NOT NULL,                 -- total charged, including GST
  gst_paise           bigint NOT NULL DEFAULT 0,
  currency            char(3) NOT NULL DEFAULT 'INR',
  status              text NOT NULL CHECK (status IN ('created','captured','failed','refunded')),
  razorpay_order_id   text,
  razorpay_payment_id text UNIQUE,                     -- idempotency key
  billing_gstin       text,
  invoice_number      text UNIQUE,                     -- HGD/2026-27/000123, one sequence per financial year
  invoice_path        text,
  paid_at             timestamptz,
  created_at          timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE job_credits (
  id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  studio_id  uuid NOT NULL REFERENCES studio_profiles(id),
  payment_id uuid NOT NULL REFERENCES payments(id),
  job_id     uuid REFERENCES jobs(id),                 -- set when the credit is used
  granted_at timestamptz NOT NULL DEFAULT now(),
  expires_at timestamptz NOT NULL
);

CREATE TABLE hire_events (
  id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  application_id           uuid NOT NULL UNIQUE REFERENCES applications(id),
  studio_confirmed_at      timestamptz,
  candidate_confirmed_at   timestamptz,
  joined_on                date,
  final_ctc_lpa            numeric(6,2),               -- optional, entered by candidate, feeds benchmarks
  status                   text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','confirmed','disputed'))
);

CREATE TABLE webhook_events (                           -- idempotent webhook processing
  id           text PRIMARY KEY,                          -- Razorpay event id
  event_type   text NOT NULL,
  payload      jsonb NOT NULL,
  received_at  timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz
);
```

### Rules

- **One entitlement gate:** all limits (active jobs, views, invites, seats) go through `IEntitlementService.GetForStudio(studioId)`, which merges the plan limits, valid job credits and app\_settings defaults. Replace the direct app\_settings checks from earlier phases.
- **Razorpay:** one-time payments use Orders; recurring plans use the Subscriptions API (cards and UPI AutoPay). Verify the checkout signature server-side and every webhook with HMAC SHA256 using the webhook secret. Insert the event id into `webhook_events` first; if it already exists, stop. Handle payment captured, subscription activated, charged, halted and cancelled. Check the current RBI e-mandate rules and Razorpay limits before promising free trials with auto-charge.
- **GST invoices:** generate a PDF per payment with GST breakdown, sequential invoice numbers per financial year, and GSTIN when the studio provides one. Confirm the invoice format with your accountant.
- **Dunning:** failed renewal sets past\_due with a 7-day grace period, then downgrades to Free (jobs above the free limit are paused, never deleted).
- **Hire confirmation:** when a studio marks Hired, the candidate is asked to confirm joining. Both confirmations create a confirmed hire event; the candidate may enter final CTC for benchmarks. A confirmed hire gives the studio one free month of Pro credit (optional growth lever).
- **Boost:** `boosted_until` is set from the Premium subscription. It adds +3 rank points in studio candidate search and never changes the displayed match.
- **Analytics for Pro studios:** views to applications to shortlist to interview to hire funnel per job, time to first response, source mix, and a comparison with the median for similar jobs.

### Build order

1. Plans, entitlements, and the `IEntitlementService` refactor with tests (no payments yet).
2. Razorpay test mode: orders, checkout, signature verification, webhook endpoint with idempotency.
3. Subscriptions, trial, dunning job, customer billing page.
4. Invoices with PDF generation.
5. Hire confirmation flow and hire\_events.
6. Candidate Premium features and studio analytics.

### Acceptance

A replayed webhook never creates a second payment. A studio past its job limit sees an upgrade prompt, not an error. An expired subscription pauses extra jobs but loses no data. A boosted candidate appears slightly higher but the match percentage is unchanged.

## 11. Phase 7 – Growth Features (Months 10–15)

Build these one at a time, each behind a feature flag, and keep only what moves hires.

| Feature | Design notes | New tables |
| --- | --- | --- |
| Salary benchmarks | Datapoints from confirmed hires, posted job ranges (weighted low) and optional candidate input. Publish a bucket only when it has **at least 5 datapoints**, and show a range, never single values. Feed the helper text in the job and profile forms | salary\_datapoints |
| Skill challenges and badges | Auto-graded timed question sets per skill, randomized pool, retake after 30 days. Score of 80 percent or more gives evidence level 2 for that skill | skill\_challenges, challenge\_attempts, user\_badges |
| Layoff response | Admin activates an event for a named studio. Affected candidates **opt in** to a Laid off flag; verified studios see a prioritized list and a banner. Candidates can hide the flag at any time | layoff\_events, candidate\_profiles.layoff\_event\_id |
| Team preview | A job page can show team members, but only those who have accepted a consent request | job\_team\_members |
| Referrals | Candidate shares a link; credit (Premium days) only after a confirmed hire from the referral | referrals |
| Studio reviews | Only candidates with confirmed hires or completed interviews can review, anonymously; show a studio's reviews only after 3 or more; moderation queue | studio\_reviews |
| WhatsApp alerts | Opt-in only, via an approved WhatsApp Business provider and template messages | notification\_preferences.channel |
| AI assistants | Job description improver and profile tips, using the paid LLM tier with PII redacted | none |
| Community (team finder, jam events) | Build only if hiring metrics are healthy; otherwise skip to avoid becoming a social network | later |

```sql
CREATE TABLE salary_datapoints (
  id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  category   text NOT NULL,
  seniority  text NOT NULL,
  skill_id   int REFERENCES skills(id),         -- usually the engine
  city_id    int REFERENCES cities(id),
  ctc_lpa    numeric(6,2) NOT NULL,
  source     text NOT NULL CHECK (source IN ('hire','job_post','candidate')),
  weight     numeric(3,2) NOT NULL DEFAULT 1.0, -- hire 1.0, candidate 0.6, job_post 0.4
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE skill_challenges (
  id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  skill_id int NOT NULL REFERENCES skills(id),
  title text NOT NULL, duration_min smallint NOT NULL, pass_percent smallint NOT NULL DEFAULT 80,
  question_bank jsonb NOT NULL, is_active boolean NOT NULL DEFAULT true
);
CREATE TABLE challenge_attempts (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  challenge_id int NOT NULL REFERENCES skill_challenges(id),
  candidate_id uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  started_at timestamptz NOT NULL DEFAULT now(), submitted_at timestamptz,
  score_percent numeric(5,2), passed boolean
);
CREATE TABLE user_badges (
  candidate_id uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  skill_id int NOT NULL REFERENCES skills(id),
  badge_type text NOT NULL CHECK (badge_type IN ('challenge_passed','credit_verified_title')),
  earned_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (candidate_id, skill_id, badge_type)
);

CREATE TABLE layoff_events (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  studio_name text NOT NULL, announced_on date NOT NULL, headcount_affected int,
  is_active boolean NOT NULL DEFAULT true, created_by uuid NOT NULL REFERENCES users(id),
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE job_team_members (
  job_id uuid NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
  candidate_id uuid NOT NULL REFERENCES candidate_profiles(id) ON DELETE CASCADE,
  display_role text NOT NULL, consented_at timestamptz,
  PRIMARY KEY (job_id, candidate_id)
);
CREATE TABLE referrals (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  referrer_candidate_id uuid NOT NULL REFERENCES candidate_profiles(id),
  referred_user_id uuid REFERENCES users(id), code text NOT NULL UNIQUE,
  rewarded_at timestamptz, created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE studio_reviews (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  studio_id uuid NOT NULL REFERENCES studio_profiles(id),
  reviewer_candidate_id uuid NOT NULL REFERENCES candidate_profiles(id),
  application_id uuid NOT NULL UNIQUE REFERENCES applications(id),
  rating_process smallint CHECK (rating_process BETWEEN 1 AND 5),
  rating_communication smallint CHECK (rating_communication BETWEEN 1 AND 5),
  rating_offer_fairness smallint CHECK (rating_offer_fairness BETWEEN 1 AND 5),
  comment text CHECK (char_length(comment) <= 1000),
  status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','published','removed')),
  created_at timestamptz NOT NULL DEFAULT now()
);
```

## 12. Phase 8 – Scale and Ecosystem (Months 16+)

- **ML re-ranking:** when you have about 5,000 shortlist events and 50,000 impressions in `match_events`, train a gradient-boosted model (ML.NET LightGBM) to predict the chance of shortlist from the existing features (skill, experience, logistics scores, evidence mix, studio response rate, freshness). Use it only to re-rank candidates that already pass the hard filters; keep the rule-based score as the displayed percentage and as the fallback. Evaluate offline with NDCG@10 and AUC, then A/B test.
- **Infrastructure:** move uploads to S3-compatible storage, add a read replica, a second app server with a Redis SignalR backplane, and a CDN.
- **Public API and ATS integration** with API keys, scopes and rate limits. **PWA** first, native apps only if usage demands it.
- **International:** currency, locale and time zone fields, region-specific legal pages.

### Investor and publisher module (separate)

Build this only after hiring is working, and validate the demand by hand first: curate 5–10 projects and introduce them to a few gaming-focused funds, publishers and angels by email. Have a lawyer review the model before building (securities rules in India can apply when you connect investors and unlisted companies or take a fee tied to funds raised). Never process funds on the platform, use subscriptions rather than a success fee, and never expose candidate data to investors.

```sql
CREATE TABLE investor_profiles (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id       uuid NOT NULL UNIQUE REFERENCES users(id),       -- account_type gains the value 'investor'
  investor_type text NOT NULL CHECK (investor_type IN ('angel','vc_fund','publisher','accelerator')),
  org_name      text NOT NULL, website text,
  ticket_min_inr bigint, ticket_max_inr bigint,
  focus_skill_ids int[] NOT NULL DEFAULT '{}',                    -- genres and platforms
  stages        text[] NOT NULL DEFAULT '{}',                      -- prototype, vertical_slice, beta, launched
  verification_status text NOT NULL DEFAULT 'unverified' CHECK (verification_status IN ('unverified','pending','verified','rejected')),
  verified_at   timestamptz, bio text
);
CREATE TABLE funding_projects (
  id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  studio_id     uuid NOT NULL REFERENCES studio_profiles(id),
  title         text NOT NULL, pitch text NOT NULL,
  stage         text NOT NULL CHECK (stage IN ('prototype','vertical_slice','beta','launched')),
  funding_type  text NOT NULL CHECK (funding_type IN ('equity','revenue_share','publisher_deal','sponsorship')),
  ask_amount_inr bigint, use_of_funds text, team_summary text,
  build_url text, trailer_url text,
  nda_required  boolean NOT NULL DEFAULT false,
  status        text NOT NULL DEFAULT 'draft' CHECK (status IN ('draft','pending_review','live','closed')),
  created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE intro_requests (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  funding_project_id uuid NOT NULL REFERENCES funding_projects(id) ON DELETE CASCADE,
  investor_id uuid NOT NULL REFERENCES investor_profiles(id),
  initiated_by text NOT NULL CHECK (initiated_by IN ('investor','studio')),
  message text CHECK (char_length(message) <= 500),
  status text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','accepted','declined','expired')),
  created_at timestamptz NOT NULL DEFAULT now(), responded_at timestamptz,
  UNIQUE (funding_project_id, investor_id)
);
```

Reuse the two-way pattern: every project is reviewed by an admin, investors need verification, an intro becomes a conversation only after both sides accept, and the studio decides what each investor can see (NDA first for unreleased IP).

## 13. Cross-Cutting Reference

### A. Rate limits and anti-abuse

| Action | Limit | Where |
| --- | --- | --- |
| Login attempts | 5 per 15 min per IP + lockout per account | Identity + AddRateLimiter |
| Registration | 5 per hour per IP | AddRateLimiter |
| Resume import | 5 per day per user | settings + resume\_parse\_logs |
| Phone OTP | 3 per hour, 5 per day per number | OTP service |
| Applications | 15 per day per candidate; 50 active per job | settings + transaction |
| Studio profile views | 100 per day (Free), 300 (Pro) | entitlements |
| Invites | 5 per day (Free), 20 (Pro); candidate inbox max 5 pending per week | entitlements |
| Chat messages | 20 per minute per user | hub filter |
| Reports | 10 per day per user | settings |
| Job posting | duplicates (same title, city, studio within 7 days) blocked | IJobService |

### B. Security checklist

- Every Razor Page has an authorization attribute; every id-based load checks ownership or studio membership. Write one test per page proving a second user is denied.
- Anti-forgery tokens on all POST handlers. Output encoding by default; never use `Html.Raw` on user content. Content-Security-Policy header, HSTS, secure and HttpOnly cookies, SameSite Lax.
- Parameterized queries only (EF Core); no string-built SQL. Escape LIKE input.
- Uploads: extension allowlist, magic-byte check, size limit, random names, stored outside wwwroot, served through an authorized endpoint.
- Outbound URL fetches (store links) use SSRF protection.
- Secrets in environment variables; rotate keys; separate test and live Razorpay keys.
- Passwords: Identity defaults with minimum length 10 and breach-list check if available. Optional 2FA for studios and admins.
- Admin actions always write `audit_logs` with a reason. Dependency updates monthly (`dotnet list package --vulnerable`).

### C. Deployment and operations

- Nginx terminates TLS (Let's Encrypt) and proxies to Kestrel; run the app as a systemd service with `EnvironmentFile` for secrets; PostgreSQL bound to localhost only; firewall allows 22, 80, 443.
- Migrations: build an EF migrations bundle in CI and run it on deploy before restarting the app. Keep a staging environment with the same setup.
- Backups: daily `pg_dump` (custom format) pushed off-server (Backblaze B2, Cloudflare R2 or similar), 30-day retention, uploads synced the same way. **Restore-test every month.** Document the recovery steps and target recovery time.
- Hangfire dashboard protected by AdminOnly. Serilog logs rotated daily. Uptime check on `/health`.
- Single server in the MVP: no Redis. Add it only with a second app server.

### D. Testing strategy

- **Unit tests (Core):** matching engine, evidence rules, completeness, salary normalization, entitlement rules. These are the most valuable tests in the project.
- **Integration tests (Testcontainers PostgreSQL):** application transaction and cap race, search queries, anti-ghosting jobs with a fake clock, webhook idempotency.
- **Authorization tests:** one per page and handler.
- **Golden tests:** the worked example in Phase 3 and a CSV of 20 candidate-job pairs with expected scores that you extend whenever a bug is found.

### E. Metrics to watch

- **North star:** confirmed hires per month.
- Supply and demand: active jobs, active candidates, applications per job (target 5–20), jobs with zero applications after 14 days.
- Responsiveness: studio response rate, median time to first response, share of auto-declined applications (target under 15 percent).
- Matching quality: apply-to-shortlist rate by match band, shortlist rate for recommended vs searched jobs, share of applications with a filter warning.
- Trust: share of shipped titles verified, reports per 1,000 jobs, fake-profile removals.
- Revenue (from Phase 6): paying studios, job credits sold, churn, trial-to-paid.

### F. Decision log (start with these)

| Decision | Why |
| --- | --- |
| Hard filters before scoring | Dealbreakers should not be hidden inside a percentage |
| Evidence factor in skill score | Stops keyword stuffing and rewards real proof |
| Years-derived seniority | Self-declared level is easy to inflate |
| Current CTC hidden by default | Prevents anchoring on past pay |
| No cold messages | Prevents spam and scams |
| Payments after Phase 4 | No one pays before there is supply and responses |
| Charge for access, not hires | Hire fees leak off-platform |
| Free-tier AI is never used for resumes | Personal data must not be used for model training |
| Investor module is separate and last | Different risk, different users, legal review first |
