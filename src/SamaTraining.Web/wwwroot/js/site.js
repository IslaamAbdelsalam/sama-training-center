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
      var cur = opt && opt.dataset.currency ? opt.dataset.currency : '—';
      currencyHint.textContent = cur;
      document.querySelectorAll('[data-currency-label]').forEach(function (el) { el.textContent = cur; });
    };
    countrySelect.addEventListener('change', syncCurrency);
    syncCurrency();
  }
  // ---- Workshop schedule (US-05): single day or date range ----
  var multi = document.querySelectorAll('input[name="IsMultiDay"]');
  if (multi.length && document.getElementById('end-date-group')) {
    var syncSchedule = function () {
      var range = document.querySelector('input[name="IsMultiDay"]:checked');
      var isRange = range && range.value === 'true';
      document.getElementById('end-date-group').classList.toggle('d-none', !isRange);
      var lbl = document.getElementById('start-date-label');
      if (lbl) lbl.textContent = isRange ? (lbl.dataset.textRange || 'Start date') : (lbl.dataset.textSingle || 'Date');
      var start = document.getElementById('Date'), end = document.getElementById('EndDate');
      if (start && end) {
        var fixEnd = isRange && start.value && (!end.value || end.value < start.value);
        if (end._flatpickr) {
          end._flatpickr.set('minDate', start.value || null);
          if (fixEnd) end._flatpickr.setDate(start.value, false);
        } else {
          end.min = start.value || '';
          if (fixEnd) end.value = start.value;
        }
      }
    };
    multi.forEach(function (r) { r.addEventListener('change', syncSchedule); });
    var startInput = document.getElementById('Date');
    if (startInput) startInput.addEventListener('change', syncSchedule);
    document.addEventListener('sama:datepickers', syncSchedule);
    syncSchedule();
  }

  // ---- Rich-text agenda editors (US-04): <div data-rich-editor="TextareaId" dir="ltr|rtl"> ----
  if (window.Quill) {
    var toolbar = [
      [{ header: [2, 3, 4, false] }],
      ['bold', 'italic', 'underline', 'strike'],
      [{ color: [] }, { background: [] }],
      [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }],
      [{ align: [] }, { direction: 'rtl' }],
      ['blockquote', 'link', 'clean']
    ];
    document.querySelectorAll('[data-rich-editor]').forEach(function (host) {
      var field = document.getElementById(host.getAttribute('data-rich-editor'));
      if (!field) return;
      var rtl = host.getAttribute('dir') === 'rtl';
      var q = new Quill(host, { theme: 'snow', modules: { toolbar: toolbar }, placeholder: host.getAttribute('data-placeholder') || '' });
      if (field.value) q.clipboard.dangerouslyPasteHTML(field.value);
      if (rtl && !field.value) { q.format('direction', 'rtl'); q.format('align', 'right'); }
      var save = function () {
        var html = q.root.innerHTML;
        field.value = q.getText().trim().length === 0 && html.indexOf('<hr') < 0 ? '' : html;
      };
      q.on('text-change', save);
      if (field.form) field.form.addEventListener('submit', save);
    });
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

  // ---- Whole-row links: a row whose main link has data-row-link can be clicked anywhere ----
  document.querySelectorAll('tr a[data-row-link]').forEach(function (a) {
    var tr = a.closest('tr');
    if (tr && !tr.classList.contains('row-link')) { tr.classList.add('row-link'); tr.dataset.href = a.href; }
  });
  document.addEventListener('click', function (e) {
    var tr = e.target.closest && e.target.closest('tr.row-link');
    if (!tr || e.target.closest('a, button, input, select, textarea, label, form, [data-bs-toggle]')) return;
    if (window.getSelection && String(window.getSelection()).trim().length) return; // let people select text
    if (e.ctrlKey || e.metaKey || e.shiftKey) window.open(tr.dataset.href, '_blank');
    else window.location.href = tr.dataset.href;
  });

  // ---- Date fields: always shown as dd/MM/yyyy (sent to the server as yyyy-MM-dd). Loaded only on pages with dates. ----
  var dateInputs = document.querySelectorAll('input[type="date"]');
  if (dateInputs.length) {
    var isAr = document.documentElement.lang === 'ar';
    var load = function (src, done) {
      var s = document.createElement('script'); s.src = src; s.onload = done; s.onerror = function () { }; document.head.appendChild(s);
    };
    var css = document.createElement('link'); css.rel = 'stylesheet'; css.href = '/lib/flatpickr/flatpickr.min.css?v=4.6.13';
    document.head.appendChild(css);
    var parseDmy = function (str) {
      var m = /^\s*(\d{1,2})[\/.\-](\d{1,2})[\/.\-](\d{4})\s*$/.exec(str || '');
      if (m) return new Date(+m[3], +m[2] - 1, +m[1]);
      m = /^\s*(\d{4})-(\d{1,2})-(\d{1,2})\s*$/.exec(str || '');
      if (m) return new Date(+m[1], +m[2] - 1, +m[3]);
      return undefined;
    };
    var init = function () {
      dateInputs.forEach(function (inp) {
        flatpickr(inp, {
          dateFormat: 'Y-m-d', altInput: true, altFormat: 'd/m/Y', allowInput: true, disableMobile: true,
          parseDate: parseDmy, minDate: inp.min || null, maxDate: inp.max || null,
          locale: isAr && flatpickr.l10ns && flatpickr.l10ns.ar ? 'ar' : 'default',
          onReady: function (_, __, fp) { if (fp.altInput) fp.altInput.placeholder = 'dd/mm/yyyy'; }
        });
      });
      document.dispatchEvent(new Event('sama:datepickers'));
    };
    load('/lib/flatpickr/flatpickr.min.js?v=4.6.13', function () {
      if (isAr) load('/lib/flatpickr/ar.js?v=4.6.13', init); else init();
    });
  }

  // ---- Sidebar collapse on desktop (the template only supports it on small screens) ----
  // The circle button next to the logo shrinks the menu to icons; hovering the slim menu shows it in full.
  var root = document.documentElement, sideMenu = document.getElementById('layout-menu');
  if (sideMenu) {
    var small = function () { return window.Helpers && Helpers.isSmallScreen ? Helpers.isSmallScreen() : window.innerWidth < 1200; };
    var KEY = 'sama.menuCollapsed';
    try { if (localStorage.getItem(KEY) === '1' && !small()) root.classList.add('layout-menu-collapsed'); } catch (e) { }
    document.addEventListener('click', function (e) {
      var t = e.target.closest && e.target.closest('#layout-menu .layout-menu-toggle');
      if (!t || small()) return;
      e.preventDefault(); e.stopPropagation();
      var on = !root.classList.contains('layout-menu-collapsed');
      root.classList.remove('layout-menu-hover');
      root.classList.toggle('layout-menu-collapsed', on);
      try { localStorage.setItem(KEY, on ? '1' : '0'); } catch (e2) { }
      window.dispatchEvent(new Event('resize'));
    }, true);
    var hoverTimer = null;
    sideMenu.addEventListener('mouseenter', function () {
      if (small() || !root.classList.contains('layout-menu-collapsed')) return;
      hoverTimer = setTimeout(function () { root.classList.add('layout-menu-hover'); }, 150);
    });
    sideMenu.addEventListener('mouseleave', function () {
      clearTimeout(hoverTimer); root.classList.remove('layout-menu-hover');
    });
  }
})();
