(function () {
  'use strict';

  /* ---------------------------------------------------------------- helpers */

  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text);
    }

    return new Promise(function (resolve, reject) {
      var area = document.createElement('textarea');
      area.value = text;
      area.setAttribute('readonly', '');
      area.style.position = 'fixed';
      area.style.opacity = '0';
      document.body.appendChild(area);
      area.select();

      try {
        document.execCommand('copy') ? resolve() : reject(new Error('copy failed'));
      } catch (error) {
        reject(error);
      } finally {
        area.remove();
      }
    });
  }

  /* -------------------------------------------------------------- body insert */

  function insertIntoBody(url) {
    var area = document.querySelector('[data-body-input]');
    if (!area) return false;

    var name = String(url || '').split('/').pop() || 'image';
    var alt = name.replace(/\.[^.]+$/, '').replace(/[-_]+/g, ' ').trim() || 'image';
    var markdown = '![' + alt + '](' + url + ')';

    var start = typeof area.selectionStart === 'number' ? area.selectionStart : area.value.length;
    var end = typeof area.selectionEnd === 'number' ? area.selectionEnd : area.value.length;

    // Put the snippet on its own line so it renders as a block, not inline text.
    var before = area.value.slice(0, start);
    var after = area.value.slice(end);
    var prefix = before.length && !/\n\s*$/.test(before) ? '\n\n' : '';
    var suffix = after.length && !/^\s*\n/.test(after) ? '\n\n' : '';

    area.value = before + prefix + markdown + suffix + after;
    area.dispatchEvent(new Event('input', { bubbles: true }));
    area.focus();

    var caret = (before + prefix + markdown + suffix).length;
    area.setSelectionRange(caret, caret);

    // Reveal the write pane so the insertion is visible.
    var editor = area.closest('[data-md-editor]');
    var writeTab = editor ? editor.querySelector('[data-md-tab="write"]') : null;
    if (writeTab) writeTab.click();

    return true;
  }

  /* ------------------------------------------------------------- image upload */

  function formatKb(bytes) {
    return (bytes / 1024).toFixed(1).replace('.0', '') + ' KB';
  }

  function imageListFor(section) {
    var list = section.querySelector('[data-image-list]');
    if (list) return list;

    list = document.createElement('ul');
    list.className = 'grid gap-3 sm:grid-cols-2';
    list.setAttribute('data-image-list', '');

    var empty = section.querySelector('[data-image-empty]');
    if (empty) {
      empty.replaceWith(list);
    } else {
      var body = section.querySelector('.card-body');
      if (body) body.appendChild(list);
    }

    return list;
  }

  function addImageCard(section, image) {
    var list = imageListFor(section);

    var item = document.createElement('li');
    item.className = 'flex items-center gap-3 rounded-input border border-border bg-surface-2/30 p-3';

    var thumb = document.createElement('img');
    thumb.src = image.url;
    thumb.alt = '';
    thumb.loading = 'lazy';
    thumb.className = 'h-14 w-14 shrink-0 rounded border border-border object-cover';

    var meta = document.createElement('div');
    meta.className = 'min-w-0 flex-1';

    var name = document.createElement('p');
    name.className = 'truncate font-mono text-xs text-text';
    name.textContent = image.fileName;

    var size = document.createElement('p');
    size.className = 'mt-0.5 text-[11px] text-muted nums';
    size.textContent = formatKb(image.bytes || 0);

    meta.append(name, size);

    var actions = document.createElement('div');
    actions.className = 'flex shrink-0 items-center gap-1';

    var insert = document.createElement('button');
    insert.type = 'button';
    insert.className = 'btn btn-sm';
    insert.setAttribute('data-image-insert', image.url);
    insert.innerHTML = '<i class="fa-solid fa-turn-down text-[10px]" aria-hidden="true"></i> Insert';

    var copy = document.createElement('button');
    copy.type = 'button';
    copy.className = 'btn btn-sm btn-ghost';
    copy.setAttribute('data-image-copy', image.url);
    copy.title = 'Copy the image address';
    copy.innerHTML = '<i class="fa-regular fa-copy text-[10px]" aria-hidden="true"></i>';

    actions.append(insert, copy);
    item.append(thumb, meta, actions);
    list.appendChild(item);
  }

  function showStatus(status, message, isError) {
    if (!status) return;

    status.textContent = message;
    status.classList.remove('hidden', 'border-danger/30', 'bg-danger/10', 'text-danger',
      'border-accent/30', 'bg-accent/10', 'text-text');

    if (isError) {
      status.classList.add('border-danger/30', 'bg-danger/10', 'text-danger');
    } else {
      status.classList.add('border-accent/30', 'bg-accent/10', 'text-text');
    }
  }

  var imageForms = document.querySelectorAll('[data-image-form]');

  imageForms.forEach(function (form) {
    var section = form.closest('section');
    var sectionOrForm = section || form;
    var status = sectionOrForm.querySelector('[data-image-status]');
    var submit = form.querySelector('[data-image-submit]');
    var fileInput = form.querySelector('[data-image-file]');

    form.addEventListener('submit', function (event) {
      if (!window.fetch || !window.FormData) return;

      event.preventDefault();

      var file = fileInput && fileInput.files ? fileInput.files[0] : null;
      if (!file) {
        showStatus(status, 'Choose an image first.', true);
        return;
      }

      if (file.size > 4 * 1024 * 1024) {
        showStatus(status, 'That image is over 4 MB. Shrink it and try again.', true);
        return;
      }

      var original = submit ? submit.innerHTML : '';
      if (submit) {
        submit.disabled = true;
        submit.textContent = 'Uploading...';
      }

      showStatus(status, 'Uploading ' + file.name + '...', false);

      fetch(form.getAttribute('data-upload-url'), {
        method: 'POST',
        headers: { Accept: 'application/json' },
        body: new FormData(form),
        credentials: 'same-origin'
      })
        .then(function (response) {
          return response.json();
        })
        .then(function (payload) {
          if (!payload || !payload.ok || !payload.data) {
            throw new Error(payload && payload.message
              ? payload.message
              : 'The upload failed.');
          }

          addImageCard(sectionOrForm, payload.data);
          showStatus(status, payload.message || 'Uploaded.', false);

          if (fileInput) fileInput.value = '';
          if (window.Toast) {
            window.Toast.show({ type: 'success', message: payload.message || 'Image uploaded.' });
          }
        })
        .catch(function (error) {
          showStatus(status, error.message || 'The upload failed. Try another image.', true);
          if (window.Toast) {
            window.Toast.show({
              type: 'error',
              message: error.message || 'The upload failed.'
            });
          }
        })
        .finally(function () {
          if (submit) {
            submit.disabled = false;
            submit.innerHTML = original;
          }
        });
    });
  });

  /* --------------------------------------------------- insert / copy buttons */

  document.addEventListener('click', function (event) {
    var insert = event.target.closest('[data-image-insert]');
    if (insert) {
      event.preventDefault();

      if (insertIntoBody(insert.getAttribute('data-image-insert'))) {
        if (window.Toast) {
          window.Toast.show({ type: 'success', message: 'Image added to the body.' });
        }
      } else if (window.Toast) {
        window.Toast.show({
          type: 'error',
          message: 'Open the editor first, then insert the image.'
        });
      }

      return;
    }

    var copy = event.target.closest('[data-image-copy]');
    if (copy) {
      event.preventDefault();

      copyText(copy.getAttribute('data-image-copy')).then(function () {
        if (window.Toast) {
          window.Toast.show({ type: 'success', message: 'Image address copied.' });
        }
      }, function () {
        if (window.Toast) {
          window.Toast.show({
            type: 'error',
            message: 'Could not copy. Select the address and copy it manually.'
          });
        }
      });
    }
  });
})();