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
        // Fallback silently; theme is already applied in DOM for the current page.
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
    }
  });

  document.addEventListener('keydown', function (event) {
    if (event.key !== 'Escape') {
      return;
    }

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
  });
})();
