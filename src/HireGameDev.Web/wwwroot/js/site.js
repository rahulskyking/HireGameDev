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
      if (event.clientX === 0 && event.clientY === 0) {
        return;
      }
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
      return;
    }

    var importToggleBtn = event.target.closest('[data-import-toggle-all]');
    if (importToggleBtn) {
      var group = importToggleBtn.closest('[data-import-group]');
      if (group) {
        var shouldCheck = importToggleBtn.getAttribute('data-import-toggle-all') === 'true';
        var checks = group.querySelectorAll('[data-import-item-check]');
        for (var idx = 0; idx < checks.length; idx++) {
          checks[idx].checked = shouldCheck;
        }
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
    if (event.target.matches('[data-nda-toggle]')) {
      var titleCard = event.target.closest('[data-repeat-card]');
      if (titleCard) {
        var hideStudioBox = titleCard.querySelector('[data-nda-hide-studio]');
        if (hideStudioBox) {
          if (event.target.checked) {
            hideStudioBox.disabled = false;
          } else {
            hideStudioBox.checked = false;
            hideStudioBox.disabled = true;
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

  function resetAvatarUploaderState(container, clearFileInput) {
    if (!container) {
      return;
    }

    var fileInput = container.querySelector('[data-avatar-input]');
    var errorEl = container.querySelector('[data-avatar-error]');
    var previewImg = container.querySelector('[data-avatar-preview]');
    var fallbackEl = container.querySelector('[data-avatar-fallback]');
    var filenameEl = container.querySelector('[data-avatar-filename]');
    var saveBtn = container.querySelector('[data-avatar-save-btn]');
    var clearBtn = container.querySelector('[data-avatar-clear]');

    if (clearFileInput && fileInput) {
      fileInput.value = '';
    }

    if (errorEl) {
      errorEl.hidden = true;
      errorEl.textContent = '';
    }

    if (filenameEl) {
      filenameEl.hidden = true;
      filenameEl.textContent = '';
    }

    if (saveBtn) {
      saveBtn.disabled = true;
    }

    if (clearBtn) {
      clearBtn.hidden = true;
    }

    if (previewImg) {
      var prevUrl = previewImg.getAttribute('data-object-url');
      if (prevUrl && typeof URL !== 'undefined' && URL.revokeObjectURL) {
        URL.revokeObjectURL(prevUrl);
        previewImg.removeAttribute('data-object-url');
      }

      var originalSrc = previewImg.getAttribute('data-original-src');
      if (originalSrc) {
        previewImg.src = originalSrc;
        previewImg.hidden = false;
        if (fallbackEl) {
          fallbackEl.hidden = true;
        }
      } else {
        previewImg.removeAttribute('src');
        previewImg.hidden = true;
        if (fallbackEl) {
          fallbackEl.hidden = false;
        }
      }
    }

    var headerSyncId = container.getAttribute('data-avatar-header-sync');
    if (headerSyncId && clearFileInput) {
      var headerHost = document.getElementById(headerSyncId);
      if (headerHost) {
        var headerImg = headerHost.querySelector('[data-avatar-header-img]');
        var headerFallback = headerHost.querySelector('[data-avatar-header-fallback]');
        if (headerImg) {
          headerImg.removeAttribute('src');
          headerImg.hidden = true;
        }
        if (headerFallback) {
          headerFallback.hidden = false;
        }
      }
    }
  }

  document.addEventListener('click', function (event) {
    var clearBtn = event.target.closest('[data-avatar-clear]');
    if (clearBtn) {
      var container = clearBtn.closest('[data-avatar-uploader]');
      resetAvatarUploaderState(container, true);
    }
  });

  document.addEventListener('change', function (event) {
    var fileInput = event.target.closest('[data-avatar-input]');
    if (!fileInput) {
      return;
    }

    var container = fileInput.closest('[data-avatar-uploader]');
    if (!container) {
      return;
    }

    var errorEl = container.querySelector('[data-avatar-error]');
    var previewImg = container.querySelector('[data-avatar-preview]');
    var fallbackEl = container.querySelector('[data-avatar-fallback]');
    var filenameEl = container.querySelector('[data-avatar-filename]');
    var saveBtn = container.querySelector('[data-avatar-save-btn]');
    var clearBtn = container.querySelector('[data-avatar-clear]');

    if (errorEl) {
      errorEl.hidden = true;
      errorEl.textContent = '';
    }

    var file = fileInput.files && fileInput.files[0];
    if (!file) {
      resetAvatarUploaderState(container, false);
      return;
    }

    var maxBytes = parseInt(fileInput.getAttribute('data-max-bytes') || '1048576', 10);
    var nameLower = (file.name || '').toLowerCase();
    var validExt =
      nameLower.endsWith('.jpg') ||
      nameLower.endsWith('.jpeg') ||
      nameLower.endsWith('.png') ||
      nameLower.endsWith('.webp');

    if (!validExt) {
      resetAvatarUploaderState(container, true);
      if (errorEl) {
        errorEl.textContent = 'Only .jpg, .jpeg, .png, and .webp images are allowed (SVG and GIF are not supported).';
        errorEl.hidden = false;
      }
      return;
    }

    if (file.size > maxBytes) {
      resetAvatarUploaderState(container, true);
      if (errorEl) {
        errorEl.textContent = 'Selected image exceeds 1 MB. Choose an image 1 MB or smaller.';
        errorEl.hidden = false;
      }
      return;
    }

    if (filenameEl) {
      var kb = Math.max(1, Math.round(file.size / 1024));
      filenameEl.textContent = file.name + ' · ' + kb + ' KB';
      filenameEl.hidden = false;
    }

    if (saveBtn) {
      saveBtn.disabled = false;
    }

    if (clearBtn) {
      clearBtn.hidden = false;
    }

    if (previewImg && typeof URL !== 'undefined' && URL.createObjectURL) {
      var prevUrl = previewImg.getAttribute('data-object-url');
      if (prevUrl) {
        URL.revokeObjectURL(prevUrl);
      }

      var nextUrl = URL.createObjectURL(file);
      previewImg.setAttribute('data-object-url', nextUrl);
      previewImg.src = nextUrl;
      previewImg.hidden = false;
      if (fallbackEl) {
        fallbackEl.hidden = true;
      }

      var headerSyncId = container.getAttribute('data-avatar-header-sync');
      if (headerSyncId) {
        var headerHost = document.getElementById(headerSyncId);
        if (headerHost) {
          var headerImg = headerHost.querySelector('[data-avatar-header-img]');
          var headerFallback = headerHost.querySelector('[data-avatar-header-fallback]');
          if (headerImg) {
            headerImg.src = nextUrl;
            headerImg.hidden = false;
          }
          if (headerFallback) {
            headerFallback.hidden = true;
          }
        }
      }
    }
  });

  var resettableAvatarDialogs = document.querySelectorAll('dialog[data-avatar-modal-reset-on-close]');
  for (var dIdx = 0; dIdx < resettableAvatarDialogs.length; dIdx++) {
    resettableAvatarDialogs[dIdx].addEventListener('close', function (e) {
      resetAvatarUploaderState(e.currentTarget, true);
    });
  }

  document.addEventListener('change', function (event) {
    if (!event.target.matches('[data-resume-pdf-input]')) {
      return;
    }

    var input = event.target;
    var form = input.closest('[data-resume-pdf-uploader]');
    if (!form) {
      return;
    }

    var filenameBadge = form.querySelector('[data-resume-pdf-filename]');
    var errorEl = form.querySelector('[data-resume-pdf-error]');
    var submitBtn = form.querySelector('[data-resume-pdf-submit]');

    if (errorEl) {
      errorEl.textContent = '';
      errorEl.hidden = true;
    }

    var file = input.files && input.files[0];
    if (!file) {
      if (filenameBadge) {
        filenameBadge.textContent = '';
        filenameBadge.hidden = true;
      }
      return;
    }

    var maxBytes = parseInt(input.getAttribute('data-max-bytes') || '2097152', 10);
    var lowerName = (file.name || '').toLowerCase();
    if (!lowerName.endsWith('.pdf')) {
      input.value = '';
      if (filenameBadge) filenameBadge.hidden = true;
      if (errorEl) {
        errorEl.textContent = 'Only .pdf files are supported for resume and LinkedIn import.';
        errorEl.hidden = false;
      }
      return;
    }

    if (file.size > maxBytes) {
      input.value = '';
      if (filenameBadge) filenameBadge.hidden = true;
      if (errorEl) {
        errorEl.textContent = 'Selected PDF exceeds 2 MB. Export a smaller PDF and try again.';
        errorEl.hidden = false;
      }
      return;
    }

    if (filenameBadge) {
      var kb = Math.max(1, Math.round(file.size / 1024));
      filenameBadge.textContent = file.name + ' · ' + kb + ' KB';
      filenameBadge.hidden = false;
    }

    if (submitBtn) {
      submitBtn.disabled = false;
    }
  });

  function slugifyStudioName(raw) {
    if (!raw) return '';
    return raw
      .toLowerCase()
      .replace(/[&+]/g, ' and ')
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 80)
      .replace(/-+$/g, '');
  }

  var slugCheckTimer = null;

  function scheduleStudioSlugCheck(slugInput) {
    if (!slugInput || slugInput.readOnly) return;
    var statusBadge = document.querySelector('[data-studio-slug-status]');
    var previewEl = document.querySelector('[data-studio-slug-preview]');
    var val = (slugInput.value || '').trim().toLowerCase();

    if (previewEl) {
      previewEl.textContent = val || 'your-studio';
    }

    if (!statusBadge) return;
    if (slugCheckTimer) {
      clearTimeout(slugCheckTimer);
    }

    if (val.length < 2) {
      statusBadge.hidden = true;
      statusBadge.textContent = '';
      return;
    }

    var excludeId = slugInput.getAttribute('data-exclude-studio-id') || '';
    slugCheckTimer = setTimeout(function () {
      var url = '/Studio/Slug/Check?slug=' + encodeURIComponent(val);
      if (excludeId) {
        url += '&excludeStudioId=' + encodeURIComponent(excludeId);
      }

      fetch(url, {
        method: 'GET',
        headers: { 'Accept': 'application/json' },
        credentials: 'same-origin'
      })
        .then(function (resp) {
          return resp.ok ? resp.json() : null;
        })
        .then(function (data) {
          if (!data || !statusBadge) return;
          statusBadge.hidden = false;
          statusBadge.textContent = data.isAvailable ? 'Available ✓' : (data.reason || 'Unavailable');
          statusBadge.classList.toggle('c-badge--success', !!data.isAvailable);
          statusBadge.classList.toggle('c-badge--danger', !data.isAvailable);
          statusBadge.classList.remove('c-badge--neutral');
        })
        .catch(function () {
          // Non-blocking progressive enhancement.
        });
    }, 280);
  }

  document.addEventListener('input', function (event) {
    if (event.target.matches('[data-studio-name-input]')) {
      var slugField = document.querySelector('[data-studio-slug-input]');
      if (slugField && !slugField.readOnly && slugField.getAttribute('data-slug-manual') !== 'true') {
        slugField.value = slugifyStudioName(event.target.value);
        scheduleStudioSlugCheck(slugField);
      }
      return;
    }

    if (event.target.matches('[data-studio-slug-input]')) {
      var sField = event.target;
      if (!sField.readOnly) {
        sField.setAttribute('data-slug-manual', sField.value.trim().length > 0 ? 'true' : 'false');
        scheduleStudioSlugCheck(sField);
      }
      return;
    }

    if (event.target.matches('[data-studio-desc-input]')) {
      var descCounter = document.querySelector('[data-studio-desc-counter]');
      if (descCounter) {
        var len = (event.target.value || '').trim().length;
        descCounter.textContent = len + ' / 3000 chars (min 100)';
        descCounter.classList.toggle('c-badge--success', len >= 100 && len <= 3000);
        descCounter.classList.toggle('c-badge--neutral', len < 100);
      }
    }
  });

  document.addEventListener('change', function (event) {
    if (!event.target.matches('[data-culture-tag-check]')) {
      return;
    }

    var picker = event.target.closest('[data-culture-picker]');
    if (!picker) return;

    var boxes = picker.querySelectorAll('[data-culture-tag-check]');
    var checkedCount = 0;
    for (var i = 0; i < boxes.length; i++) {
      if (boxes[i].checked) checkedCount++;
    }

    for (var j = 0; j < boxes.length; j++) {
      boxes[j].disabled = !boxes[j].checked && checkedCount >= 6;
    }

    var badge = picker.querySelector('[data-culture-count-badge]');
    if (badge) {
      badge.textContent = checkedCount + ' / 6 selected';
    }
  });

  // Studio Verification: dynamic https:// proof link rows + live char counters (M1.10b)
  function reindexProofLinkGroup(group) {
    if (!group) return;
    var prefix = group.getAttribute('data-input-prefix') || 'SubmitForm.ProofUrls';
    var maxRows = parseInt(group.getAttribute('data-max-rows') || '5', 10);
    var rows = group.querySelectorAll('[data-proof-link-row]');
    for (var i = 0; i < rows.length; i++) {
      var input = rows[i].querySelector('input[type="url"]');
      if (input) {
        input.name = prefix + '[' + i + ']';
      }
    }

    var addBtn = group.querySelector('[data-add-proof-link]');
    if (addBtn) {
      addBtn.disabled = rows.length >= maxRows;
    }
  }

  document.addEventListener('click', function (event) {
    var addBtn = event.target.closest('[data-add-proof-link]');
    if (addBtn) {
      var group = addBtn.closest('[data-proof-links-group]');
      if (!group) return;
      var maxRows = parseInt(group.getAttribute('data-max-rows') || '5', 10);
      var list = group.querySelector('[data-proof-links-list]');
      if (!list) return;

      var existingRows = list.querySelectorAll('[data-proof-link-row]');
      if (existingRows.length >= maxRows) return;

      var row = document.createElement('div');
      row.className = 'p-sg-row';
      row.style.gap = '8px';
      row.setAttribute('data-proof-link-row', '');

      var input = document.createElement('input');
      input.type = 'url';
      input.className = 'c-input';
      input.maxLength = 500;
      input.placeholder = 'https://www.linkedin.com/company/yourstudio';

      var removeBtn = document.createElement('button');
      removeBtn.type = 'button';
      removeBtn.className = 'c-btn c-btn--ghost c-btn--sm';
      removeBtn.setAttribute('data-remove-proof-link', '');
      removeBtn.setAttribute('aria-label', 'Remove proof link');
      removeBtn.textContent = 'Remove';

      row.appendChild(input);
      row.appendChild(removeBtn);
      list.appendChild(row);
      reindexProofLinkGroup(group);
      input.focus();
      return;
    }

    var removeTrigger = event.target.closest('[data-remove-proof-link]');
    if (removeTrigger) {
      var parentGroup = removeTrigger.closest('[data-proof-links-group]');
      var targetRow = removeTrigger.closest('[data-proof-link-row]');
      if (!parentGroup || !targetRow) return;

      var allRows = parentGroup.querySelectorAll('[data-proof-link-row]');
      if (allRows.length <= 1) {
        var singleInput = targetRow.querySelector('input[type="url"]');
        if (singleInput) singleInput.value = '';
        return;
      }

      targetRow.remove();
      reindexProofLinkGroup(parentGroup);
    }
  });

  document.addEventListener('input', function (event) {
    var field = event.target;
    if (!field || !field.id) return;

    var counter = document.querySelector('[data-char-counter-for="' + field.id + '"]');
    if (!counter) return;

    var maxChars = parseInt(counter.getAttribute('data-max-chars') || '1000', 10);
    var count = (field.value || '').trim().length;
    counter.textContent = count + ' / ' + maxChars + ' chars';
  });

  // Candidate Privacy Settings: Current CTC visibility sync + live Verified Studio blocklist search (M1.11b)
  function syncCurrentCtcGroup(group) {
    if (!group) return;
    var input = group.querySelector('[data-current-ctc-input]');
    var checkbox = group.querySelector('[data-show-current-ctc-check]');
    var badge = group.querySelector('[data-current-ctc-status-badge]');
    if (!input || !checkbox) return;

    var raw = (input.value || '').trim();
    var numeric = parseFloat(raw);
    var hasValidValue = raw.length > 0 && !isNaN(numeric) && numeric >= 0;

    if (!hasValidValue) {
      checkbox.checked = false;
      checkbox.disabled = true;
    } else {
      checkbox.disabled = false;
    }

    if (badge) {
      var visible = hasValidValue && checkbox.checked;
      badge.textContent = visible ? 'Visible to verified studios' : 'Current CTC: hidden';
      badge.classList.toggle('c-badge--info', visible);
      badge.classList.toggle('c-badge--neutral', !visible);
    }
  }

  var initialCtcGroup = document.querySelector('[data-current-ctc-group]');
  if (initialCtcGroup) {
    syncCurrentCtcGroup(initialCtcGroup);
  }

  document.addEventListener('input', function (event) {
    if (event.target.matches('[data-current-ctc-input]')) {
      syncCurrentCtcGroup(event.target.closest('[data-current-ctc-group]'));
    }
  });

  document.addEventListener('change', function (event) {
    if (event.target.matches('[data-show-current-ctc-check]')) {
      syncCurrentCtcGroup(event.target.closest('[data-current-ctc-group]'));
    }
  });

  function buildStudioMonogram(name) {
    var trimmed = (name || '').trim();
    if (!trimmed) return 'ST';
    var parts = trimmed.split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
    }
    return parts[0].slice(0, 2).toUpperCase();
  }

  function renderStudioBlockSearchResults(picker, query, items) {
    var container = picker.querySelector('[data-studio-block-live-results]');
    if (!container) return;
    container.textContent = '';

    if (!query || query.length < 2) {
      return;
    }

    if (!items || items.length === 0) {
      var empty = document.createElement('p');
      empty.className = 'c-hint';
      empty.style.margin = '0';
      empty.textContent = 'No verified studios matched "' + query + '".';
      container.appendChild(empty);
      return;
    }

    var blockLimitReached = picker.getAttribute('data-block-limit-reached') === 'true';
    var formTemplate = picker.querySelector('[data-studio-block-post-template]');
    var list = document.createElement('div');
    list.className = 'c-studio-block-list';

    for (var i = 0; i < items.length; i++) {
      var item = items[i];
      var row = document.createElement('div');
      row.className = 'c-studio-block-row';

      var head = document.createElement('div');
      head.className = 'c-entity-head';

      if (item.logoUrl) {
        var img = document.createElement('img');
        img.src = item.logoUrl;
        img.alt = item.name + ' logo';
        img.className = 'c-avatar c-avatar--sm';
        head.appendChild(img);
      } else {
        var mark = document.createElement('span');
        mark.className = 'c-entity-mark c-entity-mark--accent c-avatar--sm';
        mark.setAttribute('aria-hidden', 'true');
        mark.textContent = buildStudioMonogram(item.name);
        head.appendChild(mark);
      }

      var info = document.createElement('div');
      var topLine = document.createElement('div');
      topLine.className = 'p-sg-row';
      topLine.style.gap = '6px';

      var link = document.createElement('a');
      link.href = item.publicUrl || ('/studios/' + encodeURIComponent(item.slug || ''));
      link.target = '_blank';
      link.rel = 'noopener noreferrer';
      var strong = document.createElement('strong');
      strong.textContent = item.name || 'Studio';
      link.appendChild(strong);
      topLine.appendChild(link);

      if (item.studioTypeLabel) {
        var typeBadge = document.createElement('span');
        typeBadge.className = 'c-badge c-badge--neutral';
        typeBadge.textContent = item.studioTypeLabel;
        topLine.appendChild(typeBadge);
      }

      info.appendChild(topLine);

      if (item.location) {
        var loc = document.createElement('div');
        loc.className = 'c-hint';
        loc.style.margin = '2px 0 0 0';
        loc.textContent = item.location;
        info.appendChild(loc);
      }

      head.appendChild(info);
      row.appendChild(head);

      if (item.isAlreadyBlocked) {
        var alreadyBadge = document.createElement('span');
        alreadyBadge.className = 'c-badge c-badge--warning';
        alreadyBadge.textContent = 'Already hidden';
        row.appendChild(alreadyBadge);
      } else if (blockLimitReached) {
        var capBadge = document.createElement('span');
        capBadge.className = 'c-badge c-badge--neutral';
        capBadge.textContent = 'Limit reached (50)';
        row.appendChild(capBadge);
      } else if (formTemplate) {
        var postForm = formTemplate.cloneNode(true);
        postForm.hidden = false;
        postForm.removeAttribute('data-studio-block-post-template');
        postForm.style.margin = '0';

        var studioIdInput = postForm.querySelector('input[name="studioId"]');
        if (studioIdInput) {
          studioIdInput.value = item.studioId || '';
        }

        var submitBtn = document.createElement('button');
        submitBtn.type = 'submit';
        submitBtn.className = 'c-btn c-btn--secondary c-btn--sm';
        submitBtn.textContent = 'Hide Profile';
        postForm.appendChild(submitBtn);
        row.appendChild(postForm);
      }

      list.appendChild(row);
    }

    container.appendChild(list);
  }

  var studioBlockDebounceTimer = null;
  var studioBlockRequestSeq = 0;

  document.addEventListener('input', function (event) {
    if (!event.target.matches('[data-studio-block-search-input]')) {
      return;
    }

    var input = event.target;
    var picker = input.closest('[data-studio-block-picker]');
    if (!picker) return;

    var searchUrl = picker.getAttribute('data-search-url');
    if (!searchUrl) return;

    var query = (input.value || '').trim();
    if (studioBlockDebounceTimer) {
      clearTimeout(studioBlockDebounceTimer);
    }

    if (query.length < 2) {
      var resultsBox = picker.querySelector('[data-studio-block-live-results]');
      if (resultsBox) resultsBox.textContent = '';
      return;
    }

    var currentSeq = ++studioBlockRequestSeq;
    studioBlockDebounceTimer = setTimeout(function () {
      var sep = searchUrl.indexOf('?') >= 0 ? '&' : '?';
      fetch(searchUrl + sep + 'q=' + encodeURIComponent(query), {
        method: 'GET',
        headers: {
          'Accept': 'application/json',
          'X-Requested-With': 'XMLHttpRequest'
        },
        credentials: 'same-origin'
      })
        .then(function (resp) {
          if (!resp.ok) throw new Error('Search failed');
          return resp.json();
        })
        .then(function (payload) {
          if (currentSeq !== studioBlockRequestSeq) return;
          renderStudioBlockSearchResults(picker, query, payload && payload.items ? payload.items : []);
        })
        .catch(function () {
          // Keep server-side GET Search Studios button available if network fetch fails.
        });
    }, 240);
  });
})();
