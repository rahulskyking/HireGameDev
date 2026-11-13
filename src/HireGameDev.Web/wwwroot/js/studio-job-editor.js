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
