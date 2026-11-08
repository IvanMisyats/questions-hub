// Small behaviours for server-rendered forms (plain HTML forms, no Blazor circuit).
(function () {
    'use strict';

    // <form data-autosubmit>: submit as soon as one of its fields changes (filter checkboxes)
    document.addEventListener('change', function (e) {
        const form = e.target.form;
        if (form && form.hasAttribute('data-autosubmit')) {
            form.submit();
        }
    });

    // data-confirm on a submit button (or, as a fallback, on its form): ask before submitting
    document.addEventListener('submit', function (e) {
        const message = (e.submitter && e.submitter.dataset.confirm) || e.target.dataset.confirm;
        if (message && !window.confirm(message)) {
            e.preventDefault();
        }
    });
})();
