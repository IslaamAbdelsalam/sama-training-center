// SAMA Training Center — page behaviours: toasts, confirmation dialog, loading buttons,
// copy buttons, row highlight, and the workshop form's online/on-site switch.
(function () {
  'use strict';

  // ---- Toasts: show any rendered toasts (success / error / info / validation) ----
  document.querySelectorAll('.app-toast').forEach(function (el) {
    if (window.bootstrap && bootstrap.Toast) {
      var delay = parseInt(el.getAttribute('data-bs-delay') || '5000', 10);
      var bar = el.querySelector('.app-toast-progress');
      if (bar) bar.style.animationDuration = delay + 'ms';
      new bootstrap.Toast(el, { delay: delay }).show();
    } else {
      el.classList.add('show');
    }
  });

  // ---- Confirmation dialog: <form data-confirm="message" data-confirm-title="..." data-confirm-button="Delete"> ----
  var modalEl = document.getElementById('confirmModal');
  var pendingForm = null;
  document.addEventListener('submit', function (e) {
    var form = e.target;
    var message = form.getAttribute && form.getAttribute('data-confirm');
    if (!message || form.dataset.confirmed === 'true') return;
    e.preventDefault();

    if (!modalEl || !window.bootstrap) {
      if (window.confirm(message)) { form.dataset.confirmed = 'true'; form.requestSubmit ? form.requestSubmit() : form.submit(); }
      return;
    }

    pendingForm = form;
    document.getElementById('confirmTitle').textContent = form.getAttribute('data-confirm-title') || 'Are you sure?';
    document.getElementById('confirmMessage').textContent = message;
    var ok = document.getElementById('confirmOk');
    ok.textContent = form.getAttribute('data-confirm-button') || 'Yes, continue';
    ok.className = 'btn ' + (form.getAttribute('data-confirm-style') === 'primary' ? 'btn-primary' : 'btn-danger');
    bootstrap.Modal.getOrCreateInstance(modalEl).show();
  }, true);

  if (modalEl) {
    document.getElementById('confirmOk').addEventListener('click', function () {
      if (!pendingForm) return;
      var form = pendingForm;
      pendingForm = null;
      form.dataset.confirmed = 'true';
      bootstrap.Modal.getOrCreateInstance(modalEl).hide();
      if (form.requestSubmit) form.requestSubmit(); else form.submit();
    });
  }

  // ---- Loading state on submit: prevents double saves ----
  document.addEventListener('submit', function (e) {
    var form = e.target;
    if (e.defaultPrevented || form.method.toLowerCase() === 'get') return;
    if (window.jQuery && jQuery(form).data('validator') && !jQuery(form).valid()) return;
    var btn = e.submitter || form.querySelector('button[type=submit]');
    if (!btn || btn.dataset.noLoading !== undefined) return;
    // Keep the clicked button's name/value (e.g. "addAnother") in the post.
    if (btn.name) {
      var hidden = document.createElement('input');
      hidden.type = 'hidden'; hidden.name = btn.name; hidden.value = btn.value;
      form.appendChild(hidden);
    }
    setTimeout(function () {
      btn.disabled = true;
      btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status"></span>' +
        (btn.getAttribute('data-loading-text') || 'Saving…');
    }, 0);
  });

  // ---- Copy-to-clipboard buttons: <button data-copy-target="#id"> ----
  document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-copy-target]');
    if (!btn) return;
    var target = document.querySelector(btn.getAttribute('data-copy-target'));
    if (!target) return;
    var text = target.value || target.textContent;
    var done = function () {
      var original = btn.innerHTML;
      btn.innerHTML = '<i class="icon-base ri ri-check-line me-1"></i>Copied';
      btn.classList.add('btn-success');
      setTimeout(function () { btn.innerHTML = original; btn.classList.remove('btn-success'); }, 1800);
    };
    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(done);
    } else {
      if (target.select) target.select();
      document.execCommand('copy');
      done();
    }
  });

  // ---- Highlight the row that was just added or updated ----
  var highlighted = document.querySelector('.row-highlight');
  if (highlighted) {
    highlighted.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }

  // ---- Open the tab named in ?tab=... and keep the URL in sync when switching ----
  document.querySelectorAll('[data-bs-toggle="tab"][data-tab-name]').forEach(function (tab) {
    tab.addEventListener('shown.bs.tab', function () {
      var url = new URL(window.location.href);
      url.searchParams.set('tab', tab.getAttribute('data-tab-name'));
      history.replaceState(null, '', url);
    });
  });

  // ---- Workshop form: show hall for on-site, meeting link for online; show currency for the country ----
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
  var countrySelect = document.getElementById('Country');
  var currencyHint = document.getElementById('currency-hint');
  if (countrySelect && currencyHint) {
    var syncCurrency = function () {
      var opt = countrySelect.options[countrySelect.selectedIndex];
      currencyHint.textContent = opt && opt.dataset.currency ? opt.dataset.currency : '—';
    };
    countrySelect.addEventListener('change', syncCurrency);
    syncCurrency();
  }
  // ---- Registration phone: fixed "00" prefix, digits only, live check of country code + length ----
  var phone = document.getElementById('Phone');
  var help = document.getElementById('phone-help');
  if (phone && phone.dataset.phoneCountries && help) {
    var countries = [];
    try { countries = JSON.parse(phone.dataset.phoneCountries); } catch (e) { countries = []; }
    countries.sort(function (a, b) { return b.c.length - a.c.length; });
    var fmt = function (t, args) { return t.replace(/\{(\d)\}/g, function (_, i) { return args[i]; }); };

    var update = function () {
      // Keep digits only; drop a 00 or + the student typed or pasted (the 00 is already shown).
      var v = phone.value.replace(/\D/g, '').replace(/^0{2,}/, '');
      if (v !== phone.value) phone.value = v;
      help.classList.remove('text-success', 'text-warning');
      if (!v) { help.textContent = help.dataset.helpDefault; return; }
      var c = countries.find(function (x) { return v.indexOf(x.c) === 0; });
      if (!c) { help.textContent = help.dataset.helpDefault; return; }
      var rest = v.length - c.c.length;
      if (rest === c.d && v.charAt(c.c.length) !== '0') {
        help.textContent = fmt(help.dataset.okText, [c.n]);
        help.classList.add('text-success');
      } else if (rest < c.d) {
        help.textContent = fmt(help.dataset.needText, [c.n, c.d, c.c, c.d - rest]);
      } else {
        help.textContent = fmt(help.dataset.overText, [c.n, c.d, c.c]);
        help.classList.add('text-warning');
      }
    };
    phone.addEventListener('input', update);
    update();
  }
})();
