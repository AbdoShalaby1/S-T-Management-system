(function () {
  'use strict';

  var editors = document.querySelectorAll('[data-md-editor]');
  if (!editors.length) return;

  editors.forEach(function (root) {
    var tabs = root.querySelectorAll('[data-md-tab]');
    var toggle = root.querySelector('[data-md-toggle]');
    var toggleLabel = toggle ? toggle.querySelector('[data-md-toggle-label]') : null;
    var toggleIcon = toggle ? toggle.querySelector('[data-md-toggle-icon]') : null;
    var writePane = root.querySelector('[data-md-pane="write"]');
    var previewPane = root.querySelector('[data-md-pane="preview"]');
    var input = root.querySelector('[data-md-input]');
    var previewUrl = root.getAttribute('data-preview-url');
    var current = 'write';
    var renderSeq = 0;
    var debounceTimer = null;

    function show(name) {
      current = name;
      tabs.forEach(function (tab) {
        var active = tab.getAttribute('data-md-tab') === name;
        tab.classList.toggle('btn-primary', active);
        tab.classList.toggle('btn-ghost', !active);
        tab.setAttribute('aria-pressed', active ? 'true' : 'false');
      });

      if (toggle) {
        var previewing = name === 'preview';
        toggle.setAttribute('aria-pressed', previewing ? 'true' : 'false');
        if (toggleLabel) toggleLabel.textContent = previewing ? 'Write' : 'Preview';
        if (toggleIcon) {
          toggleIcon.className = previewing
            ? 'fa-solid fa-pen text-[10px]'
            : 'fa-solid fa-eye text-[10px]';
        }
      }

      if (writePane) writePane.hidden = name !== 'write';
      if (previewPane) previewPane.hidden = name !== 'preview';
    }

    function render() {
      if (!previewPane || !input) return;

      var seq = ++renderSeq;
      previewPane.innerHTML =
        '<p class="text-xs text-muted">Rendering preview...</p>';

      var body = new URLSearchParams();
      body.append('markdown', input.value);

      var form = root.closest('form');
      var token = form ? form.querySelector('input[name="__RequestVerificationToken"]') : null;
      if (token) body.append('__RequestVerificationToken', token.value);

      fetch(previewUrl, {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8'
        },
        body: body.toString(),
        credentials: 'same-origin'
      })
        .then(function (response) {
          return response.json();
        })
        .then(function (payload) {
          if (seq !== renderSeq) return;
          if (!payload || !payload.ok) {
            throw new Error('preview failed');
          }

          if (!input.value.trim()) {
            previewPane.innerHTML =
              '<p class="text-sm text-muted">Nothing to preview yet. Write the statement first.</p>';
            return;
          }

          previewPane.innerHTML =
            '<div class="md-body">' + payload.html + '</div>';
        })
        .catch(function () {
          if (seq !== renderSeq) return;
          previewPane.innerHTML =
            '<p class="text-sm text-danger">The preview could not be rendered. Your text is safe; try again.</p>';
        });
    }

    function scheduleRender() {
      window.clearTimeout(debounceTimer);
      debounceTimer = window.setTimeout(render, 300);
    }

    tabs.forEach(function (tab) {
      tab.addEventListener('click', function (event) {
        event.preventDefault();
        var name = tab.getAttribute('data-md-tab');
        if (name === current) return;

        show(name);
        if (name === 'preview') {
          render();
        }
      });
    });

    if (toggle) {
      toggle.addEventListener('click', function (event) {
        event.preventDefault();
        var next = current === 'preview' ? 'write' : 'preview';
        show(next);
        if (next === 'preview') {
          render();
        }
      });
    }

    // Keep the preview honest if it is already open while the text changes.
    if (input) {
      input.addEventListener('input', function () {
        if (current === 'preview') {
          scheduleRender();
        }
      });
    }

    show('write');
  });
})();