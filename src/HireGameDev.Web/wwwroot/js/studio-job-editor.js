(() => {
    const search = document.getElementById('job-skill-search');
    if (!search) return;
    search.addEventListener('input', () => {
        const term = search.value.trim().toLocaleLowerCase();
        document.querySelectorAll('[data-job-skill-picker]').forEach(select => {
            for (const option of select.options) {
                // Never hide the selected value; filtering is visual only, not a content change.
                option.hidden = !!option.value && !option.selected && !option.text.toLocaleLowerCase().includes(term);
            }
        });
    });
    const mode = document.getElementById('Content_WorkMode');
    const city = document.getElementById('Content_CityId');
    if (mode && city) {
        const syncCity = () => {
            const remote = mode.value === mode.dataset.remoteValue;
            if (remote) city.value = '';
            city.disabled = remote;
        };
        mode.addEventListener('change', syncCity);
        syncCity();
    }
})();

// Progressive enhancement: the regular POST Save remains authoritative without JavaScript.
(() => {
    const form = document.getElementById('job-editor');
    if (!form) return;
    const status = document.getElementById('job-save-status');
    const reload = document.getElementById('job-reload');
    const version = form.querySelector('[name="Version"]');
    const jobId = form.querySelector('[name="JobId"]');
    const existing = !!jobId?.value;
    const snapshot = data => JSON.stringify(Array.from(data.entries())
        .filter(([name]) => name.startsWith('Content.') || name.startsWith('Skills[')));
    let baseline = form.dataset.unsaved === 'true' ? null : snapshot(new FormData(form));
    let stopped = form.dataset.autosaveStopped === 'true';
    let saving = false;
    let navigating = false;
    let queuedSubmit = null;
    const dirty = () => snapshot(new FormData(form)) !== baseline;
    const show = (text, state) => {
        status.textContent = text;
        status.dataset.state = state;
    };
    const stop = message => {
        stopped = true;
        queuedSubmit = null;
        show(`Autosave stopped. ${message} Your edits remain on this page.`, 'error');
        reload.hidden = false;
    };
    if (stopped) stop('Resolve the save error, then reload or save manually.');
    const changed = event => {
        if (!event.target.name || event.target.name === '__RequestVerificationToken' || stopped) return;
        if (!saving) show(dirty() ? (existing ? 'Unsaved changes — saving within 30 seconds.' : 'Unsaved changes — save draft to begin.') : 'Saved.', dirty() ? 'dirty' : 'saved');
    };
    form.addEventListener('input', changed);
    form.addEventListener('change', changed);
    form.addEventListener('submit', event => {
        if (saving) {
            event.preventDefault();
            queuedSubmit = { button: event.submitter };
            show('Finishing autosave before manual save…', 'saving');
            return;
        }
        navigating = true;
    });
    window.addEventListener('beforeunload', event => {
        if (!navigating && dirty()) { event.preventDefault(); event.returnValue = ''; }
    });
    window.setInterval(async () => {
        if (!existing || stopped || saving || navigating || !dirty()) return;
        saving = true;
        const data = new FormData(form);
        const sentSnapshot = snapshot(data);
        show('Saving…', 'saving');
        const abort = new AbortController();
        const timeout = window.setTimeout(() => abort.abort(), 20000);
        let succeeded = false;
        try {
            const response = await fetch(form.dataset.autosaveUrl, {
                method: 'POST', credentials: 'same-origin', body: data,
                headers: { Accept: 'application/json' }, signal: abort.signal
            });
            if (response.redirected || !response.headers.get('content-type')?.includes('application/json')) {
                stop('Your session may have ended. Sign in again or reload before retrying.');
                return;
            }
            const result = await response.json();
            if (!response.ok) {
                stop(result.message || (response.status === 409 ? 'This job changed elsewhere. Reload before editing again.' : 'The server rejected this save. Reload or save manually.'));
                return;
            }
            if (String(result.jobId).toLowerCase() !== jobId.value.toLowerCase() || !Number.isInteger(result.version) || result.version < 0) {
                stop('Unexpected save response. Reload before retrying.');
                return;
            }
            // Update only the token, never inputs the user may have changed during this request.
            version.value = String(result.version);
            baseline = sentSnapshot;
            succeeded = true;
            show(dirty() ? 'Unsaved changes — another save is due within 30 seconds.' : 'Saved just now.', dirty() ? 'dirty' : 'saved');
        } catch {
            // The commit outcome may be unknown after a network failure. Never retry blindly.
            stop('Could not confirm the save. Check your connection and reload or save manually.');
        } finally {
            window.clearTimeout(timeout);
            saving = false;
            if (succeeded && queuedSubmit) {
                const button = queuedSubmit.button;
                queuedSubmit = null;
                if (button) form.requestSubmit(button);
                else form.requestSubmit();
            }
        }
    }, 30000);
})();
