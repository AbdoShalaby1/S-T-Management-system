(function () {
  'use strict';

  var ICONS = {
    success: 'fa-circle-check',
    error: 'fa-circle-exclamation',
    warning: 'fa-triangle-exclamation',
    info: 'fa-circle-info'
  };

  var DURATIONS = { success: 5000, info: 5000, warning: 5000, error: 8000 };
  var MAX_VISIBLE = 4;

  var host = null;

  function ensureHost() {
    if (host && document.body.contains(host)) return host;

    host = document.getElementById('toast-host');
    if (!host) {
      host = document.createElement('div');
      host.id = 'toast-host';
      host.className =
        'pointer-events-none fixed inset-x-0 bottom-0 z-[60] flex flex-col items-center gap-2 p-4 sm:inset-x-auto sm:bottom-auto sm:right-6 sm:top-20 sm:items-end';
      host.setAttribute('aria-live', 'polite');
      document.body.appendChild(host);
    }

    return host;
  }

  function dismiss(element) {
    if (!element || element.dataset.leaving === 'true') return;
    element.dataset.leaving = 'true';
    element.classList.add('is-leaving');
    window.setTimeout(function () {
      element.remove();
    }, 160);
  }

  function show(options) {
    var settings = options || {};
    var type = ICONS[settings.type] ? settings.type : 'info';
    var duration = typeof settings.duration === 'number' ? settings.duration : DURATIONS[type];
    var mount = ensureHost();

    var visible = mount.querySelectorAll('.toast');
    for (var index = 0; index <= visible.length - MAX_VISIBLE; index++) {
      dismiss(visible[index]);
    }

    var toast = document.createElement('div');
    toast.className = 'toast';
    toast.dataset.type = type;
    toast.setAttribute('role', type === 'error' ? 'alert' : 'status');

    var icon = document.createElement('i');
    icon.className = 'toast-icon fa-solid ' + ICONS[type];
    icon.setAttribute('aria-hidden', 'true');

    var body = document.createElement('div');
    body.className = 'min-w-0';

    if (settings.title) {
      var title = document.createElement('p');
      title.className = 'toast-title';
      title.textContent = settings.title;
      body.appendChild(title);
    }

    var message = document.createElement('p');
    message.className = 'toast-message';
    if (settings.title) message.classList.add('mt-0.5');
    message.textContent = settings.message || '';
    body.appendChild(message);

    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'btn-icon -mr-1 -mt-1 h-7 w-7 text-muted';
    close.setAttribute('aria-label', 'Dismiss notification');
    var closeIcon = document.createElement('i');
    closeIcon.className = 'fa-solid fa-xmark';
    closeIcon.setAttribute('aria-hidden', 'true');
    close.appendChild(closeIcon);

    var progress = document.createElement('div');
    progress.className = 'toast-progress absolute bottom-0 left-0 h-0.5';
    progress.style.animationDuration = duration + 'ms';

    toast.append(icon, body, close, progress);
    mount.appendChild(toast);

    var remaining = duration;
    var startedAt = Date.now();
    var timer = window.setTimeout(function () {
      dismiss(toast);
    }, remaining);

    function pause() {
      window.clearTimeout(timer);
      remaining = Math.max(0, remaining - (Date.now() - startedAt));
      progress.style.animationPlayState = 'paused';
    }

    function resume() {
      startedAt = Date.now();
      progress.style.animationPlayState = 'running';
      timer = window.setTimeout(function () {
        dismiss(toast);
      }, remaining);
    }

    toast.addEventListener('mouseenter', pause);
    toast.addEventListener('mouseleave', resume);
    toast.addEventListener('focusin', pause);
    toast.addEventListener('focusout', resume);
    close.addEventListener('click', function () {
      window.clearTimeout(timer);
      dismiss(toast);
    });

    return toast;
  }

  function flushPayload() {
    var payload = document.getElementById('toast-payload');
    if (!payload) return;

    var items;
    try {
      items = JSON.parse(payload.textContent || '[]');
    } catch (error) {
      items = [];
    }

    payload.remove();

    if (!Array.isArray(items)) return;
    items.forEach(function (item) {
      show(item);
    });
  }

  window.Toast = { show: show, dismiss: dismiss };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', flushPayload);
  } else {
    flushPayload();
  }
})();
