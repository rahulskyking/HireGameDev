# Application workflow

This workflow is available automatically in the ASP.NET Core `Development` environment. It is not enabled outside Development. It is **not approved for real personal data, public use or production deployment**. The retention deletion prototype is still disabled and no application PDF feature exists.

Run the Development app normally and sign in as a Candidate or Studio. No extra application-feature setting is required. Use a disposable local PostgreSQL database and fictional accounts/data. Apply the checked-in migrations against that database (see `POSTGRES_TESTS.md`).

## Manual walkthrough

Before step 1, sign in as the Studio Owner or Recruiter, create a job draft in Studio Jobs, complete the required fields, preview it and publish it. The Studio must be verified and the publication checks must pass. Use only fictional data.

1. With an active Candidate account and complete profile, open an active job and provide its screening answer if appropriate. Choose **Review application**. Check all snapshot fields, the consent text and phone notice. Agree and submit; the success page opens the Candidate timeline. Verify a stale or altered review cannot be submitted, and that retry returns to review without losing the answer or reusing consent.
2. Open **My applications** and its timeline. Verify the submission and status changes appear, then test withdrawal and confirmation using a separate test application; verify Studio access is revoked.
3. As an active verified Studio Owner or Recruiter, open that job's management page and choose **View applicants** (or choose **Applicants** in the Studio job list). Filter by status, open a submitted snapshot and transition its status using allowed transitions. Names on the list come from the submitted snapshot, not the live profile. Verify phone appears only for Shortlisted or Hired.
4. Repeat as a Studio Viewer: list and detail are read-only. Check a non-member and a Candidate-blocked Studio cannot retrieve the snapshot; also check an inactive account and a withdrawn/purged application. Never use real candidate data.
5. Exercise pagination, role/access changes between requests, form anti-forgery, expired review tokens, invalid transitions and concurrent requests. Listing uses offset pagination; concurrent changes can shift rows. Candidate history also uses offset pagination; its job and Studio names are current values rather than the submitted snapshot.

## Before any exposure

Run `dotnet build`, `dotnet test`, and the non-skipped PostgreSQL integration suite described in `POSTGRES_TESTS.md`, plus the manual walkthrough. These checks are not a substitute for privacy/legal review; access, retention, account deletion, PDF storage, backup and audit/operational handling remain unfinished. Complete the wider deadlock/race and load tests, deployment review and end-to-end/browser tests before deciding whether to change the Development-only guard. Do not enable this for production as a configuration shortcut.

## Optional browser smoke test

`scripts/test-application-workflow.cjs` is a destructive browser smoke test. It submits and withdraws an application, changes its status and consumes that Candidate/job pair (duplicates remain disallowed). Run it **only** against a disposable local database with fictitious data and a fresh Candidate; it never deletes or resets the database. It rejects non-loopback base URLs and blocks browser requests to other origins, but that is not a guarantee that the database configured behind the local server is disposable—verify that yourself. A failure can leave an application or status change behind. Do not use production or shared environments.

Prerequisites: start the Development app; prepare an active job on a verified Studio, a fresh active Candidate with a complete profile and phone, an Owner for that Studio, and a separate Studio Viewer. Use local email/password login. Install Node.js and Playwright locally (`npm install --no-save --no-package-lock playwright`, then `npx playwright install chromium`). No browser binary or credentials belong in Git.

Set these process environment variables privately, without posting credentials in chat or storing them in tracked files:

- `HGD_E2E_BASE_URL`: loopback origin, such as `http://localhost:5000` (no path).
- `HGD_E2E_DISPOSABLE`: `YES`, acknowledging the destructive local test.
- `HGD_E2E_JOB_ID` and `HGD_E2E_JOB_SLUG`: the **same** active job.
- `HGD_E2E_CANDIDATE_EMAIL`, `HGD_E2E_CANDIDATE_PASSWORD`, `HGD_E2E_CANDIDATE_PHONE`.
- `HGD_E2E_OWNER_EMAIL`, `HGD_E2E_OWNER_PASSWORD`.
- `HGD_E2E_VIEWER_EMAIL`, `HGD_E2E_VIEWER_PASSWORD`.

Then run `node scripts/test-application-workflow.cjs`. It checks review/consent/submission, Owner shortlist, phone visibility, Viewer read-only UI, Candidate withdrawal and a subsequent 404 for Studio detail. It does not cover all roles, attacks, accessibility, race conditions or browser configurations, and it does not prove production readiness. The separate PostgreSQL workflow test exercises the services, not HTTP or a browser.

## Consent and privacy status

The current application-share-v2 disclosure describes snapshot sharing, match scoring and the fact that withdrawal does not automatically delete stored records. Changing the version makes outstanding v1 review tokens stale; existing consent records remain unchanged. The Development-only flow still has no self-service account erasure, production retention worker, or verified backup deletion process. The privacy notice is an engineering draft, not a legal approval; do not use real personal data.

Candidates can open their own submitted snapshot from their application timeline, including after withdrawal while it remains stored. Studio access remains revoked after withdrawal. An intentionally purged snapshot is reported as removed; missing or invalid data is only reported as unavailable. This does not implement deletion, account erasure, or a retention schedule.

## Candidate-requested snapshot deletion (Development only)

After withdrawing, the owner can explicitly delete the stored application snapshot, including its phone and screening answer, through the application detail page. A separate confirmation explains that the action cannot be undone and that the application record, consent receipt, history and audit entry remain. The service rechecks the active Candidate owner, withdrawn state, history and matching consent metadata while locking the user, Candidate, job and application; the snapshot deletion, purge marker and audit entry commit together. Retrying an already-purged application is a no-op. It does not delete the account, live profile, third-party downloads or backups. The service is registered only in Development and the controller rejects requests outside Development. Automatic retention and account erasure remain unavailable. Legal holds, backup handling, concurrency verification and privacy review are required before any production release or real personal data.
