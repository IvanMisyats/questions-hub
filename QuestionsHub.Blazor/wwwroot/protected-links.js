// Links and addresses kept out of the page HTML, so bots that harvest it (Telegram invite links, e-mail
// addresses) do not find them. The server writes them protected: Base64 of the reversed string
// (SiteLinks.Protect).
//   <a href="#" data-protected-href="…">  gets its real href only when someone is about to use it
//       (pointer down, keyboard focus, click) — never on page load, so crawlers that run scripts but do
//       not click (search engines) do not see it either.
//   <a href="#" data-protected-email="…"> becomes a mailto: link on page load; the address is written
//       into its [data-email-text] child (or the link itself).
(function () {
    function decode(value) {
        try {
            return atob(value).split('').reverse().join('');
        } catch (e) {
            return null;
        }
    }

    function revealHref(event) {
        var target = event.target;
        var link = target && target.closest ? target.closest('a[data-protected-href]') : null;
        if (!link) return;
        var url = decode(link.getAttribute('data-protected-href'));
        if (url) link.setAttribute('href', url);
        link.removeAttribute('data-protected-href');
    }

    // Capture phase: the href is in place before the browser follows the link or opens its menu
    ['pointerdown', 'focusin', 'click'].forEach(function (type) {
        document.addEventListener(type, revealHref, true);
    });

    function revealEmails() {
        var links = document.querySelectorAll('a[data-protected-email]');
        for (var i = 0; i < links.length; i++) {
            var link = links[i];
            var email = decode(link.getAttribute('data-protected-email'));
            if (!email) continue;
            link.setAttribute('href', 'mailto:' + email);
            (link.querySelector('[data-email-text]') || link).textContent = email;
            link.removeAttribute('data-protected-email');
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', revealEmails);
    } else {
        revealEmails();
    }
})();
