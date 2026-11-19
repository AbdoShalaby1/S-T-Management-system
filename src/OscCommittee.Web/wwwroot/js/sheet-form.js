(function () {
  'use strict';

  var form = document.querySelector('[data-sheet-form]');
  if (!form) return;

  var editButton = document.querySelector('[data-enable-edit]');
  var cancelEditButton = form.querySelector('[data-cancel-edit]');
  var backLink = form.querySelector('[data-view-back]');
  var saveButton = form.querySelector('[data-save-button]');
  var heading = document.querySelector('[data-page-heading]');
  var crumb = document.querySelector('[data-page-crumb]');
  var fields = form.querySelectorAll('[data-sheet-field]');
  var editOnly = document.querySelectorAll('[data-sheet-edit-only]');

  function show(element, visible) {
    if (!element) return;
    if (visible) {
      element.removeAttribute('hidden');
    } else {
      element.setAttribute('hidden', 'hidden');
    }
  }

  function setEditing(enabled) {
    for (var index = 0; index < fields.length; index += 1) {
      if (fields[index].getAttribute('data-stay-disabled') !== 'true') {
        fields[index].disabled = !enabled;
      }
    }

    for (var other = 0; other < editOnly.length; other += 1) {
      show(editOnly[other], enabled);
    }

    show(saveButton, enabled);
    show(cancelEditButton, enabled);
    show(editButton, !enabled);
    show(backLink, !enabled);

    if (heading) heading.textContent = enabled ? 'Edit DS sheet' : 'View DS sheet';
    if (crumb) crumb.textContent = enabled ? 'Edit' : 'View';
  }

  if (editButton) {
    editButton.addEventListener('click', function () {
      setEditing(true);
    });
  }

  if (cancelEditButton) {
    cancelEditButton.addEventListener('click', function () {
      form.reset();
      setEditing(false);
    });
  }
})();
