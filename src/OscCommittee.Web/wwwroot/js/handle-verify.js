(function () {
  'use strict';

  var buttons = document.querySelectorAll('[data-handle-verify]');
  if (!buttons.length) return;

  buttons.forEach(function (button) {
    button.addEventListener('click', function () {
      if (button.disabled) return;
      if (button.dataset.busy === 'true') return;

      var icon = button.querySelector('i');
      var input = document.getElementById(button.getAttribute('data-input'));
      var platform = button.getAttribute('data-platform');
      var url = button.getAttribute('data-verify-url');
      var status = document.querySelector('[data-handle-status="' + platform + '"]');
      var handle = input ? input.value.trim() : '';
      var form = button.closest('form');
      var token = form ? form.querySelector('input[name="__RequestVerificationToken"]') : null;

      if (!handle) {
        if (window.Toast) {
          window.Toast.show({ type: 'warning', message: 'Type your ' + platform + ' handle first.' });
        }
        if (input) input.focus();
        return;
      }

      button.disabled = true;
      button.dataset.busy = 'true';
      if (icon) icon.classList.add('animate-spin');

      var body = new URLSearchParams();
      body.append('platform', platform);
      body.append('handle', handle);
      if (token) body.append('__RequestVerificationToken', token.value);

      fetch(url, {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8'
        },
        body: body.toString(),
        credentials: 'same-origin'
      })
        .then(function (response) {
          return response
            .json()
            .catch(function () {
              return { ok: false, message: 'The check returned an unexpected response.' };
            });
        })
        .then(function (payload) {
          if (window.Toast) {
            window.Toast.show({
              type: payload.ok ? 'success' : 'warning',
              message: payload.message || (payload.ok ? 'Handle verified.' : 'That handle could not be verified.')
            });
          }

          if (status) {
            status.textContent = payload.ok ? 'Verified.' : 'Not verified yet — verification is advisory only.';
            status.classList.toggle('text-success', !!payload.ok);
          }
        })
        .catch(function () {
          if (window.Toast) {
            window.Toast.show({
              type: 'error',
              message: 'The platform could not be reached. Try again in a moment.'
            });
          }
        })
        .finally(function () {
          button.disabled = false;
          button.dataset.busy = 'false';
          if (icon) icon.classList.remove('animate-spin');
        });
    });
  });
})();