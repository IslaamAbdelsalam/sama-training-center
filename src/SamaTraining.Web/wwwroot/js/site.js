// SAMA Training Center — small page behaviours.
(function () {
  'use strict';

  // Confirmation before destructive actions: <form data-confirm="Are you sure?">
  document.addEventListener('submit', function (e) {
    var form = e.target;
    var message = form.getAttribute && form.getAttribute('data-confirm');
    if (message && !window.confirm(message)) {
      e.preventDefault();
    }
  });

  // Copy-to-clipboard buttons: <button data-copy-target="#id">
  document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-copy-target]');
    if (!btn) return;
    var target = document.querySelector(btn.getAttribute('data-copy-target'));
    if (!target) return;
    var text = target.value || target.textContent;
    var done = function () {
      var original = btn.innerHTML;
      btn.innerHTML = '<i class="icon-base ri ri-check-line me-1"></i>Copied';
      setTimeout(function () { btn.innerHTML = original; }, 1800);
    };
    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(done);
    } else {
      target.select && target.select();
      document.execCommand('copy');
      done();
    }
  });

  // Workshop form: show hall for on-site, meeting link for online.
  var typeSelect = document.getElementById('Type');
  if (typeSelect && document.getElementById('hall-group')) {
    var sync = function () {
      var onsite = typeSelect.value === 'OnSite' || typeSelect.value === '2';
      document.getElementById('hall-group').classList.toggle('d-none', !onsite);
      document.getElementById('link-group').classList.toggle('d-none', onsite);
    };
    typeSelect.addEventListener('change', sync);
    sync();
  }
})();
