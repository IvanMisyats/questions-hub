// Handle smooth scrolling for anchor links in Blazor interactive mode
window.scrollToElement = function(elementId) {
    console.log('scrollToElement called with:', elementId);
    const element = document.getElementById(elementId);
    if (element) {
        console.log('Element found:', element);

        // Get element position and add offset for fixed header/better spacing
        const elementPosition = element.getBoundingClientRect().top + window.pageYOffset;
        const offsetPosition = elementPosition - 100; // 100px offset for navbar and better spacing

        window.scrollTo({
            top: offsetPosition,
            behavior: 'smooth'
        });
        return true;
    } else {
        console.error('Element not found with id:', elementId);
    }
    return false;
};

// Copy text to clipboard
window.copyToClipboard = async function(text) {
    // Modern clipboard API (requires HTTPS in production)
    if (navigator.clipboard && window.isSecureContext) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (err) {
            console.error('Clipboard API failed:', err);
        }
    }

    // Fallback for older browsers or non-HTTPS contexts
    const textArea = document.createElement('textarea');
    textArea.value = text;
    textArea.style.position = 'fixed';
    textArea.style.left = '-999999px';
    textArea.style.top = '-999999px';
    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();

    try {
        document.execCommand('copy');
        textArea.remove();
        return true;
    } catch (err) {
        console.error('Fallback copy failed:', err);
        textArea.remove();
        return false;
    }
};

// Pre-fill a date input with value from another date input when empty
window.prefillDateFrom = function(targetId, sourceId) {
    const target = document.getElementById(targetId);
    const source = document.getElementById(sourceId);
    if (target && source && !target.value && source.value) {
        target.value = source.value;
    }
};

// Handle top search bar form submission (static SSR)
window.handleTopSearchSubmit = function(form) {
    const query = form.q.value.trim();
    if (query) {
        const encodedQuery = encodeURIComponent(query);
        window.location.href = '/search/' + encodedQuery;
    }
    return false;
};

// Handle search page form submission (static SSR)
window.handleSearchFormSubmit = function(form) {
    const query = form.q.value.trim();
    if (query) {
        const encodedQuery = encodeURIComponent(query);
        window.location.href = '/search/' + encodedQuery;
    }
    return false;
};

// Scroll position preservation for Blazor Server re-renders
window._savedScrollPosition = null;

window.saveScrollPosition = function() {
    window._savedScrollPosition = window.scrollY;
};

window.restoreScrollPosition = function() {
    if (window._savedScrollPosition !== null) {
        window.scrollTo(0, window._savedScrollPosition);
        window._savedScrollPosition = null;
    }
};

// Sidebar collapse toggle (desktop)
window.toggleSidebarCollapse = function() {
    // State lives on <html> so Blazor's DOM morphing never strips it
    const isCollapsed = document.documentElement.classList.toggle('sidebar-collapsed');
    localStorage.setItem('sidebar-collapsed', isCollapsed ? '1' : '0');
};

// Mobile sidebar drawer toggle
window.toggleMobileSidebar = function() {
    const sidebar = document.querySelector('.sidebar');
    const backdrop = document.querySelector('.sidebar-backdrop');
    if (!sidebar || !backdrop) return;

    const isOpen = sidebar.classList.contains('mobile-open');
    if (isOpen) {
        closeMobileSidebar();
    } else {
        sidebar.classList.add('mobile-open');
        backdrop.classList.add('show');
        document.body.style.overflow = 'hidden';
    }
};

window.closeMobileSidebar = function() {
    const sidebar = document.querySelector('.sidebar');
    const backdrop = document.querySelector('.sidebar-backdrop');
    if (!sidebar || !backdrop) return;

    sidebar.classList.remove('mobile-open');
    backdrop.classList.remove('show');
    document.body.style.overflow = '';
};

// Restore sidebar collapsed state on load.
(function initSidebarCollapse() {
    function applySidebarState() {
        if (window.innerWidth < 768) {
            document.documentElement.classList.remove('sidebar-collapsed');
            return;
        }
        if (localStorage.getItem('sidebar-collapsed') === '1') {
            document.documentElement.classList.add('sidebar-collapsed');
        } else {
            document.documentElement.classList.remove('sidebar-collapsed');
        }
    }

    applySidebarState();

    // Re-apply after Blazor enhanced navigation (same pattern as theme.js)
    Blazor.addEventListener('enhancedload', applySidebarState);
})();

