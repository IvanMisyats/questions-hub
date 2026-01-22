// Format UTC dates to local time for display
// Looks for elements with data-utc attribute and formats them

(function() {
    'use strict';

    const ukrainianMonths = [
        'січня', 'лютого', 'березня', 'квітня', 'травня', 'червня',
        'липня', 'серпня', 'вересня', 'жовтня', 'листопада', 'грудня'
    ];

    function formatUkrainianDate(date) {
        const day = date.getDate();
        const month = ukrainianMonths[date.getMonth()];
        const year = date.getFullYear();
        return `${day} ${month} ${year}`;
    }

    function formatDates() {
        document.querySelectorAll('[data-utc]').forEach(function(element) {
            const utcString = element.getAttribute('data-utc');
            if (utcString && !element.textContent) {
                try {
                    const date = new Date(utcString);
                    if (!isNaN(date.getTime())) {
                        element.textContent = formatUkrainianDate(date);
                    }
                } catch (e) {
                    console.error('Failed to parse date:', utcString, e);
                }
            }
        });
    }

    // Run on page load
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', formatDates);
    } else {
        formatDates();
    }

    // Re-run when Blazor updates the DOM
    const observer = new MutationObserver(function(mutations) {
        let hasNewDates = false;
        mutations.forEach(function(mutation) {
            if (mutation.addedNodes.length > 0) {
                mutation.addedNodes.forEach(function(node) {
                    if (node.nodeType === 1) {
                        if (node.hasAttribute && node.hasAttribute('data-utc')) {
                            hasNewDates = true;
                        } else if (node.querySelectorAll) {
                            if (node.querySelectorAll('[data-utc]').length > 0) {
                                hasNewDates = true;
                            }
                        }
                    }
                });
            }
        });
        if (hasNewDates) {
            formatDates();
        }
    });

    observer.observe(document.body, { childList: true, subtree: true });

    // Expose for manual calls if needed
    window.formatUtcDates = formatDates;
})();
