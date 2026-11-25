// Destructive browser smoke test: local Development application and fictitious accounts only.
// See docs/APPLICATION_WORKFLOW.md. Credentials are read from environment, never printed.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');

function required(name) {
    const value = process.env[name];
    if (!value) throw new Error(`Missing environment variable ${name}.`);
    return value;
}
function localUrl(raw) {
    const url = new URL(raw);
    if (!['http:', 'https:'].includes(url.protocol) || !['localhost', '127.0.0.1', '[::1]'].includes(url.hostname) || url.username || url.password)
        throw new Error('HGD_E2E_BASE_URL must be a loopback HTTP(S) origin without credentials.');
    if (url.pathname !== '/' || url.search || url.hash) throw new Error('HGD_E2E_BASE_URL must contain only the origin.');
    return url.origin;
}
const origin = localUrl(required('HGD_E2E_BASE_URL'));
if (required('HGD_E2E_DISPOSABLE') !== 'YES') throw new Error('Set HGD_E2E_DISPOSABLE=YES only for an isolated disposable database and test accounts.');
const jobId = required('HGD_E2E_JOB_ID');
if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(jobId)) throw new Error('HGD_E2E_JOB_ID must be a GUID.');
const slug = required('HGD_E2E_JOB_SLUG');
if (!/^[a-z0-9-]{1,160}$/.test(slug)) throw new Error('HGD_E2E_JOB_SLUG must be a lowercase job slug.');
const candidate = { email: required('HGD_E2E_CANDIDATE_EMAIL'), password: required('HGD_E2E_CANDIDATE_PASSWORD') };
const owner = { email: required('HGD_E2E_OWNER_EMAIL'), password: required('HGD_E2E_OWNER_PASSWORD') };
const viewer = { email: required('HGD_E2E_VIEWER_EMAIL'), password: required('HGD_E2E_VIEWER_PASSWORD') };
const phone = required('HGD_E2E_CANDIDATE_PHONE');
const answer = 'Fictitious application smoke test answer.';
let step = 'setup';

async function main() {
    const browser = await chromium.launch({ headless: true });
    const contexts = [];
    try {
        async function login(account) {
            const context = await browser.newContext({ ignoreHTTPSErrors: true });
            contexts.push(context);
            // Reject every non-local origin, including redirects and background requests.
            await context.route('**/*', route => {
                const url = new URL(route.request().url());
                return url.origin === origin ? route.continue() : route.abort();
            });
            const page = await context.newPage();
            await page.goto(`${origin}/Account/Login`);
            await page.locator('#Email').fill(account.email);
            await page.locator('#Password').fill(account.password);
            await Promise.all([page.waitForURL(url => !url.pathname.toLowerCase().endsWith('/account/login')), page.getByRole('button', { name: 'Sign in', exact: true }).click()]);
            return page;
        }
        step = 'Candidate login and clean-account preflight';
        const candidatePage = await login(candidate);
        await candidatePage.goto(`${origin}/candidate/applications`);
        await candidatePage.getByRole('heading', { name: 'My applications', exact: true }).waitFor();
        assert.equal(await candidatePage.locator('a[href*="/withdraw"]').count(), 0, 'Use a fresh Candidate with no other applications.');
        step = 'Candidate review and submission';
        await candidatePage.goto(`${origin}/jobs/${slug}`);
        const reviewForm = candidatePage.locator('form').filter({ has: candidatePage.getByRole('button', { name: 'Review application' }) });
        const action = await reviewForm.getAttribute('action');
        assert.ok(action && action.toLowerCase().includes(jobId.toLowerCase()), 'Job slug and job ID must match.');
        const answerBox = candidatePage.locator('#screeningAnswer');
        if (await answerBox.count()) await answerBox.fill(answer);
        await candidatePage.getByRole('button', { name: 'Review application' }).click();
        await candidatePage.getByRole('heading', { name: 'Review before applying' }).waitFor();
        if (await answerBox.count()) assert.fail('Unexpected job page after review.');
        await candidatePage.locator('input[name="consent"]').check();
        await candidatePage.getByRole('button', { name: 'Submit application' }).click();
        await candidatePage.getByText('Application submitted.', { exact: true }).waitFor();
        // The fresh test Candidate must have exactly one withdrawable application.
        const withdrawLinks = candidatePage.locator('a[href*="/withdraw"]');
        assert.equal(await withdrawLinks.count(), 1, 'Use a fresh Candidate with no other applications.');
        const withdrawHref = await withdrawLinks.first().getAttribute('href');
        const match = withdrawHref.match(/\/([0-9a-f-]{36})\/withdraw\/?$/i);
        assert.ok(match, 'Could not identify the submitted application.');
        const applicationId = match[1];

        step = 'Studio Owner review and shortlist';
        const ownerPage = await login(owner);
        await ownerPage.goto(`${origin}/studio/applications/job/${jobId}`);
        await ownerPage.getByRole('heading', { name: 'Applicants', exact: true }).waitFor();
        await ownerPage.locator(`a[href$="/${applicationId}"]`).click();
        await ownerPage.getByRole('heading', { name: 'Applicant snapshot' }).waitFor();
        assert.equal(await ownerPage.getByText(phone, { exact: true }).count(), 0, 'Phone must be hidden before shortlist.');
        await ownerPage.locator('#newStatus').selectOption({ label: 'Shortlisted' });
        await ownerPage.getByLabel('I confirm this status change').check();
        await ownerPage.getByRole('button', { name: 'Update', exact: true }).click();
        await ownerPage.getByText('Status updated.', { exact: true }).waitFor();
        await ownerPage.getByText(phone, { exact: true }).waitFor();

        step = 'Studio Viewer read-only access';
        const viewerPage = await login(viewer);
        await viewerPage.goto(`${origin}/studio/applications/${applicationId}`);
        await viewerPage.getByText('Viewer access is read-only.', { exact: true }).waitFor();
        await viewerPage.getByText(phone, { exact: true }).waitFor();
        assert.equal(await viewerPage.locator('#newStatus').count(), 0, 'Viewer must not receive status controls.');

        step = 'Candidate withdrawal and Studio revocation';
        await candidatePage.goto(`${origin}${withdrawHref}`);
        await candidatePage.getByRole('button', { name: 'Confirm withdrawal' }).click();
        await candidatePage.getByText('Application withdrawn. Studio access to the snapshot is revoked.', { exact: true }).waitFor();
        const denied = await ownerPage.goto(`${origin}/studio/applications/${applicationId}`);
        assert.equal(denied.status(), 404, 'Studio detail must be unavailable after withdrawal.');
        console.log('PASS: review, consent, submission, shortlist, viewer read-only, phone visibility and withdrawal.');
    } finally {
        for (const context of contexts) await context.close();
        await browser.close();
    }
}
main().catch(() => {
    // No page content, account details, tokens or credentials are logged.
    console.error(`Application workflow browser smoke test failed during: ${step}. Check disposable setup and the manual walkthrough.`);
    process.exitCode = 1;
});
