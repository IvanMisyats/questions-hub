window.toggleTheme = function () {
    const html = document.documentElement;
    const current = html.getAttribute('data-bs-theme');
    const next = current === 'dark' ? 'light' : 'dark';
    html.setAttribute('data-bs-theme', next);
    localStorage.setItem('theme', next);
    return next;
};

window.toggleThemeWithIcon = function (button) {
    window.toggleTheme();
};

window.getTheme = function () {
    return document.documentElement.getAttribute('data-bs-theme');
};
