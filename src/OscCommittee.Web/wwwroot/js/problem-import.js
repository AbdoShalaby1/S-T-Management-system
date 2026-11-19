(function () {
  'use strict';

  var form = document.querySelector('[data-problem-form]');
  if (!form) return;

  var details = form.querySelector('[data-details]');
  var importButton = form.querySelector('[data-import-button]');
  var saveButton = form.querySelector('button[name="command"][value="save"]');
  var status = form.querySelector('[data-import-status]');
  var url = form.querySelector('[data-problem-url]');
  var difficulty = form.querySelector('[data-difficulty]');
  var override = form.querySelector('[data-points-override]');
  var preview = form.querySelector('[data-points-preview]');
  var token = form.querySelector('input[name="__RequestVerificationToken"]');

  function field(name) {
    return form.querySelector('[name="' + name + '"]');
  }

  function setStatus(message, type) {
    if (!status) return;
    status.textContent = message;
    status.classList.toggle('text-danger', type === 'error');
    status.classList.toggle('text-muted', type !== 'error');
  }

  function show(element, visible) {
    if (!element) return;
    if (visible) {
      element.removeAttribute('hidden');
    } else {
      element.setAttribute('hidden', 'hidden');
    }
  }

  function showDetails() {
    if (!details) return;

    details.removeAttribute('hidden');

    var fields = details.querySelectorAll('input, select, textarea');
    for (var index = 0; index < fields.length; index += 1) {
      fields[index].disabled = false;
    }

    if (saveButton) saveButton.disabled = false;
    if (importButton) importButton.setAttribute('hidden', 'hidden');
  }

  function apply(data) {
    field('Platform').value = data.platform;
    field('ExternalId').value = data.externalId;
    field('Rating').value = data.rating === null || data.rating === undefined ? '' : data.rating;
    field('IsPaidOnly').value = data.isPaidOnly ? 'true' : 'false';

    if (url) url.value = data.url;

    var title = field('Title');
    if (title && !title.value.trim()) title.value = data.title;

    var badge = form.querySelector('[data-platform-badge]');
    if (badge) {
      badge.textContent = data.platformLabel;
      badge.className = 'badge ' + (data.platform === 'LeetCode' ? 'badge-platform-lc' : 'badge-platform-cf');
    }

    var external = form.querySelector('[data-external-id]');
    if (external) external.textContent = data.externalId;

    var reference = form.querySelector('[data-reference]');
    if (reference) {
      reference.textContent = data.reference || '';
      show(reference, Boolean(data.reference));
    }

    var ratingChip = form.querySelector('[data-rating-chip]');
    if (ratingChip) {
      ratingChip.textContent = data.rating ? 'rating ' + data.rating : '';
      show(ratingChip, Boolean(data.rating));
    }

    show(form.querySelector('[data-paid-chip]'), Boolean(data.isPaidOnly));
    show(form.querySelector('[data-paid-warning]'), Boolean(data.isPaidOnly));

    var warning = form.querySelector('[data-import-warning]');
    if (warning) {
      warning.textContent = data.warning || '';
      show(warning, Boolean(data.warning));
    }

    showDetails();
    updatePoints();
  }

  function lookup() {
    if (!url || !url.value.trim() || !token) return;

    var body = new URLSearchParams();
    body.set('url', url.value.trim());
    body.set('__RequestVerificationToken', token.value);

    setStatus('Looking up the problem…', 'info');
    if (importButton) importButton.disabled = true;

    fetch(form.getAttribute('data-preview-url'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: body.toString(),
      credentials: 'same-origin'
    })
      .then(function (response) {
        return response.json().then(function (payload) {
          return { ok: response.ok, payload: payload };
        });
      })
      .then(function (result) {
        if (!result.ok || !result.payload.ok) {
          setStatus(result.payload.message || 'That URL could not be read.', 'error');
          return;
        }

        var data = result.payload;
        apply(data);
        setStatus(
          'Found “' + data.title + '”. Pick the difficulty and the sheet, then add it.',
          'info'
        );
      })
      .catch(function () {
        setStatus('The lookup failed. You can still press “Fetch details” to retry on the server.', 'error');
      })
      .then(function () {
        if (importButton && !importButton.hasAttribute('hidden')) importButton.disabled = false;
      });
  }

  function pointsForDifficulty() {
    if (!difficulty) return 0;
    var value = difficulty.value;
    var easy = Number(form.getAttribute('data-preset-easy'));
    var medium = Number(form.getAttribute('data-preset-medium'));
    var hard = Number(form.getAttribute('data-preset-hard'));

    if (value === 'Easy') return easy;
    if (value === 'Hard') return hard;
    return medium;
  }

  function updatePoints() {
    if (!preview) return;

    var custom = override && override.value.trim() !== '' ? Number(override.value) : null;
    preview.textContent = custom === null || isNaN(custom) ? pointsForDifficulty() : custom;
  }

  if (importButton) {
    importButton.addEventListener('click', function (event) {
      if (!window.fetch || !url) return; // Let the server handle it without JS.
      event.preventDefault();
      lookup();
    });
  }

  if (difficulty) difficulty.addEventListener('change', updatePoints);
  if (override) {
    override.addEventListener('input', updatePoints);
    override.addEventListener('change', updatePoints);
  }

  if (url) {
    url.addEventListener('paste', function () {
      window.setTimeout(function () {
        if (importButton && !importButton.disabled) importButton.click();
      }, 50);
    });
  }
})();