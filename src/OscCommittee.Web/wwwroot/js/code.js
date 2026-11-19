(function () {
  'use strict';

  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text);
    }

    return new Promise(function (resolve, reject) {
      var area = document.createElement('textarea');
      area.value = text;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();

      try {
        document.execCommand('copy') ? resolve() : reject(new Error('copy failed'));
      } catch (error) {
        reject(error);
      } finally {
        area.remove();
      }
    });
  }

  document.addEventListener('click', function (event) {
    var button = event.target.closest('[data-copy]');
    if (!button) return;

    var selector = button.dataset.copyTarget;
    var source = selector ? document.querySelector(selector) : null;
    var text = source ? (source.dataset.code || source.textContent || '') : button.dataset.copy || '';

    if (!text.trim()) return;

    copyText(text)
      .then(function () {
        if (window.Toast) window.Toast.show({ type: 'success', message: 'Copied to clipboard.' });
      })
      .catch(function () {
        if (window.Toast) window.Toast.show({ type: 'error', message: 'Could not copy. Select the text and copy manually.' });
      });
  });
})();
