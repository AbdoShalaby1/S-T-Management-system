(function () {
  'use strict';

  var roots = document.querySelectorAll('[data-sync-root]');
  if (!roots.length) return;

  var flashKey = 'osc.syncFlash';

  // The page reloads as soon as a sync succeeds, so the result is shown after the reload.
  function takeFlash() {
    try {
      var raw = window.sessionStorage.getItem(flashKey);
      if (!raw) return null;

      window.sessionStorage.removeItem(flashKey);
      return JSON.parse(raw);
    } catch (error) {
      return null;
    }
  }

  function saveFlash(payload) {
    try {
      window.sessionStorage.setItem(
        flashKey,
        JSON.stringify({
          type: payload.hasFailure ? 'warning' : 'success',
          message: payload.message || 'Sync finished.'
        })
      );
    } catch (error) {
      // A blocked storage API must never stop the reload.
    }
  }

  var flash = takeFlash();
  if (flash && window.Toast) {
    window.Toast.show({ type: flash.type || 'success', message: flash.message });
  }

  roots.forEach(function (root) {
    var status = root.querySelector('[data-sync-status]');
    var button = root.querySelector('[data-sync-button]');
    var label = root.querySelector('[data-sync-label]');
    var icon = root.querySelector('[data-sync-icon]');
    var form = root.querySelector('[data-sync-form]');
    var url = root.getAttribute('data-sync-url');

    var busy = root.getAttribute('data-sync-in-progress') === 'true';

    function setStatus(text) {
      if (status) status.textContent = text;
    }

    function resetButton() {
      busy = false;
      if (icon) icon.classList.remove('animate-spin');
      if (label) label.textContent = 'Sync solves';
    }

    if (busy) {
      setStatus('Sync already running...');
      if (button) button.disabled = true;
    }

    if (!form || !button) return;

    form.addEventListener('submit', function (event) {
      event.preventDefault();
      if (busy) return;

      busy = true;
      button.disabled = true;
      if (icon) icon.classList.add('animate-spin');
      if (label) label.textContent = 'Syncing...';
      setStatus('');

      fetch(url, {
        method: 'POST',
        headers: { 'X-Sync-Client': 'js', Accept: 'application/json' },
        body: new FormData(form),
        credentials: 'same-origin'
      })
        .then(function (response) {
          return response
            .json()
            .catch(function () {
              return { ok: false, message: 'The sync returned an unexpected response.' };
            });
        })
        .then(function (payload) {
          if (payload.ok) {
            saveFlash(payload);
            window.location.reload();
            return;
          }

          var type =
            payload.errorCode === 'sync_in_progress' ? 'warning' : 'error';

          if (window.Toast) {
            window.Toast.show({ type: type, message: payload.message || 'Sync finished.' });
          }

          resetButton();

          if (payload.errorCode === 'needs_handle') {
            button.disabled = true;
            setStatus('Add a handle in Settings first.');
            return;
          }

          button.disabled = false;
        })
        .catch(function () {
          resetButton();
          button.disabled = false;
          if (window.Toast) {
            window.Toast.show({
              type: 'error',
              message: 'The sync could not be reached. Check your connection and try again.'
            });
          }
        });
    });
  });
})();