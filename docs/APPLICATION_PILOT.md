# Application workflow: local development pilot only

This is an unfinished test workflow, not a production release. It is disabled by default and cannot be enabled outside the ASP.NET Core `Development` environment. It is **not approved for real personal data, a public pilot, or production deployment**. The retention deletion prototype is still disabled and no application PDF feature exists.

Use a disposable local PostgreSQL database and fictitious accounts/data only. Run the checked-in migrations against that database (see `POSTGRES_TESTS.md` and existing setup instructions). In a git-ignored `src/HireGameDev.Web/appsettings.Development.local.json`, merge `"ApplicationPilot": { "Enabled": true }` into the local settings object; alternatively set `ApplicationPilot__Enabled=true` in the local process environment. Never place database credentials in tracked configuration. Restart the Development app. Remove the setting to disable the workflow again.

## Manual walkthrough

1. With an active Candidate account and complete profile, open an active job and provide its screening answer if appropriate. Choose **Review application**. Check all snapshot fields, the consent text and phone notice. Agree and submit. Verify a stale or altered review cannot be submitted.
2. Open **My applications**. Verify the submission appears, then test withdrawal and confirmation using a separate test application; verify Studio access is revoked.
3. As an active verified Studio Owner or Recruiter, open that job's management page and choose **View applicants**. Open a snapshot and transition its status using allowed transitions. Verify phone appears only for Shortlisted or Hired.
4. Repeat as a Studio Viewer: list and detail are read-only. Check a non-member and a Candidate-blocked Studio cannot retrieve the snapshot; also check an inactive account and a withdrawn/purged application. Never use real candidate data.
5. Exercise pagination, role/access changes between requests, form anti-forgery, expired review tokens, invalid transitions and concurrent requests. Listing uses offset pagination; concurrent changes can shift rows. Candidate history also uses offset pagination; its job and Studio names are current values rather than the submitted snapshot.

## Before any exposure

Run `dotnet build`, `dotnet test`, and the non-skipped PostgreSQL integration suite described in `POSTGRES_TESTS.md`, plus the manual walkthrough. These checks are not a substitute for privacy/legal review; access, retention, account deletion, PDF storage, backup and audit/operational handling remain unfinished. Complete the wider deadlock/race and load tests, deployment review and end-to-end/browser tests before deciding whether to change the Development-only guard. Do not enable this for production as a configuration shortcut.

## Optional browser smoke test

`scripts/test-application-pilot.cjs` is a destructive browser smoke test. It submits and withdraws an application, changes its status and consumes that Candidate/job pair (duplicates remain disallowed). Run it **only** against a disposable local database with fictitious data and a fresh Candidate; it never deletes or resets the database. It rejects non-loopback base URLs and blocks browser requests to other origins, but that is not a guarantee that the database configured behind the local server is disposable—verify that yourself. A failure can leave an application or status change behind. Do not use production or shared environments.

Prerequisites: start the Development app with the pilot enabled; prepare an active job on a verified Studio, a fresh active Candidate with a complete profile and phone, an Owner for that Studio, and a separate Studio Viewer. Use local email/password login. Install Node.js and Playwright locally (`npm install --no-save --no-package-lock playwright`, then `npx playwright install chromium`). No browser binary or credentials belong in Git.

Set these process environment variables privately, without posting credentials in chat or storing them in tracked files:

- `HGD_E2E_BASE_URL`: loopback origin, such as `http://localhost:5000` (no path).
- `HGD_E2E_DISPOSABLE`: `YES`, acknowledging the destructive local test.
- `HGD_E2E_JOB_ID` and `HGD_E2E_JOB_SLUG`: the **same** active job.
- `HGD_E2E_CANDIDATE_EMAIL`, `HGD_E2E_CANDIDATE_PASSWORD`, `HGD_E2E_CANDIDATE_PHONE`.
- `HGD_E2E_OWNER_EMAIL`, `HGD_E2E_OWNER_PASSWORD`.
- `HGD_E2E_VIEWER_EMAIL`, `HGD_E2E_VIEWER_PASSWORD`.

Then run `node scripts/test-application-pilot.cjs`. It checks review/consent/submission, Owner shortlist, phone visibility, Viewer read-only UI, Candidate withdrawal and a subsequent 404 for Studio detail. It does not cover all roles, attacks, accessibility, race conditions or browser configurations, and it does not prove production readiness. The separate PostgreSQL workflow test exercises the services, not HTTP or a browser.
