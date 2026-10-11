// Heart buttons (Components/FavoriteButton.razor): add/remove favorites through /api/favorites.
// The toggle is optimistic; it reverts if the server refuses. Does nothing at page load.
(function () {
    var TITLE_ON = 'Прибрати з обраного';
    var TITLE_OFF = 'Додати до обраного';
    var MESSAGE_FAILED = 'Не вдалося зберегти. Спробуйте ще раз.';
    var MESSAGE_FORBIDDEN = 'Обране доступне лише користувачам з підтвердженою електронною поштою.';
    var MESSAGE_TOO_MANY = 'Забагато змін поспіль. Спробуйте за хвилину.';

    // Requests in flight, by "kind:id" — one item can have several buttons on a page
    var busy = {};

    function loginUrl() {
        return '/Account/Login?returnUrl=' + encodeURIComponent(location.pathname + location.search + location.hash);
    }

    // Every signed-in page carries one (the logout form in the layout)
    function antiforgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    // A short message at the bottom of the screen (tooltips don't show on touch screens)
    var toastTimer;
    function showMessage(text) {
        var toast = document.getElementById('fav-toast');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'fav-toast';
            toast.className = 'fav-toast';
            toast.setAttribute('role', 'status');
            document.body.appendChild(toast);
        }
        toast.textContent = text;
        toast.classList.add('show');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { toast.classList.remove('show'); }, 4000);
    }

    function setState(kind, id, isFavorite) {
        var sprite = window.__iconsSvgUrl || '/icons.svg';
        var buttons = document.querySelectorAll('.fav-btn[data-fav-kind="' + kind + '"][data-fav-id="' + id + '"]');
        buttons.forEach(function (button) {
            var text = isFavorite ? TITLE_ON : TITLE_OFF;
            button.classList.toggle('is-fav', isFavorite);
            button.setAttribute('aria-pressed', isFavorite ? 'true' : 'false');
            button.setAttribute('aria-label', text);
            button.title = text;
            var use = button.querySelector('use');
            if (use) {
                use.setAttribute('href', sprite + (isFavorite ? '#i-heart-fill' : '#i-heart'));
            }
        });
    }

    window.toggleFavorite = function (button) {
        if (button.dataset.favAnon) {
            location.href = loginUrl();
            return;
        }

        var kind = button.dataset.favKind;
        var id = button.dataset.favId;
        var key = kind + ':' + id;
        if (busy[key]) return;

        var token = antiforgeryToken();
        if (!token) {
            console.error('favorites: no antiforgery token on the page');
            showMessage(MESSAGE_FAILED);
            return;
        }

        var wasFavorite = button.classList.contains('is-fav');
        busy[key] = true;
        setState(kind, id, !wasFavorite);

        fetch('/api/favorites/' + kind + '/' + id, {
            method: wasFavorite ? 'DELETE' : 'PUT',
            headers: { 'RequestVerificationToken': token },
            credentials: 'same-origin'
        }).then(function (response) {
            // Signed out meanwhile (a redirect to the login page means the same, should the endpoint ever redirect)
            if (response.status === 401 || (response.redirected && response.url.indexOf('/Account/Login') >= 0)) {
                location.href = loginUrl();
                return;
            }
            if (response.status === 403 || response.redirected) {
                setState(kind, id, wasFavorite);
                showMessage(MESSAGE_FORBIDDEN);
                return;
            }
            if (response.status === 429) {
                setState(kind, id, wasFavorite);
                showMessage(MESSAGE_TOO_MANY);
                return;
            }
            if (!response.ok) {
                setState(kind, id, wasFavorite);
                return response.json()
                    .catch(function () { return {}; })
                    .then(function (body) { showMessage((body && body.error) || MESSAGE_FAILED); });
            }
        }).catch(function () {
            setState(kind, id, wasFavorite);
            showMessage(MESSAGE_FAILED);
        }).finally(function () {
            delete busy[key];
        });
    };
})();
