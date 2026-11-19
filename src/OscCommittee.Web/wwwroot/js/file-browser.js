(function () {
  'use strict';

  // Picking a file in the shared browser is a full page load (the selected path lives in the
  // query string). Remember where the reader was so the page does not jump back to the top.
  var storageKey = 'osc:file-browser-scroll';

  function saveScroll() {
    try {
      sessionStorage.setItem(storageKey, JSON.stringify({
        path: window.location.pathname,
        y: window.scrollY,
        at: Date.now()
      }));
    } catch (error) {
      // Storage can be unavailable (private mode); a plain click still works.
    }
  }

  document.querySelectorAll('[data-file-browser-link]').forEach(function (link) {
    link.addEventListener('click', saveScroll);
  });

  var params = new URLSearchParams(window.location.search);
  if (!params.has('file') && !params.has('reference')) return;

  var raw = null;
  try {
    raw = sessionStorage.getItem(storageKey);
    sessionStorage.removeItem(storageKey);
  } catch (error) {
    return;
  }

  if (!raw) return;

  var saved;
  try {
    saved = JSON.parse(raw);
  } catch (error) {
    return;
  }

  if (!saved || saved.path !== window.location.pathname || Date.now() - saved.at > 15000) {
    return;
  }

  // Two frames: the first lets Prism finish highlighting, the second restores after layout.
  requestAnimationFrame(function () {
    requestAnimationFrame(function () {
      window.scrollTo(0, saved.y);
    });
  });
})();