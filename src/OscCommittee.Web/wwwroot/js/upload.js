(function () {
  'use strict';

  var form = document.querySelector('[data-upload]');
  if (!form) return;

  var zone = form.querySelector('[data-upload-zone]');
  var folderInput = form.querySelector('[data-upload-folder]');
  var selection = form.querySelector('[data-upload-selection]');
  var list = form.querySelector('[data-upload-list]');
  var summary = form.querySelector('[data-upload-summary]');
  var sizeLabel = form.querySelector('[data-upload-size]');
  var errorLabel = form.querySelector('[data-upload-error]');
  var warningLabel = form.querySelector('[data-upload-warning]');
  var progress = form.querySelector('[data-upload-progress]');
  var track = form.querySelector('[data-upload-track]');
  var bar = form.querySelector('[data-upload-bar]');
  var percentLabel = form.querySelector('[data-upload-percent]');
  var submitButton = form.querySelector('[data-upload-submit]');
  var clearButton = form.querySelector('[data-upload-clear]');

  var maxFiles = parseInt(form.getAttribute('data-max-files') || '300', 10);
  var maxTotalMb = parseInt(form.getAttribute('data-max-total-mb') || '30', 10);
  var maxFileMb = parseInt(form.getAttribute('data-max-file-mb') || '2', 10);
  var maxTotalBytes = maxTotalMb * 1024 * 1024;
  var maxFileBytes = maxFileMb * 1024 * 1024;

  var files = [];
  var blocked = false;
  var uploading = false;

  function formatBytes(bytes) {
    if (bytes >= 1024 * 1024) {
      return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    }
    return (bytes / 1024).toFixed(1) + ' KB';
  }

  function pathOf(file) {
    return file.relativePath || file.webkitRelativePath || file.name;
  }

  function setMessages(error, warning) {
    errorLabel.textContent = error || '';
    errorLabel.classList.toggle('hidden', !error);
    warningLabel.textContent = warning || '';
    warningLabel.classList.toggle('hidden', !warning);
  }

  function evaluate() {
    var total = files.reduce(function (sum, file) { return sum + file.size; }, 0);
    var biggest = files.reduce(function (name, file) {
      return file.size > maxFileBytes && !name ? pathOf(file) : name;
    }, '');
    var hasCode = files.some(function (file) {
      return /\.cs$/i.test(pathOf(file));
    });

    blocked = false;

    if (files.length > maxFiles) {
      blocked = true;
      setMessages(
        'That folder has more than ' + maxFiles +
          ' files. Upload the project folder without build output (bin, obj) and try again.',
        '');
    } else if (total > maxTotalBytes) {
      blocked = true;
      setMessages(
        'That upload is over ' + maxTotalMb +
          ' MB. Remove build output (bin, obj) and try again.',
        '');
    } else if (biggest) {
      blocked = true;
      setMessages(
        '“' + biggest + '” is over ' + maxFileMb +
          ' MB. Remove build output (bin, obj) and try again.',
        '');
    } else if (!hasCode) {
      blocked = true;
      setMessages(
        'This folder has no .cs file, so the server would reject it. Add at least one .cs file and try again.',
        '');
    } else {
      setMessages('', '');
    }

    submitButton.disabled = uploading || blocked || files.length === 0;
  }

  function render() {
    var total = files.reduce(function (sum, file) { return sum + file.size; }, 0);

    selection.hidden = files.length === 0;
    clearButton.hidden = files.length === 0;
    summary.textContent =
      files.length + (files.length === 1 ? ' file selected' : ' files selected');
    sizeLabel.textContent = formatBytes(total);

    list.innerHTML = '';
    files.slice(0, 8).forEach(function (file) {
      var item = document.createElement('li');
      item.className = 'truncate py-0.5';
      item.textContent = '/' + pathOf(file);
      list.appendChild(item);
    });

    if (files.length > 8) {
      var more = document.createElement('li');
      more.className = 'py-0.5 text-muted/70';
      more.textContent = '+ ' + (files.length - 8) + ' more';
      list.appendChild(more);
    }

    evaluate();
  }

  function setFiles(next) {
    files = next
      .filter(function (file) { return file && file.size >= 0; })
      .map(function (file) {
        if (!file.relativePath) {
          file.relativePath = file.webkitRelativePath || file.name;
        }
        return file;
      });

    render();
  }

  function fromInput() {
    return Array.from(folderInput.files || []);
  }

  function readAll(reader) {
    return new Promise(function (resolve, reject) {
      reader.readEntries(resolve, reject);
    });
  }

  function fileOf(entry) {
    return new Promise(function (resolve, reject) {
      entry.file(resolve, reject);
    });
  }

  async function walk(entry, prefix, out) {
    if (!entry) return;

    if (entry.isFile) {
      var file = await fileOf(entry);
      file.relativePath = prefix + entry.name;
      out.push(file);
      return;
    }

    if (entry.isDirectory) {
      var reader = entry.createReader();
      var batch;
      do {
        batch = await readAll(reader);
        for (var index = 0; index < batch.length; index++) {
          await walk(batch[index], prefix + entry.name + '/', out);
        }
      } while (batch.length > 0);
    }
  }

  async function fromDrop(dataTransfer) {
    var out = [];
    var items = dataTransfer.items ? Array.from(dataTransfer.items) : [];

    if (items.length && items[0].webkitGetAsEntry) {
      for (var index = 0; index < items.length; index++) {
        var entry = items[index].webkitGetAsEntry();
        if (entry) {
          await walk(entry, '', out);
        }
      }
    }

    if (!out.length) {
      out = Array.from(dataTransfer.files || []);
    }

    return out;
  }

  function setProgress(percent) {
    bar.style.width = percent + '%';
    track.setAttribute('aria-valuenow', String(percent));
    percentLabel.textContent = percent + '%';
  }

  function statusMessage(status) {
    if (status === 413) {
      return 'That upload is too large for the server. Remove build output and try again.';
    }
    if (status === 429) {
      return 'You have uploaded too many times in the last hour. Try again later.';
    }
    return 'The upload did not finish. Try again.';
  }

  function fail(message) {
    uploading = false;
    progress.hidden = true;
    submitButton.disabled = false;
    setMessages(message, '');
    if (window.Toast) {
      window.Toast.show({ type: 'error', message: message });
    }
  }

  function upload() {
    var data = new FormData();
    var token = form.querySelector('input[name="__RequestVerificationToken"]');

    if (token) {
      data.append(token.name, token.value);
    }

    files.forEach(function (file) {
      data.append('files', file, pathOf(file));
    });

    uploading = true;
    submitButton.disabled = true;
    progress.hidden = false;
    setProgress(0);

    var xhr = new XMLHttpRequest();
    xhr.open('POST', form.action, true);
    xhr.setRequestHeader('Accept', 'text/html');

    xhr.upload.onprogress = function (event) {
      if (event.lengthComputable) {
        setProgress(Math.min(100, Math.round((event.loaded / event.total) * 100)));
      }
    };

    xhr.onload = function () {
      // The server redirects on success and on a handled rejection; both land on a page that
      // renders the toast, so navigate to the final URL.
      if (xhr.status >= 200 && xhr.status < 400 && xhr.responseURL) {
        window.location.assign(xhr.responseURL);
        return;
      }

      fail(statusMessage(xhr.status));
    };

    xhr.onerror = function () {
      fail('The upload did not finish. Check your connection and try again.');
    };

    xhr.send(data);
  }

  folderInput.addEventListener('change', function () {
    setFiles(fromInput());
  });

  if (clearButton) {
    clearButton.addEventListener('click', function () {
      folderInput.value = '';
      setFiles([]);
    });
  }

  ['dragenter', 'dragover'].forEach(function (name) {
    zone.addEventListener(name, function (event) {
      event.preventDefault();
      event.stopPropagation();
      zone.classList.add('is-dragging');
    });
  });

  ['dragleave', 'dragend'].forEach(function (name) {
    zone.addEventListener(name, function (event) {
      event.preventDefault();
      event.stopPropagation();
      zone.classList.remove('is-dragging');
    });
  });

  zone.addEventListener('drop', function (event) {
    event.preventDefault();
    event.stopPropagation();
    zone.classList.remove('is-dragging');

    if (!event.dataTransfer) return;

    fromDrop(event.dataTransfer)
      .then(setFiles)
      .catch(function () {
        setMessages('That folder could not be read. Use “Select folder” instead.', '');
      });
  });

  form.addEventListener('submit', function (event) {
    if (uploading) {
      event.preventDefault();
      return;
    }

    // The folder input is the no-JavaScript fallback; the script sends the same files with
    // their relative paths and an upload progress bar.
    event.preventDefault();

    if (files.length === 0 || blocked) {
      evaluate();
      return;
    }

    upload();
  });
})();