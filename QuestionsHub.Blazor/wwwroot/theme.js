window.toggleTheme = function () {
    const html = document.documentElement;
    const current = html.getAttribute('data-bs-theme');
    const next = current === 'dark' ? 'light' : 'dark';
    html.setAttribute('data-bs-theme', next);
    localStorage.setItem('theme', next);
    return next;
};

window.getTheme = function () {
    return document.documentElement.getAttribute('data-bs-theme');
};

window.applyTheme = function () {
    const stored = localStorage.getItem('theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const theme = stored || (prefersDark ? 'dark' : 'light');
    document.documentElement.setAttribute('data-bs-theme', theme);
};

// Re-apply theme on Blazor enhanced navigation
Blazor.addEventListener('enhancedload', window.applyTheme);
