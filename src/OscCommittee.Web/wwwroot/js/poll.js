(function () {
  'use strict';

  var host = document.querySelector('[data-poll]');
  if (!host) return;

  var url = host.getAttribute('data-poll-url');
  // No URL means the page rendered in a state where polling cannot change anything.
  if (!url) return;
  var detail = host.querySelector('[data-poll-detail]');
  var title = host.querySelector('[data-poll-title]');
  var delay = 2000;
  var stopped = false;

  function schedule() {
    if (stopped) return;
    window.setTimeout(check, delay);
    // PLAN §7.3: poll every 2 s, then back off to 10 s.
    delay = Math.min(Math.round(delay * 1.5), 10000);
  }

  function message(data) {
    if (data.status === 'Failed') {
      return data.errorMessage ||
        'Your files are safe. The board can retry grading without another upload.';
    }
    return 'Your files are stored and the bundle is ready. This page refreshes when the score arrives.';
  }

  function check() {
    fetch(url, {
      method: 'GET',
      headers: { Accept: 'application/json' },
      credentials: 'same-origin',
      cache: 'no-store'
    })
      .then(function (response) {
        if (response.status === 404) {
          stopped = true;
          return null;
        }
        return response.ok ? response.json() : null;
      })
      .then(function (data) {
        if (!data) {
          schedule();
          return;
        }

        if (data.status === 'Graded' || data.status === 'Failed') {
          stopped = true;
          window.location.reload();
          return;
        }

        if (data.status === 'AwaitingReview' || data.status === 'TryAgain') {
          // Reload once so the server renders the AI draft or the "Try again" upload form. The
          // reloaded page has no poll host, so waiting for the board costs no more requests.
          stopped = true;
          window.location.reload();
          return;
        }

        // The server tells us when grading is off; polling cannot change the outcome then.
        if (data.gradingConfigured === false) {
          stopped = true;
          if (title) title.textContent = 'Saved — waiting for the board';
          if (detail) {
            detail.textContent =
              'Grading is not configured on this deployment. Your files are safe; a board member can grade this attempt later.';
          }
          return;
        }

        if (title && data.status === 'Grading') {
          title.textContent = 'Grading in progress';
        }

        if (detail) {
          detail.textContent = message(data);
        }

        schedule();
      })
      .catch(schedule);
  }

  schedule();
})();