// Dependency-free behavioral harness for the production autosave script (not a browser test).
// Run from repository root: node scripts/test-job-autosave.cjs
const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = readFileSync('src/HireGameDev.Web/wwwroot/js/studio-job-editor.js', 'utf8');
function harness({ existing = true, stopped = false } = {}) {
    const listeners = {};
    const version = { value: '7' };
    const id = { value: existing ? 'test-id' : '' };
    const status = { textContent: '', dataset: {} };
    const reload = { hidden: true };
    const state = { title: 'Original', calls: 0, submitted: 0 };
    const form = {
        dataset: { autosaveUrl: '/Studio/Jobs/Autosave', autosaveStopped: String(stopped) },
        querySelector: name => name.includes('Version') ? version : id,
        addEventListener: (name, callback) => { listeners[name] = callback; },
        requestSubmit: () => { assert.equal(version.value, '8'); state.submitted++; }
    };
    let tick;
    let unload;
    const context = {
        document: { getElementById: name => ({ 'job-editor': form, 'job-save-status': status, 'job-reload': reload }[name] || null) },
        FormData: class {
            constructor() { this.values = [['Content.Title', state.title], ['Version', version.value]]; }
            entries() { return this.values.values(); }
        },
        AbortController,
        fetch: async (...args) => { state.calls++; return state.fetch(...args); },
        window: {
            setInterval: (callback, milliseconds) => { assert.equal(milliseconds, 30000); tick = callback; },
            setTimeout: () => 1, clearTimeout: () => {},
            addEventListener: (name, callback) => { if (name === 'beforeunload') unload = callback; }
        }
    };
    state.fetch = async () => response(200, { version: 8, jobId: 'test-id' });
    vm.runInNewContext(source, context);
    return { state, version, status, reload, tick: () => tick(), listeners, unload: event => unload(event) };
}
function response(status, body) {
    return { status, ok: status === 200, redirected: false, headers: { get: () => 'application/json' }, json: async () => body };
}
(async () => {
    let h = harness();
    await h.tick(); assert.equal(h.state.calls, 0, 'unchanged form must not save');
    h.state.title = 'Edited'; await h.tick();
    assert.equal(h.state.calls, 1); assert.equal(h.version.value, '8');
    await h.tick(); assert.equal(h.state.calls, 1, 'successful baseline must suppress redundant saves');

    h = harness({ existing: false }); h.state.title = 'New'; await h.tick();
    assert.equal(h.state.calls, 0, 'first draft requires explicit save');
    let warned = false; h.unload({ preventDefault: () => { warned = true; } }); assert.equal(warned, true);

    h = harness(); h.state.title = 'Conflict';
    h.state.fetch = async () => response(409, { message: 'Changed elsewhere.' });
    await h.tick(); assert.equal(h.version.value, '7'); assert.equal(h.reload.hidden, false);
    assert.match(h.status.textContent, /Changed elsewhere/);
    await h.tick(); assert.equal(h.state.calls, 1, 'conflict must stop subsequent autosaves');

    h = harness(); h.state.title = 'In flight';
    let resolve;
    h.state.fetch = () => new Promise(done => { resolve = done; });
    let pending = h.tick(); h.state.title = 'Typed during save';
    await h.tick(); assert.equal(h.state.calls, 1, 'requests cannot overlap');
    resolve(response(200, { version: 8, jobId: 'test-id' })); await pending;
    assert.equal(h.state.title, 'Typed during save'); assert.match(h.status.textContent, /Unsaved changes/);

    h = harness(); h.state.title = 'Manual race';
    h.state.fetch = () => new Promise(done => { resolve = done; }); pending = h.tick();
    let prevented = false;
    h.listeners.submit({ submitter: {}, preventDefault: () => { prevented = true; } });
    assert.equal(prevented, true); assert.equal(h.state.submitted, 0);
    resolve(response(200, { version: 8, jobId: 'test-id' })); await pending;
    assert.equal(h.state.submitted, 1, 'manual save waits for fresh token');

    h = harness(); h.state.title = 'Offline'; h.state.fetch = async () => { throw Error('offline'); };
    await h.tick(); await h.tick(); assert.equal(h.state.calls, 1, 'unknown commit outcome must not retry blindly');
    assert.equal(h.reload.hidden, false);

    h = harness(); h.state.title = 'Session expired';
    h.state.fetch = async () => ({ redirected: true, headers: { get: () => 'text/html' } });
    await h.tick(); await h.tick(); assert.equal(h.state.calls, 1); assert.match(h.status.textContent, /session/);

    h = harness({ stopped: true }); h.state.title = 'Manual save failed'; await h.tick();
    assert.equal(h.state.calls, 0, 'failed manual save cannot restart background saves');
    console.log('PASS: autosave baseline, first-save gate, conflict stop, in-flight edits, overlap prevention, manual-save ordering, network/auth failure and stopped state.');
})().catch(error => { console.error(error); process.exitCode = 1; });
