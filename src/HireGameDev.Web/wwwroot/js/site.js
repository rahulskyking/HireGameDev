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
  });

  document.addEventListener('input', function (event) {
    var comboInput = event.target.closest('[data-combobox-input]');
    if (!comboInput) {
      return;
    }

    var combo = comboInput.closest('[data-combobox]');
    if (!combo) {
      return;
    }

    var query = comboInput.value.trim().toLowerCase();
    var options = combo.querySelectorAll('[data-combobox-option]');
    var visibleCount = 0;

    for (var i = 0; i < options.length; i++) {
      var label = (options[i].getAttribute('data-label') || options[i].textContent || '').toLowerCase();
      var match = query.length === 0 || label.indexOf(query) !== -1;
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
})();
