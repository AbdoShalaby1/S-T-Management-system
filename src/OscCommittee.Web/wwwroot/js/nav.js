(function () {
  'use strict';

  function setupSidebar() {
    var toggle = document.getElementById('nav-toggle');
    var sidebar = document.getElementById('sidebar');
    var scrim = document.getElementById('nav-scrim');
    if (!toggle || !sidebar || !scrim) return;

    var desktop = window.matchMedia('(min-width: 1024px)');

    function setOpen(open) {
      var mobile = !desktop.matches;

      sidebar.classList.toggle('-translate-x-full', !open);
      scrim.classList.toggle('hidden', !open);
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
      document.documentElement.classList.toggle('overflow-hidden', open && mobile);

      // An off-screen drawer must not keep focusable links, otherwise Tab walks
      // through navigation the member cannot see.
      sidebar.inert = !open && mobile;
    }

    toggle.addEventListener('click', function () {
      setOpen(sidebar.classList.contains('-translate-x-full'));
    });

    scrim.addEventListener('click', function () {
      setOpen(false);
    });

    sidebar.querySelectorAll('a').forEach(function (link) {
      link.addEventListener('click', function () {
        if (!desktop.matches) setOpen(false);
      });
    });

    // On phones, a tap anywhere outside the drawer closes it. The scrim already
    // swallows those clicks; this also covers taps on the top bar and any page
    // element the scrim does not sit over.
    document.addEventListener('click', function (event) {
      if (desktop.matches) return;
      if (sidebar.classList.contains('-translate-x-full')) return;
      if (!(event.target instanceof Element)) return;
      if (event.target.closest('#sidebar') || event.target.closest('#nav-toggle')) return;
      setOpen(false);
    });

    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape' && !scrim.classList.contains('hidden')) setOpen(false);
    });

    desktop.addEventListener('change', function () {
      setOpen(false);
    });

    // Match the markup's starting state, including `inert` on small screens.
    setOpen(false);
  }

  function setupUserMenu() {
    var button = document.getElementById('user-menu-button');
    var menu = document.getElementById('user-menu');
    if (!button || !menu) return;

    function close() {
      menu.classList.add('hidden');
      button.setAttribute('aria-expanded', 'false');
    }

    button.addEventListener('click', function (event) {
      event.stopPropagation();
      var isOpen = !menu.classList.contains('hidden');
      menu.classList.toggle('hidden', isOpen);
      button.setAttribute('aria-expanded', isOpen ? 'false' : 'true');
    });

    document.addEventListener('click', function (event) {
      if (!menu.contains(event.target) && event.target !== button) close();
    });

    document.addEventListener('keydown', function (event) {
      if (event.key === 'Escape') close();
    });
  }

  function setupBackButtons() {
    document.addEventListener('click', function (event) {
      var button = event.target.closest('[data-back]');
      if (!button) return;

      if (window.history.length > 1) {
        window.history.back();
      } else {
        window.location.href = '/';
      }
    });
  }

  function init() {
    setupSidebar();
    setupUserMenu();
    setupBackButtons();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
