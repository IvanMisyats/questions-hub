// 18+ adult content blur and reveal logic
// Reveal is transient: refreshing the page re-blurs everything.

(function () {
    'use strict';

    /**
     * Click handler for individual adult content reveal overlays.
     * Called from onclick on .adult-reveal-overlay elements.
     */
    window.revealAdultContent = function (overlayEl) {
        var wrapper = overlayEl.closest('.adult-blur-wrapper');
        if (wrapper) {
            wrapper.classList.remove('blurred');
        }
    };

    /**
     * Reveal all 18+ content on the current page (used on package detail banner).
     */
    window.revealAllAdultContent = function () {
        document.querySelectorAll('.adult-blur-wrapper.blurred').forEach(function (w) {
            w.classList.remove('blurred');
        });

        var banner = document.getElementById('adult-banner');
        if (banner) {
            banner.style.display = 'none';
        }
    };
})();
