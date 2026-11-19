(function () {
  'use strict';

  var FOCUSABLE = 'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])';
  var dialogSeq = 0;

  function ask(options) {
    var settings = options || {};

    return new Promise(function (resolve) {
      var previouslyFocused = document.activeElement;
      // Unique per dialog: two confirms in a row must not share an id.
      var dialogId = 'confirm-dialog-' + (++dialogSeq);

      var scrim = document.createElement('div');
      scrim.className = 'modal-scrim';

      var panel = document.createElement('div');
      panel.className = 'modal-panel';
      panel.setAttribute('role', 'dialog');
      panel.setAttribute('aria-modal', 'true');
      panel.setAttribute('aria-labelledby', dialogId + '-title');
      panel.setAttribute('aria-describedby', dialogId + '-message');

      var title = document.createElement('h2');
      title.id = dialogId + '-title';
      title.className = 'text-base font-semibold text-text';
      title.textContent = settings.title || 'Are you sure?';

      var message = document.createElement('p');
      message.id = dialogId + '-message';
      message.className = 'mt-2 text-sm leading-relaxed text-muted';
      message.textContent = settings.message || '';

      var actions = document.createElement('div');
      actions.className = 'mt-5 flex flex-col-reverse justify-end gap-2 sm:flex-row';

      var cancel = document.createElement('button');
      cancel.type = 'button';
      cancel.className = 'btn btn-ghost';
      cancel.textContent = settings.cancelLabel || 'Cancel';

      var confirm = document.createElement('button');
      confirm.type = 'button';
      confirm.className = settings.danger === false ? 'btn btn-primary' : 'btn btn-danger';
      confirm.textContent = settings.confirmLabel || 'Confirm';

      actions.append(cancel, confirm);
      panel.append(title, message, actions);
      scrim.appendChild(panel);
      document.body.appendChild(scrim);

      function close(result) {
        document.removeEventListener('keydown', onKeyDown, true);
        scrim.remove();
        if (previouslyFocused && typeof previouslyFocused.focus === 'function') {
          previouslyFocused.focus();
        }
        resolve(result);
      }

      function onKeyDown(event) {
        if (event.key === 'Escape') {
          event.preventDefault();
          close(false);
          return;
        }

        if (event.key !== 'Tab') return;

        var focusable = panel.querySelectorAll(FOCUSABLE);
        if (focusable.length === 0) return;

        var first = focusable[0];
        var last = focusable[focusable.length - 1];

        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault();
          last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first.focus();
        }
      }

      cancel.addEventListener('click', function () {
        close(false);
      });

      confirm.addEventListener('click', function () {
        close(true);
      });

      scrim.addEventListener('click', function (event) {
        if (event.target === scrim) close(false);
      });

      document.addEventListener('keydown', onKeyDown, true);
      cancel.focus();
    });
  }

  function bindFormForms() {
    document.addEventListener('submit', function (event) {
      var form = event.target;
      if (!(form instanceof HTMLFormElement)) return;

      var message = form.dataset.confirm;
      if (!message || form.dataset.confirmed === 'true') return;

      event.preventDefault();

      ask({
        title: form.dataset.confirmTitle || 'Are you sure?',
        message: message,
        confirmLabel: form.dataset.confirmLabel || 'Confirm',
        danger: form.dataset.confirmDanger !== 'false'
      }).then(function (confirmed) {
        if (!confirmed) return;
        form.dataset.confirmed = 'true';
        form.requestSubmit();
      });
    });

    document.addEventListener('click', function (event) {
      var link = event.target.closest('a[data-confirm]');
      if (!link) return;

      event.preventDefault();

      ask({
        title: link.dataset.confirmTitle || 'Are you sure?',
        message: link.dataset.confirm,
        confirmLabel: link.dataset.confirmLabel || 'Continue',
        danger: link.dataset.confirmDanger !== 'false'
      }).then(function (confirmed) {
        if (confirmed) window.location.href = link.href;
      });
    });
  }

  window.Confirm = { ask: ask };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', bindFormForms);
  } else {
    bindFormForms();
  }
})();
