(function () {
  'use strict';

  function applyThemeToDom(theme) {
    var root = document.documentElement;
    if (theme === 'light' || theme === 'dark') {
      root.setAttribute('data-theme', theme);
    } else {
      root.removeAttribute('data-theme');
    }

    var buttons = document.querySelectorAll('[data-theme-value]');
    for (var i = 0; i < buttons.length; i++) {
      var isSelected = buttons[i].getAttribute('data-theme-value') === theme;
      buttons[i].setAttribute('aria-pressed', isSelected ? 'true' : 'false');
      if (buttons[i].classList.contains('c-btn')) {
        buttons[i].classList.toggle('c-btn--primary', isSelected);
        buttons[i].classList.toggle('c-btn--secondary', !isSelected);
      }
    }
  }

  function syncAccountRoleSelection() {
    var radios = document.querySelectorAll('[data-account-type-radio]');
    if (radios.length === 0) {
      return;
    }

    var selectedValue = null;
    for (var i = 0; i < radios.length; i++) {
      var card = radios[i].closest('.c-role-card');
      if (card) {
        card.classList.toggle('is-selected', radios[i].checked);
      }
      if (radios[i].checked) {
        selectedValue = radios[i].value;
      }
    }

    if (!selectedValue) {
      return;
    }

    var hiddenType = document.getElementById('external-preferred-account-type');
    if (hiddenType) {
      hiddenType.value = selectedValue;
    }

    var isStudio = selectedValue === 'Studio';
    var providerButtons = document.querySelectorAll('button[form="external-register-form"][name="provider"]');
    for (var j = 0; j < providerButtons.length; j++) {
      var btn = providerButtons[j];
      var allowedForStudios = btn.getAttribute('data-allowed-for-studios') === 'true';
      var shouldDisable = isStudio && !allowedForStudios;
      btn.disabled = shouldDisable;
      if (shouldDisable) {
        btn.setAttribute('aria-disabled', 'true');
      } else {
        btn.removeAttribute('aria-disabled');
      }
    }
  }

  function syncDisciplineSelection() {
    var radios = document.querySelectorAll('[data-discipline-radio]');
    for (var i = 0; i < radios.length; i++) {
      var card = radios[i].closest('.c-role-card');
      if (card) {
        card.classList.toggle('is-selected', radios[i].checked);
      }
    }
  }

  function suggestSeniorityIndex(years) {
    if (isNaN(years) || years < 1) return 0;
    if (years < 3) return 1;
    if (years < 7) return 2;
    if (years < 12) return 3;
    return 4;
  }

  function syncSeniorityBand() {
    var yearsInput = document.querySelector('[data-years-exp-input]');
    var senioritySelect = document.querySelector('[data-seniority-select]');
    if (!yearsInput || !senioritySelect) {
      return;
    }

    var years = parseFloat(yearsInput.value);
    var suggestedIdx = suggestSeniorityIndex(years);
    var tierNames = ['fresher', 'junior', 'mid', 'senior', 'lead'];

    var hint = document.querySelector('[data-seniority-hint]');
    if (hint) {
      hint.textContent = 'Suggested: ' + tierNames[suggestedIdx];
    }

    var options = senioritySelect.querySelectorAll('option[data-level-index]');
    var selectedValid = false;
    var suggestedValue = null;

    for (var i = 0; i < options.length; i++) {
      var levelIdx = parseInt(options[i].getAttribute('data-level-index'), 10);
      var allowed = Math.abs(levelIdx - suggestedIdx) <= 1;
      options[i].disabled = !allowed;

      if (levelIdx === suggestedIdx) {
        suggestedValue = options[i].value;
      }
      if (options[i].selected && allowed) {
        selectedValid = true;
      }
    }

    if (!selectedValid && suggestedValue !== null) {
      senioritySelect.value = suggestedValue;
    }

    syncFresherBanner();
  }

  function syncFresherBanner() {
    var banner = document.querySelector('[data-fresher-banner]');
    if (!banner) return;
    var yearsInput = document.querySelector('[data-years-exp-input]');
    var senioritySelect = document.querySelector('[data-seniority-select]');
    var years = yearsInput ? parseFloat(yearsInput.value) : 0;
    var seniorityVal = senioritySelect ? senioritySelect.value : '';
    var isFresher = isNaN(years) || years < 1 || seniorityVal === 'Fresher';
    banner.hidden = !isFresher;
  }

  function syncRepeatSection(section) {
    if (!section) return;
    var prefix = section.getAttribute('data-repeat-prefix') || '';
    var maxItems = parseInt(section.getAttribute('data-repeat-max') || '20', 10);
    var container = section.querySelector('[data-repeat-items]');
    if (!container) return;

    var cards = container.querySelectorAll('[data-repeat-card]');
    for (var i = 0; i < cards.length; i++) {
      var card = cards[i];
      var idxBadge = card.querySelector('[data-card-index]');
      if (idxBadge) {
        idxBadge.textContent = '#' + (i + 1);
      }

      var fields = card.querySelectorAll('[data-field]');
      for (var f = 0; f < fields.length; f++) {
        var fieldName = fields[f].getAttribute('data-field');
        if (fieldName) {
          fields[f].setAttribute('name', prefix + '[' + i + '].' + fieldName);
        }
      }
    }

    var countBadge = section.querySelector('[data-repeat-count]');
    if (countBadge) {
      countBadge.textContent = cards.length + ' / ' + maxItems;
    }

    var emptyHint = section.querySelector('[data-repeat-empty]');
    if (emptyHint) {
      emptyHint.hidden = cards.length > 0;
    }

    var addBtn = section.querySelector('[data-repeat-add]');
    if (addBtn) {
      addBtn.disabled = cards.length >= maxItems;
    }
  }

  function filterSkillCatalog(picker) {
    if (!picker) return;
    var searchInput = picker.querySelector('[data-skill-search-input]');
    var activeCatBtn = picker.querySelector('[data-skill-cat-filter][aria-pressed="true"]');
    var activeCat = activeCatBtn ? activeCatBtn.getAttribute('data-skill-cat-filter') : 'all';
    var query = searchInput ? searchInput.value.trim().toLowerCase() : '';

    var buttons = picker.querySelectorAll('[data-skill-add-btn]');
    var visibleCount = 0;

    for (var i = 0; i < buttons.length; i++) {
      var btn = buttons[i];
      var cat = btn.getAttribute('data-skill-category') || '';
      var keywords = (btn.getAttribute('data-search-keywords') || '').toLowerCase();
      var catMatch = activeCat === 'all' || cat === activeCat;
      var textMatch = query.length === 0 || keywords.indexOf(query) !== -1;
      var show = catMatch && textMatch;
      btn.hidden = !show;
      if (show) visibleCount++;
    }

    var emptyNote = picker.querySelector('[data-skill-catalog-empty]');
    if (emptyNote) {
      emptyNote.hidden = visibleCount > 0;
    }
  }

  function syncSkillPickerState(picker) {
    if (!picker) return;
    var tbody = picker.querySelector('[data-selected-skills-body]');
    if (!tbody) return;

    var rows = tbody.querySelectorAll('[data-skill-row]');
    var selectedIds = {};
    var primaryCount = 0;

    for (var i = 0; i < rows.length; i++) {
      var row = rows[i];
      var sid = row.getAttribute('data-skill-id') || '';
      if (sid) selectedIds[sid] = true;

      var fields = row.querySelectorAll('[data-field]');
      for (var f = 0; f < fields.length; f++) {
        var fieldName = fields[f].getAttribute('data-field');
        fields[f].name = 'Step3.Skills[' + i + '].' + fieldName;
      }

      var primaryBox = row.querySelector('[data-skill-primary]');
      if (primaryBox && primaryBox.checked) {
        primaryCount++;
      }
    }

    var allPrimaryBoxes = tbody.querySelectorAll('[data-skill-primary]');
    for (var p = 0; p < allPrimaryBoxes.length; p++) {
      allPrimaryBoxes[p].disabled = !allPrimaryBoxes[p].checked && primaryCount >= 5;
    }

    var emptyState = picker.querySelector('[data-selected-skills-empty]');
    var tableWrap = picker.querySelector('[data-selected-skills-wrap]');
    if (emptyState) emptyState.hidden = rows.length > 0;
    if (tableWrap) tableWrap.hidden = rows.length === 0;

    var countBadge = picker.querySelector('[data-skill-count-badge]');
    if (countBadge) {
      countBadge.textContent = rows.length + ' / 25 selected (min 3)';
      countBadge.classList.toggle('c-badge--success', rows.length >= 3);
      countBadge.classList.toggle('c-badge--neutral', rows.length < 3);
    }

    var primaryBadge = picker.querySelector('[data-primary-count-badge]');
    if (primaryBadge) {
      primaryBadge.textContent = primaryCount + ' / 5 primary';
    }

    var compBadge = picker.querySelector('[data-skill-completeness-badge]');
    if (compBadge) {
      var reachedFive = rows.length >= 5;
      compBadge.textContent = reachedFive
        ? '+15% completeness unlocked \u2713'
        : 'Add ' + Math.max(0, 5 - rows.length) + ' more for +15% completeness';
      compBadge.classList.toggle('c-badge--success', reachedFive);
      compBadge.classList.toggle('c-badge--info', !reachedFive);
    }

    var catalogBtns = picker.querySelectorAll('[data-skill-add-btn]');
    for (var b = 0; b < catalogBtns.length; b++) {
      var btn = catalogBtns[b];
      var btnSkillId = btn.getAttribute('data-skill-id') || '';
      var skillName = btn.getAttribute('data-skill-name') || '';
      var isAdded = !!selectedIds[btnSkillId];
      btn.disabled = isAdded || rows.length >= 25;
      var labelSpan = btn.querySelector('[data-skill-btn-label]');
      if (labelSpan) {
        labelSpan.textContent = isAdded ? skillName + ' \u2713' : '+ ' + skillName;
      }
    }
  }

  function activateTab(tab) {
    var container = tab.closest('[data-tabs]');
    if (!container) {
      return;
    }

    var tabs = container.querySelectorAll('[role="tab"]');
    var targetPanelId = tab.getAttribute('aria-controls');

    for (var i = 0; i < tabs.length; i++) {
      var current = tabs[i];
      var isActive = current === tab;
      current.setAttribute('aria-selected', isActive ? 'true' : 'false');
      current.setAttribute('tabindex', isActive ? '0' : '-1');

      var panelId = current.getAttribute('aria-controls');
      var panel = panelId ? document.getElementById(panelId) : null;
      if (panel) {
        panel.hidden = panelId !== targetPanelId;
      }
    }
  }

  document.addEventListener('click', function (event) {
    var themeBtn = event.target.closest('[data-theme-value]');
    if (themeBtn) {
      var form = themeBtn.closest('form[data-theme-form]');
      if (!form) {
        return;
      }

      event.preventDefault();
      var chosenTheme = themeBtn.getAttribute('data-theme-value') || 'system';
      applyThemeToDom(chosenTheme);

      var formData = new FormData(form);
      formData.set('theme', chosenTheme);

      fetch(form.action, {
        method: 'POST',
        body: formData,
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
        credentials: 'same-origin'
      }).catch(function () {
        // Theme is already applied in DOM for the current page.
      });
      return;
    }

    var cookieBtn = event.target.closest('[data-cookie-choice]');
    if (cookieBtn) {
      var cookieForm = cookieBtn.closest('form[data-cookie-form]');
      var banner = document.getElementById('hgd-cookie-banner');
      if (!cookieForm || !banner) {
        return;
      }

      event.preventDefault();
      var choice = cookieBtn.getAttribute('data-cookie-choice') || 'essential';
      banner.hidden = true;

      var cookieData = new FormData(cookieForm);
      cookieData.set('choice', choice);

      fetch(cookieForm.action, {
        method: 'POST',
        body: cookieData,
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
        credentials: 'same-origin'
      }).catch(function () {
        banner.hidden = false;
      });
      return;
    }

    var menuToggle = event.target.closest('[data-mobile-menu-toggle]');
    if (menuToggle) {
      var drawerId = menuToggle.getAttribute('aria-controls');
      var drawer = drawerId ? document.getElementById(drawerId) : null;
      if (!drawer) {
        return;
      }

      var isExpanded = menuToggle.getAttribute('aria-expanded') === 'true';
      menuToggle.setAttribute('aria-expanded', isExpanded ? 'false' : 'true');
      drawer.classList.toggle('is-open', !isExpanded);
      return;
    }

    var tab = event.target.closest('[data-tabs] [role="tab"]');
    if (tab) {
      activateTab(tab);
      return;
    }

    var toggleChip = event.target.closest('[data-toggle-chip]');
    if (toggleChip && !toggleChip.disabled) {
      var pressed = toggleChip.getAttribute('aria-pressed') === 'true';
      toggleChip.setAttribute('aria-pressed', pressed ? 'false' : 'true');
      return;
    }

    var catFilterBtn = event.target.closest('[data-skill-cat-filter]');
    if (catFilterBtn) {
      var pickerForCat = catFilterBtn.closest('[data-skill-picker]');
      if (pickerForCat) {
        var allCatBtns = pickerForCat.querySelectorAll('[data-skill-cat-filter]');
        for (var c = 0; c < allCatBtns.length; c++) {
          allCatBtns[c].setAttribute('aria-pressed', allCatBtns[c] === catFilterBtn ? 'true' : 'false');
        }
        filterSkillCatalog(pickerForCat);
      }
      return;
    }

    var addSkillBtn = event.target.closest('[data-skill-add-btn]');
    if (addSkillBtn && !addSkillBtn.disabled) {
      var pickerForAdd = addSkillBtn.closest('[data-skill-picker]');
      var tbody = pickerForAdd ? pickerForAdd.querySelector('[data-selected-skills-body]') : null;
      var tpl = document.getElementById('hgd-skill-row-template');
      if (pickerForAdd && tbody && tpl && 'content' in tpl) {
        var existingRows = tbody.querySelectorAll('[data-skill-row]');
        if (existingRows.length >= 25) {
          return;
        }
        var skillId = addSkillBtn.getAttribute('data-skill-id') || '';
        if (tbody.querySelector('[data-skill-row][data-skill-id="' + skillId + '"]')) {
          return;
        }

        var skillName = addSkillBtn.getAttribute('data-skill-name') || '';
        var skillCatLabel = addSkillBtn.getAttribute('data-skill-category-label') || '';
        var clone = tpl.content.cloneNode(true);
        var tr = clone.querySelector('[data-skill-row]');
        tr.setAttribute('data-skill-id', skillId);

        var hiddenId = tr.querySelector('[data-field="SkillId"]');
        if (hiddenId) hiddenId.value = skillId;

        var nameEl = tr.querySelector('[data-tpl-skill-name]');
        if (nameEl) nameEl.textContent = skillName;

        var catEl = tr.querySelector('[data-tpl-skill-cat]');
        if (catEl) catEl.textContent = skillCatLabel;

        var profSelect = tr.querySelector('[data-field="Proficiency"]');
        if (profSelect) profSelect.setAttribute('aria-label', 'Proficiency for ' + skillName);

        var yearsField = tr.querySelector('[data-field="YearsUsed"]');
        if (yearsField) yearsField.setAttribute('aria-label', 'Years used for ' + skillName);

        var yearUsedField = tr.querySelector('[data-field="LastUsedYear"]');
        if (yearUsedField) yearUsedField.setAttribute('aria-label', 'Last used year for ' + skillName);

        var rmBtn = tr.querySelector('[data-skill-remove-btn]');
        if (rmBtn) rmBtn.setAttribute('aria-label', 'Remove ' + skillName);

        tbody.appendChild(clone);
        syncSkillPickerState(pickerForAdd);
      }
      return;
    }

    var removeSkillBtn = event.target.closest('[data-skill-remove-btn]');
    if (removeSkillBtn) {
      var pickerForRm = removeSkillBtn.closest('[data-skill-picker]');
      var rowToRemove = removeSkillBtn.closest('[data-skill-row]');
      if (rowToRemove && rowToRemove.parentNode) {
        rowToRemove.parentNode.removeChild(rowToRemove);
        syncSkillPickerState(pickerForRm);
      }
      return;
    }

    var repeatAddBtn = event.target.closest('[data-repeat-add]');
    if (repeatAddBtn && !repeatAddBtn.disabled) {
      var repeatSection = repeatAddBtn.closest('[data-repeat-section]');
      if (repeatSection) {
        var tplId = repeatSection.getAttribute('data-repeat-template') || '';
        var maxCount = parseInt(repeatSection.getAttribute('data-repeat-max') || '20', 10);
        var itemsHost = repeatSection.querySelector('[data-repeat-items]');
        var repeatTpl = tplId ? document.getElementById(tplId) : null;
        if (itemsHost && repeatTpl && 'content' in repeatTpl) {
          var existingCards = itemsHost.querySelectorAll('[data-repeat-card]');
          if (existingCards.length < maxCount) {
            var cardClone = repeatTpl.content.cloneNode(true);
            itemsHost.appendChild(cardClone);
            syncRepeatSection(repeatSection);
            var updatedCards = itemsHost.querySelectorAll('[data-repeat-card]');
            var newest = updatedCards[updatedCards.length - 1];
            if (newest) {
              var firstInput = newest.querySelector('input:not([type="hidden"]), textarea, select');
              if (firstInput && typeof firstInput.focus === 'function') {
                firstInput.focus();
              }
            }
          }
        }
      }
      return;
    }

    var repeatRemoveBtn = event.target.closest('[data-repeat-remove]');
    if (repeatRemoveBtn) {
      var sectionForRm = repeatRemoveBtn.closest('[data-repeat-section]');
      var cardToRemove = repeatRemoveBtn.closest('[data-repeat-card]');
      if (cardToRemove && cardToRemove.parentNode) {
        cardToRemove.parentNode.removeChild(cardToRemove);
        syncRepeatSection(sectionForRm);
      }
      return;
    }

    var modalOpenBtn = event.target.closest('[data-modal-open]');
    if (modalOpenBtn) {
      var modalId = modalOpenBtn.getAttribute('data-modal-open');
      var dialog = modalId ? document.getElementById(modalId) : null;
      if (dialog && typeof dialog.showModal === 'function') {
        dialog.showModal();
      }
      return;
    }

    var modalCloseBtn = event.target.closest('[data-modal-close]');
    if (modalCloseBtn) {
      var parentDialog = modalCloseBtn.closest('dialog');
      if (parentDialog && typeof parentDialog.close === 'function') {
        parentDialog.close();
      }
      return;
    }

    if (event.target.tagName === 'DIALOG' && event.target.classList.contains('c-modal')) {
      var rect = event.target.getBoundingClientRect();
      var inDialog =
        rect.top <= event.clientY &&
        event.clientY <= rect.bottom &&
        rect.left <= event.clientX &&
        event.clientX <= rect.right;
      if (!inDialog && typeof event.target.close === 'function') {
        event.target.close();
      }
      return;
    }

    var comboOption = event.target.closest('[data-combobox-option]');
    if (comboOption) {
      var combo = comboOption.closest('[data-combobox]');
      if (!combo) {
        return;
      }
      var input = combo.querySelector('[data-combobox-input]');
      var hidden = combo.querySelector('[data-combobox-value]');
      var options = combo.querySelectorAll('[data-combobox-option]');

      for (var k = 0; k < options.length; k++) {
        options[k].setAttribute('aria-selected', options[k] === comboOption ? 'true' : 'false');
      }

      if (input) {
        input.value = comboOption.getAttribute('data-label') || comboOption.textContent.trim();
      }
      if (hidden) {
        hidden.value = comboOption.getAttribute('data-value') || '';
      }
    }
  });

  document.addEventListener('change', function (event) {
    if (event.target.matches('[data-account-type-radio]')) {
      syncAccountRoleSelection();
    }
    if (event.target.matches('[data-discipline-radio]')) {
      syncDisciplineSelection();
    }
    if (event.target.matches('[data-years-exp-input]')) {
      syncSeniorityBand();
    }
    if (event.target.matches('[data-seniority-select]')) {
      syncFresherBanner();
    }
    if (event.target.matches('[data-skill-primary]')) {
      syncSkillPickerState(event.target.closest('[data-skill-picker]'));
    }
    if (event.target.matches('[data-exp-current]')) {
      var expCard = event.target.closest('[data-repeat-card]');
      if (expCard) {
        var endDateInput = expCard.querySelector('[data-field="EndDate"]');
        if (endDateInput) {
          if (event.target.checked) {
            endDateInput.value = '';
            endDateInput.disabled = true;
          } else {
            endDateInput.disabled = false;
          }
        }
      }
    }
  });

  document.addEventListener('input', function (event) {
    if (event.target.matches('[data-years-exp-input]')) {
      syncSeniorityBand();
      return;
    }

    if (event.target.matches('[data-skill-search-input]')) {
      filterSkillCatalog(event.target.closest('[data-skill-picker]'));
      return;
    }

    var comboInput = event.target.closest('[data-combobox-input]');
    if (!comboInput) {
      return;
    }

    var combo = comboInput.closest('[data-combobox]');
    if (!combo) {
      return;
    }

    var query = comboInput.value.trim().toLowerCase();
    if (query.length === 0) {
      var hiddenVal = combo.querySelector('[data-combobox-value]');
      if (hiddenVal) {
        hiddenVal.value = '0';
      }
    }

    var options = combo.querySelectorAll('[data-combobox-option]');
    var visibleCount = 0;

    for (var i = 0; i < options.length; i++) {
      var label = (options[i].getAttribute('data-label') || options[i].textContent || '').toLowerCase();
      var keywords = (options[i].getAttribute('data-search-keywords') || '').toLowerCase();
      var match = query.length === 0 || label.indexOf(query) !== -1 || keywords.indexOf(query) !== -1;
      options[i].hidden = !match;
      if (match) {
        visibleCount++;
      }
    }

    var empty = combo.querySelector('[data-combobox-empty]');
    if (empty) {
      empty.hidden = visibleCount > 0;
    }
  });

  document.addEventListener('keydown', function (event) {
    if (event.key === 'Escape') {
      var toggle = document.querySelector('[data-mobile-menu-toggle][aria-expanded="true"]');
      if (toggle) {
        toggle.setAttribute('aria-expanded', 'false');
        var drawerId = toggle.getAttribute('aria-controls');
        var drawer = drawerId ? document.getElementById(drawerId) : null;
        if (drawer) {
          drawer.classList.remove('is-open');
        }
        toggle.focus();
      }
      return;
    }

    var activeTab = event.target.closest('[data-tabs] [role="tab"]');
    if (activeTab && (event.key === 'ArrowRight' || event.key === 'ArrowLeft')) {
      var tabList = Array.prototype.slice.call(
        activeTab.closest('[data-tabs]').querySelectorAll('[role="tab"]')
      );
      var idx = tabList.indexOf(activeTab);
      if (idx === -1 || tabList.length === 0) {
        return;
      }

      event.preventDefault();
      var nextIdx =
        event.key === 'ArrowRight'
          ? (idx + 1) % tabList.length
          : (idx - 1 + tabList.length) % tabList.length;
      tabList[nextIdx].focus();
      activateTab(tabList[nextIdx]);
    }
  });

  syncAccountRoleSelection();
  syncDisciplineSelection();
})();
