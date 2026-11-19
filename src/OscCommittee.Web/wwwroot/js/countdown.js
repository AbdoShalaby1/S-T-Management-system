(function () {
  'use strict';

  var MINUTE = 60 * 1000;

  function span(ms) {
    var minutes = Math.floor(ms / MINUTE);
    var days = Math.floor(minutes / 1440);
    var hours = Math.floor((minutes % 1440) / 60);
    var mins = minutes % 60;

    if (days > 0) return days + 'd ' + hours + 'h';
    if (hours > 0) return hours + 'h ' + mins + 'm';
    if (minutes > 0) return minutes + 'm';
    return 'under a minute';
  }

  function render(element) {
    var target = Date.parse(element.getAttribute('data-countdown-to'));
    if (isNaN(target)) return;

    var remaining = target - Date.now();

    if (remaining > 0) {
      element.textContent = 'in ' + span(remaining);
      element.setAttribute('data-countdown-state', 'open');
      element.classList.remove('text-danger');
      element.classList.toggle('text-warning', remaining < 24 * 60 * MINUTE);
    } else {
      element.textContent = 'overdue by ' + span(-remaining);
      element.setAttribute('data-countdown-state', 'overdue');
      element.classList.remove('text-warning');
      element.classList.add('text-danger');
    }
  }

  function tick() {
    var nodes = document.querySelectorAll('[data-countdown-to]');
    for (var index = 0; index < nodes.length; index += 1) {
      render(nodes[index]);
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', tick);
  } else {
    tick();
  }

  // Minute granularity is enough; this also picks up a laptop waking from sleep.
  window.setInterval(tick, 30000);
})();