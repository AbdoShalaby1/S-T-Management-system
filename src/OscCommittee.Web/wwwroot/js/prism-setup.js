(function () {
  'use strict';

  if (!window.Prism) return;

  function languageOf(code) {
    var match = /(?:^|\s)language-([\w+-]+)/.exec(code.className || '');
    return match ? match[1] : '';
  }

  function copyButton(code) {
    var button = document.createElement('button');
    button.type = 'button';
    button.className = 'btn btn-sm';
    button.setAttribute('data-copy', '');
    button.setAttribute('title', 'Copy this block');
    button.innerHTML =
      '<i class="fa-regular fa-copy text-[10px]" aria-hidden="true"></i> Copy';
    button.addEventListener('click', function (event) {
      event.preventDefault();
      var text = code.textContent || '';
      if (!text.trim() || !window.Toast) return;

      var done = function () {
        window.Toast.show({ type: 'success', message: 'Copied to clipboard.' });
      };
      var failed = function () {
        window.Toast.show({
          type: 'error',
          message: 'Could not copy. Select the text and copy manually.'
        });
      };

      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(text).then(done, failed);
        return;
      }

      var area = document.createElement('textarea');
      area.value = text;
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();
      try {
        document.execCommand('copy') ? done() : failed();
      } catch (error) {
        failed();
      } finally {
        area.remove();
      }
    });

    return button;
  }

  function dressFences() {
    // Markdown fences arrive as <pre><code class="language-x">. Wrap them like the shared
    // _CodeBlock partial so every code surface has a header, a copy button and line numbers.
    document.querySelectorAll('pre > code[class*="language-"]').forEach(function (code) {
      var pre = code.parentElement;
      if (!pre || pre.closest('.code-shell')) return;

      var shell = document.createElement('div');
      shell.className = 'code-shell';

      var head = document.createElement('div');
      head.className = 'code-shell-head';

      var name = document.createElement('span');
      name.className = 'code-shell-name';
      name.textContent = (languageOf(code) || 'code').toUpperCase();

      head.appendChild(name);
      head.appendChild(copyButton(code));

      pre.parentNode.insertBefore(shell, pre);
      shell.appendChild(head);
      shell.appendChild(pre);

      pre.classList.add('line-numbers');
    });
  }

  function start() {
    dressFences();
    window.Prism.highlightAll();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', start);
  } else {
    start();
  }
})();