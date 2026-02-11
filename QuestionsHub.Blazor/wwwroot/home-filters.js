/**
 * Home page filters - handles search, editor dropdown, sorting, and dynamic content loading.
 * Uses AJAX to fetch packages and renders them without full page reload.
 * Updates browser URL with history.pushState for shareable links.
 */
(function () {
    'use strict';

    const DEBOUNCE_MS = 300;
    const API_URL = '/api/packages/search';
    const ICONS_SVG = window.__iconsSvgUrl || '/icons.svg';
    const EDITORS_API_URL = '/api/packages/editors';
    const POPULAR_TAGS_API_URL = '/api/packages/popular-tags';
    const TAG_PAGE_SIZE = 10;

    // State
    let state = {
        search: null,
        editorId: null,
        editorName: 'Всі редактори',
        tagId: null,
        tagName: '',
        sort: 'PublicationDate',
        dir: 'Desc',
        page: 1,
        totalPages: 1,
        totalCount: 0,
        hasFilters: false
    };

    let visibleTagCount = TAG_PAGE_SIZE;
    let totalTags = 0;

    let searchDebounceTimer = null;
    let editorSearchDebounceTimer = null;
    let isLoading = false;
    let isActive = false;

    /**
     * Initializes the home page filters.
     * Can be called multiple times safely (idempotent).
     */
    function init() {
        // Only initialize if the home page elements exist
        const pageState = document.getElementById('page-state');
        if (!pageState) return;

        // Read initial state from hidden div only on first init
        // (not on MutationObserver re-triggers during DOM updates)
        if (!isActive) {
            loadInitialState();
        }

        // Set up event listeners (each function is idempotent)
        setupSearchInput();
        setupEditorDropdown();
        setupSortControls();
        setupMobileToggle();
        setupPaginationLinks();
        setupResetFiltersButton();
        setupTagButtons();
        setupTagExpandButton();
        updateTagVisibility();

        // Handle browser back/forward (only add once)
        if (!window._homeFiltersPopstateAdded) {
            window.addEventListener('popstate', handlePopState);
            window._homeFiltersPopstateAdded = true;
        }

        isActive = true;
    }

    /**
     * Loads initial state from server-rendered hidden div.
     */
    function loadInitialState() {
        const pageState = document.getElementById('page-state');
        if (pageState) {
            state.search = pageState.dataset.search || null;
            state.editorId = pageState.dataset.editorId ? parseInt(pageState.dataset.editorId) : null;
            state.editorName = pageState.dataset.editorName || 'Всі редактори';
            state.tagId = pageState.dataset.tagId ? parseInt(pageState.dataset.tagId) : null;
            state.tagName = pageState.dataset.tagName || '';
            const section = document.getElementById('popular-tags-section');
            totalTags = section ? parseInt(section.dataset.totalTags) || 0 : 0;
            visibleTagCount = TAG_PAGE_SIZE;
            state.sort = pageState.dataset.sort || 'PublicationDate';
            state.dir = pageState.dataset.dir || 'Desc';
            state.page = parseInt(pageState.dataset.page) || 1;
            state.totalPages = parseInt(pageState.dataset.totalPages) || 1;
            state.totalCount = parseInt(pageState.dataset.totalCount) || 0;
            state.hasFilters = pageState.dataset.hasFilters === 'true';
        }
    }

    /**
     * Sets up the package search input with debounce.
     */
    function setupSearchInput() {
        const searchInput = document.getElementById('package-search');
        if (!searchInput || searchInput._hfInit) return;
        searchInput._hfInit = true;

        searchInput.addEventListener('input', function () {
            clearTimeout(searchDebounceTimer);
            const value = this.value.trim();
            searchDebounceTimer = setTimeout(() => {
                if (state.search !== (value || null)) {
                    state.search = value || null;
                    state.page = 1;
                    fetchAndRenderPackages();
                }
            }, DEBOUNCE_MS);
        });

        // Prevent form submission on Enter
        searchInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                clearTimeout(searchDebounceTimer);
                const value = this.value.trim();
                if (state.search !== (value || null)) {
                    state.search = value || null;
                    state.page = 1;
                    fetchAndRenderPackages();
                }
            }
        });
    }

    /**
     * Sets up the editor dropdown.
     */
    function setupEditorDropdown() {
        const editorDropdown = document.getElementById('editor-dropdown');
        if (!editorDropdown) return;
        
        // Use a synchronous flag to prevent race conditions during initialization
        if (editorDropdown._hfInit || editorDropdown._hfInitializing) {
            return;
        }
        editorDropdown._hfInitializing = true;

        const dropdownBtn = editorDropdown.querySelector('.dropdown-toggle');
        const dropdownMenu = editorDropdown.querySelector('.dropdown-menu');
        const editorSearchInput = document.getElementById('editor-search');
        const editorList = document.getElementById('editor-list');

        // Don't mark as initialized until we've verified critical elements exist
        if (!dropdownBtn || !dropdownMenu) {
            editorDropdown._hfInitializing = false;
            return;
        }
        editorDropdown._hfInit = true;

        // Toggle dropdown
        dropdownBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            const isOpen = dropdownMenu.classList.contains('show');
            closeAllDropdowns();
            if (!isOpen) {
                dropdownMenu.classList.add('show');
                editorSearchInput?.focus();
                loadEditors();
            }
        });

        // Close on outside click (only add this listener once globally)
        if (!window._homeFiltersDocumentClickAdded) {
            window._homeFiltersDocumentClickAdded = true;
            document.addEventListener('click', function (e) {
                const dropdown = document.getElementById('editor-dropdown');
                if (dropdown && !dropdown.contains(e.target)) {
                    const menu = dropdown.querySelector('.dropdown-menu');
                    menu?.classList.remove('show');
                }
            }, true);
        }

        // Editor search with debounce
        editorSearchInput?.addEventListener('input', function () {
            clearTimeout(editorSearchDebounceTimer);
            editorSearchDebounceTimer = setTimeout(() => {
                loadEditors(this.value.trim());
            }, DEBOUNCE_MS);
        });

        editorSearchInput?.addEventListener('click', e => e.stopPropagation());

        // Editor selection (delegated)
        editorList?.addEventListener('click', function (e) {
            const item = e.target.closest('[data-editor-id]');
            if (item) {
                const editorId = item.dataset.editorId ? parseInt(item.dataset.editorId) : null;
                const editorName = item.dataset.editorName || 'Всі редактори';
                
                state.editorId = editorId;
                state.editorName = editorName;
                state.page = 1;
                
                // Update button text
                const btnText = dropdownBtn.querySelector('.text-truncate');
                if (btnText) btnText.textContent = editorName;
                
                dropdownMenu.classList.remove('show');
                fetchAndRenderPackages();
            }
        });
    }

    /**
     * Sets up sort controls.
     */
    function setupSortControls() {
        const sortSelect = document.getElementById('sort-select');
        const sortDirBtn = document.getElementById('sort-dir-btn');

        if (sortSelect && !sortSelect._hfInit) {
            sortSelect._hfInit = true;
            sortSelect.addEventListener('change', function () {
                state.sort = this.value;
                state.page = 1;
                fetchAndRenderPackages();
            });
        }

        if (sortDirBtn && !sortDirBtn._hfInit) {
            sortDirBtn._hfInit = true;
            sortDirBtn.addEventListener('click', function () {
                state.dir = state.dir === 'Desc' ? 'Asc' : 'Desc';
                state.page = 1;
                updateSortDirButton();
                fetchAndRenderPackages();
            });
        }
    }

    /**
     * Sets up mobile filter toggle.
     */
    function setupMobileToggle() {
        const filterToggle = document.getElementById('filter-toggle');
        const filterPanel = document.getElementById('filter-panel');

        if (filterToggle && !filterToggle._hfInit) {
            filterToggle._hfInit = true;
            filterToggle.addEventListener('click', function () {
                const isExpanded = filterPanel.classList.contains('show');
                filterPanel.classList.toggle('show');
                this.setAttribute('aria-expanded', (!isExpanded).toString());
            });
        }
    }

    /**
     * Sets up pagination link interception.
     * Uses document-level delegation so it survives container removal/recreation.
     */
    function setupPaginationLinks() {
        if (window._homeFiltersPaginationAdded) return;
        window._homeFiltersPaginationAdded = true;

        document.addEventListener('click', function (e) {
            const link = e.target.closest('#pagination-container a.pagination-btn');
            if (link && !link.classList.contains('disabled')) {
                e.preventDefault();
                e.stopPropagation();
                const pageAttr = link.dataset.page;
                if (pageAttr) {
                    const page = parseInt(pageAttr, 10);
                    if (page && page !== state.page) {
                        state.page = page;
                        fetchAndRenderPackages();
                        // Scroll to top of packages
                        document.getElementById('packages-section')?.scrollIntoView({ behavior: 'smooth' });
                    }
                }
            }
        }, true);
    }

    /**
     * Sets up reset filters button.
     * Uses delegated event on document to handle dynamically added buttons.
     */
    function setupResetFiltersButton() {
        // Only add this listener once globally
        if (window._homeFiltersResetBtnAdded) return;
        window._homeFiltersResetBtnAdded = true;

        document.addEventListener('click', function (e) {
            if (e.target.closest('#reset-filters-btn')) {
                e.preventDefault();
                state.search = null;
                state.editorId = null;
                state.editorName = 'Всі редактори';
                state.tagId = null;
                state.tagName = '';
                state.page = 1;
                
                const searchInput = document.getElementById('package-search');
                if (searchInput) searchInput.value = '';
                updateEditorDropdownButton();
                updateTagButtons();
                
                fetchAndRenderPackages();
            }
        });
    }

    /**
     * Handles browser back/forward navigation.
     */
    function handlePopState(e) {
        if (e.state) {
            state = { ...state, ...e.state };
            updateUIFromState();
            fetchAndRenderPackages(false); // Don't push state again
        }
    }

    /**
     * Fetches packages from API and renders them.
     */
    async function fetchAndRenderPackages(pushHistory = true) {
        if (isLoading) return;
        isLoading = true;

        showLoadingState();

        try {
            const params = new URLSearchParams();
            if (state.search) params.set('search', state.search);
            if (state.editorId) params.set('editor', state.editorId);
            if (state.tagId) params.set('tag', state.tagId);
            if (state.sort && state.sort !== 'PublicationDate') params.set('sort', state.sort);
            if (state.dir && state.dir !== 'Desc') params.set('dir', state.dir);
            if (state.page > 1) params.set('page', state.page);

            const url = `${API_URL}?${params.toString()}`;
            const response = await fetch(url);
            
            if (!response.ok) throw new Error('Failed to fetch packages');
            
            const result = await response.json();
            
            state.totalCount = result.totalCount;
            state.totalPages = result.totalPages;
            state.page = result.currentPage;
            state.hasFilters = !!(state.search || state.editorId || state.tagId);

            renderPackages(result.packages);
            renderPagination();
            updateResultsHeader();
            updatePageState();

            if (pushHistory) {
                pushBrowserHistory();
            }
        } catch (error) {
            console.error('Error fetching packages:', error);
            showErrorState();
        } finally {
            isLoading = false;
        }
    }

    /**
     * Renders package cards.
     */
    function renderPackages(packages) {
        const section = document.getElementById('packages-section');
        if (!section) return;

        // Remove loading and no-results states
        const loading = section.querySelector('#loading-indicator');
        const noResults = section.querySelector('#no-results');
        loading?.remove();
        noResults?.remove();

        let container = document.getElementById('packages-container');
        let paginationContainer = document.getElementById('pagination-container');

        if (!packages || packages.length === 0) {
            // Show no results
            container?.remove();
            paginationContainer?.remove();
            
            const noResultsHtml = state.hasFilters
                ? `<div id="no-results">
                    <div class="alert alert-info">
                        <p class="mb-2">За вашим запитом нічого не знайдено.</p>
                        <a href="/" class="btn btn-sm btn-outline-primary" id="reset-filters-btn">Скинути фільтри</a>
                    </div>
                   </div>`
                : `<div id="no-results"><p>Поки немає пакетів. Слідкуйте за оновленнями.</p></div>`;
            
            section.insertAdjacentHTML('beforeend', noResultsHtml);
            return;
        }

        // Ensure container exists
        if (!container) {
            container = document.createElement('div');
            container.id = 'packages-container';
            container.className = 'row row-cols-1 row-cols-md-2 row-cols-lg-3 g-4';
            section.appendChild(container);
        }

        // Ensure pagination container exists
        if (!paginationContainer) {
            paginationContainer = document.createElement('div');
            paginationContainer.id = 'pagination-container';
            section.appendChild(paginationContainer);
        }

        // Render cards
        container.innerHTML = packages.map(pkg => renderPackageCard(pkg)).join('');
        container.style.opacity = '1';
        container.style.pointerEvents = '';

        // Format dates after rendering
        formatDates(container);
    }

    /**
     * Renders a single package card.
     */
    function renderPackageCard(pkg) {
        const playedPeriod = formatPlayedPeriod(pkg.playedFrom, pkg.playedTo);
        const publicationDateHtml = pkg.publicationDate
            ? `<p class="card-text text-muted mb-2">
                <small>
                    <strong>Опубліковано:</strong> <span data-utc="${escapeHtml(pkg.publicationDate)}"></span>
                </small>
               </p>`
            : '';
        const descriptionHtml = pkg.description
            ? `<p class="card-text small">${escapeHtml(pkg.description)}</p>`
            : '';
        const editorsHtml = pkg.editors && pkg.editors.length > 0
            ? `<p class="card-text">
                <small class="text-muted">
                    <strong>Редактори:</strong>
                    ${pkg.editors.map((ed, i) => 
                        ` <a href="/editor/${ed.id}" class="text-muted">${escapeHtml(ed.fullName)}</a>${i < pkg.editors.length - 1 ? ' ·' : ''}`
                    ).join('')}
                </small>
               </p>`
            : '';

        const tagsHtml = pkg.tags && pkg.tags.length > 0
            ? `<div class="d-flex flex-wrap gap-1 mt-2">
                ${pkg.tags.map(tag => 
                    `<span class="badge rounded-pill bg-primary">${escapeHtml(tag.name)}</span>`
                ).join('')}
               </div>`
            : '';

        return `
            <div class="col">
                <a href="/package/${pkg.id}" class="text-decoration-none text-reset">
                    <div class="card h-100 package-card">
                        <div class="card-body">
                            <h5 class="card-title">${escapeHtml(pkg.title)}</h5>
                            <p class="card-text text-muted mb-2">
                                <small>
                                    <strong>${pkg.questionsCount}</strong> запитань${playedPeriod ? ` · ${playedPeriod}` : ''}
                                </small>
                            </p>
                            ${publicationDateHtml}
                            ${descriptionHtml}
                            ${editorsHtml}
                            ${tagsHtml}
                        </div>
                    </div>
                </a>
            </div>
        `;
    }

    /**
     * Formats the played period (e.g., "грудень 2025 – січень 2026").
     */
    function formatPlayedPeriod(from, to) {
        if (!from && !to) return '';
        
        const months = [
            'січень', 'лютий', 'березень', 'квітень', 'травень', 'червень',
            'липень', 'серпень', 'вересень', 'жовтень', 'листопад', 'грудень'
        ];
        
        const formatDate = (dateStr) => {
            if (!dateStr) return null;
            const date = new Date(dateStr);
            return `${months[date.getMonth()]} ${date.getFullYear()}`;
        };
        
        const fromStr = formatDate(from);
        const toStr = formatDate(to);
        
        if (fromStr && toStr && fromStr !== toStr) {
            return `${fromStr} – ${toStr}`;
        }
        return fromStr || toStr || '';
    }

    /**
     * Formats UTC dates in the rendered content.
     */
    function formatDates(container) {
        container.querySelectorAll('[data-utc]').forEach(el => {
            const utc = el.dataset.utc;
            if (utc) {
                const date = new Date(utc);
                el.textContent = date.toLocaleDateString('uk-UA', {
                    day: 'numeric',
                    month: 'long',
                    year: 'numeric'
                });
            }
        });
    }

    /**
     * Renders pagination.
     */
    function renderPagination() {
        const container = document.getElementById('pagination-container');
        if (!container) return;

        if (state.totalPages <= 1) {
            container.innerHTML = '';
            return;
        }

        const pages = getVisiblePages(state.page, state.totalPages);
        const baseUrl = buildBaseUrl();

        const firstDisabled = state.page <= 1;
        const lastDisabled = state.page >= state.totalPages;

        container.innerHTML = `
            <nav aria-label="Навігація по сторінках" class="pagination-nav mt-3">
                <div class="pagination-container">
                    <a class="pagination-btn ${firstDisabled ? 'disabled' : ''}"
                       href="${firstDisabled ? '#' : getPageUrl(1, baseUrl)}"
                       data-page="1"
                       title="Перша сторінка">
                        <svg class="icon" width="16" height="16"><use href="${ICONS_SVG}#i-chevron-double-left"></use></svg>
                    </a>
                    <a class="pagination-btn ${firstDisabled ? 'disabled' : ''}"
                       href="${firstDisabled ? '#' : getPageUrl(state.page - 1, baseUrl)}"
                       data-page="${state.page - 1}"
                       title="Попередня сторінка">
                        <svg class="icon" width="16" height="16"><use href="${ICONS_SVG}#i-chevron-left"></use></svg>
                    </a>
                    <div class="pagination-numbers">
                        ${pages.map(p => `
                            <a class="pagination-btn pagination-number ${p === state.page ? 'active' : ''}"
                               href="${p === state.page ? '#' : getPageUrl(p, baseUrl)}"
                               data-page="${p}">
                                ${p}
                            </a>
                        `).join('')}
                    </div>
                    <a class="pagination-btn ${lastDisabled ? 'disabled' : ''}"
                       href="${lastDisabled ? '#' : getPageUrl(state.page + 1, baseUrl)}"
                       data-page="${state.page + 1}"
                       title="Наступна сторінка">
                        <svg class="icon" width="16" height="16"><use href="${ICONS_SVG}#i-chevron-right"></use></svg>
                    </a>
                    <a class="pagination-btn ${lastDisabled ? 'disabled' : ''}"
                       href="${lastDisabled ? '#' : getPageUrl(state.totalPages, baseUrl)}"
                       data-page="${state.totalPages}"
                       title="Остання сторінка">
                        <svg class="icon" width="16" height="16"><use href="${ICONS_SVG}#i-chevron-double-right"></use></svg>
                    </a>
                </div>
            </nav>
        `;
    }

    /**
     * Gets visible page numbers for pagination.
     */
    function getVisiblePages(current, total) {
        const maxVisible = 5;
        const pages = [];
        
        let start = Math.max(1, current - Math.floor(maxVisible / 2));
        let end = Math.min(total, start + maxVisible - 1);
        
        if (end - start + 1 < maxVisible) {
            start = Math.max(1, end - maxVisible + 1);
        }
        
        for (let i = start; i <= end; i++) {
            pages.push(i);
        }
        
        return pages;
    }

    /**
     * Builds the base URL for pagination (without page number).
     */
    function buildBaseUrl() {
        const params = new URLSearchParams();
        if (state.search) params.set('search', state.search);
        if (state.editorId) params.set('editor', state.editorId);
        if (state.sort && state.sort !== 'PublicationDate') params.set('sort', state.sort);
        if (state.dir && state.dir !== 'Desc') params.set('dir', state.dir);
        return params.toString() ? '?' + params.toString() : '';
    }

    /**
     * Gets URL for a specific page.
     */
    function getPageUrl(page, baseUrl) {
        if (page <= 1) {
            return '/' + baseUrl;
        }
        return '/' + page + baseUrl;
    }

    /**
     * Extracts page number from URL.
     */
    function extractPageFromUrl(href) {
        const match = href.match(/^\/(\d+)/);
        if (match) return parseInt(match[1]);
        if (href === '/' || href.startsWith('/?')) return 1;
        return 1;
    }

    /**
     * Updates the results header with new count.
     */
    function updateResultsHeader() {
        const header = document.getElementById('results-header');
        if (!header) return;

        const countEl = document.getElementById('packages-count');
        if (countEl) {
            countEl.textContent = state.totalCount;
        }

        // Update the header text based on whether filters are active
        const h4 = header.querySelector('h4');
        if (h4) {
            if (state.hasFilters) {
                h4.innerHTML = `<span>Знайдено <strong id="packages-count">${state.totalCount}</strong> пакетів</span>`;
            } else {
                h4.innerHTML = `<span>Пакетів на сайті <strong id="packages-count">${state.totalCount}</strong></span>`;
            }
        }
    }

    /**
     * Updates the hidden page state div.
     */
    function updatePageState() {
        const pageState = document.getElementById('page-state');
        if (!pageState) return;

        pageState.dataset.search = state.search || '';
        pageState.dataset.editorId = state.editorId || '';
        pageState.dataset.editorName = state.editorName;
        pageState.dataset.tagId = state.tagId || '';
        pageState.dataset.tagName = state.tagName;
        pageState.dataset.sort = state.sort;
        pageState.dataset.dir = state.dir;
        pageState.dataset.page = state.page;
        pageState.dataset.totalPages = state.totalPages;
        pageState.dataset.totalCount = state.totalCount;
        pageState.dataset.hasFilters = state.hasFilters.toString();
    }

    /**
     * Pushes current state to browser history.
     */
    function pushBrowserHistory() {
        const url = buildBrowserUrl();
        history.pushState({ ...state }, '', url);
    }

    /**
     * Builds browser URL from current state.
     * Uses query parameters only (no page in path).
     */
    function buildBrowserUrl() {
        const params = new URLSearchParams();
        if (state.search) params.set('search', state.search);
        if (state.editorId) params.set('editor', state.editorId);
        if (state.tagId) params.set('tag', state.tagId);
        if (state.sort && state.sort !== 'PublicationDate') params.set('sort', state.sort);
        if (state.dir && state.dir !== 'Desc') params.set('dir', state.dir);
        if (state.page > 1) params.set('page', state.page);

        const queryString = params.toString();
        return '/' + (queryString ? '?' + queryString : '');
    }

    /**
     * Updates UI elements from state (used after popstate).
     */
    function updateUIFromState() {
        const searchInput = document.getElementById('package-search');
        if (searchInput) searchInput.value = state.search || '';

        updateEditorDropdownButton();
        updateSortDirButton();
        updateTagButtons();
        updateTagVisibility();

        const sortSelect = document.getElementById('sort-select');
        if (sortSelect) sortSelect.value = state.sort;
    }

    /**
     * Updates editor dropdown button text.
     */
    function updateEditorDropdownButton() {
        const btn = document.querySelector('#editor-dropdown .dropdown-toggle');
        const btnText = btn?.querySelector('.text-truncate');
        if (btnText) btnText.textContent = state.editorName;
    }

    /**
     * Updates sort direction button icon and title.
     */
    function updateSortDirButton() {
        const btn = document.getElementById('sort-dir-btn');
        if (!btn) return;

        const isDesc = state.dir === 'Desc';
        btn.title = isDesc ? 'Спадання' : 'Зростання';
        btn.innerHTML = `<svg class="icon" width="16" height="16"><use href="${ICONS_SVG}#i-sort-${isDesc ? 'down' : 'up'}"></use></svg>`;
    }

    /**
     * Shows loading state.
     */
    function showLoadingState() {
        const container = document.getElementById('packages-container');
        if (container) {
            container.style.opacity = '0.5';
            container.style.pointerEvents = 'none';
        }
    }

    /**
     * Shows error state.
     */
    function showErrorState() {
        const section = document.getElementById('packages-section');
        if (section) {
            section.innerHTML = `
                <div class="alert alert-danger">
                    Помилка завантаження пакетів. <a href="#" onclick="location.reload()">Спробуйте оновити сторінку</a>.
                </div>
            `;
        }
    }

    /**
     * Loads editors from API and renders them in the dropdown.
     */
    async function loadEditors(searchTerm = '') {
        const editorList = document.getElementById('editor-list');
        if (!editorList) return;

        try {
            let url = EDITORS_API_URL;
            if (searchTerm) {
                url += '?search=' + encodeURIComponent(searchTerm);
            }

            const response = await fetch(url);
            if (!response.ok) throw new Error('Failed to load editors');

            const editors = await response.json();
            renderEditorList(editors);
        } catch (error) {
            console.error('Error loading editors:', error);
            editorList.innerHTML = '<div class="dropdown-item text-muted">Помилка завантаження</div>';
        }
    }

    /**
     * Renders the editor list in the dropdown.
     */
    function renderEditorList(editors) {
        const editorList = document.getElementById('editor-list');
        if (!editorList) return;

        let html = `
            <button type="button" class="dropdown-item ${!state.editorId ? 'active' : ''}" 
                    data-editor-id="" data-editor-name="Всі редактори">
                <svg class="icon me-2" width="16" height="16">
                    <use href="${ICONS_SVG}#i-check"></use>
                </svg>
                Всі редактори
            </button>
        `;

        editors.forEach(editor => {
            const isActive = state.editorId === editor.id;
            html += `
                <button type="button" class="dropdown-item ${isActive ? 'active' : ''}" 
                        data-editor-id="${editor.id}" data-editor-name="${escapeHtml(editor.fullName)}">
                    ${escapeHtml(editor.fullName)}
                </button>
            `;
        });

        editorList.innerHTML = html;
    }

    /**
     * Sets up tag filter button click handlers.
     * Uses event delegation on the popular-tags-section container.
     */
    function setupTagButtons() {
        const section = document.getElementById('popular-tags-section');
        if (!section || section._hfTagInit) return;
        section._hfTagInit = true;

        section.addEventListener('click', function (e) {
            const tagBtn = e.target.closest('.tag-filter-btn');
            if (tagBtn) {
                e.preventDefault();
                const tagId = parseInt(tagBtn.dataset.tagId);
                const tagName = tagBtn.dataset.tagName || '';

                if (state.tagId === tagId) {
                    // Deselect
                    state.tagId = null;
                    state.tagName = '';
                } else {
                    state.tagId = tagId;
                    state.tagName = tagName;
                }
                state.page = 1;
                updateTagButtons();
                fetchAndRenderPackages();
                return;
            }

            const clearBtn = e.target.closest('#clear-tag-btn');
            if (clearBtn) {
                e.preventDefault();
                state.tagId = null;
                state.tagName = '';
                state.page = 1;
                updateTagButtons();
                fetchAndRenderPackages();
            }
        });
    }

    /**
     * Updates tag button styles and clear button visibility based on state.
     */
    function updateTagButtons() {
        const section = document.getElementById('popular-tags-section');
        if (!section) return;

        section.querySelectorAll('.tag-filter-btn').forEach(btn => {
            const tagId = parseInt(btn.dataset.tagId);
            if (state.tagId === tagId) {
                btn.classList.remove('btn-outline-primary');
                btn.classList.add('btn-primary');
            } else {
                btn.classList.remove('btn-primary');
                btn.classList.add('btn-outline-primary');
            }
        });

        // Show/hide clear button
        let clearBtn = section.querySelector('#clear-tag-btn');
        if (state.tagId) {
            if (!clearBtn) {
                const wrapper = section.querySelector('.d-flex');
                if (wrapper) {
                    const btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'btn btn-sm btn-outline-secondary rounded-pill';
                    btn.id = 'clear-tag-btn';
                    btn.innerHTML = `<svg class="icon me-1" width="16" height="16"><use href="${ICONS_SVG}#i-close"></use></svg> Скинути`;
                    wrapper.appendChild(btn);
                }
            }
        } else {
            clearBtn?.remove();
        }

        updateTagVisibility();
    }

    /**
     * Sets up the tag expand/collapse button click handler.
     */
    function setupTagExpandButton() {
        const section = document.getElementById('popular-tags-section');
        if (!section || section._hfExpandInit) return;
        section._hfExpandInit = true;

        section.addEventListener('click', function (e) {
            const expandBtn = e.target.closest('#tag-expand-btn');
            if (!expandBtn) return;
            e.preventDefault();

            if (visibleTagCount >= totalTags) {
                // Collapse back to first page
                visibleTagCount = TAG_PAGE_SIZE;
            } else {
                // Show next batch
                visibleTagCount = Math.min(visibleTagCount + TAG_PAGE_SIZE, totalTags);
            }
            updateTagVisibility();
        });
    }

    /**
     * Updates tag button visibility based on visibleTagCount.
     * Tags beyond the visible count get the 'tag-hidden' class.
     * The expand button text is updated accordingly.
     * A selected tag is always kept visible regardless of its index.
     */
    function updateTagVisibility() {
        const section = document.getElementById('popular-tags-section');
        if (!section) return;

        const tagBtns = section.querySelectorAll('.tag-filter-btn');
        tagBtns.forEach(btn => {
            const index = parseInt(btn.dataset.tagIndex);
            const isSelected = state.tagId && parseInt(btn.dataset.tagId) === state.tagId;
            if (index >= visibleTagCount && !isSelected) {
                btn.classList.add('tag-hidden');
            } else {
                btn.classList.remove('tag-hidden');
            }
        });

        // Update expand button
        const expandBtn = section.querySelector('#tag-expand-btn');
        if (!expandBtn) return;

        if (totalTags <= TAG_PAGE_SIZE) {
            // Not enough tags to need expand button
            expandBtn.style.display = 'none';
        } else if (visibleTagCount >= totalTags) {
            // All tags visible — show collapse button
            expandBtn.style.display = '';
            expandBtn.textContent = '\u2191 \u0417\u0433\u043e\u0440\u043d\u0443\u0442\u0438';
        } else {
            // More tags to show
            const remaining = totalTags - visibleTagCount;
            expandBtn.style.display = '';
            expandBtn.textContent = `+${remaining} \u0449\u0435`;
        }
    }

    /**
     * Closes all open dropdowns.
     */
    function closeAllDropdowns() {
        document.querySelectorAll('.dropdown-menu.show').forEach(menu => {
            menu.classList.remove('show');
        });
    }

    /**
     * Escapes HTML to prevent XSS.
     */
    function escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Initialize on DOM ready or immediately if already loaded
    function tryInit() {
        init();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', tryInit);
    } else {
        tryInit();
    }

    // Re-initialize when page is restored from bfcache (back/forward navigation)
    window.addEventListener('pageshow', function (e) {
        if (e.persisted) {
            // Page was restored from bfcache - reinitialize state from URL
            loadInitialState();
            // Reset any stale dropdown state
            closeAllDropdowns();
        }
    });

    // Handle Blazor enhanced navigation - re-init when navigating to this page
    // Register for Blazor enhancedload event (only once)
    function registerBlazorListener() {
        if (window._homeFiltersBlazorListenerAdded) return;
        if (typeof Blazor !== 'undefined' && Blazor.addEventListener) {
            Blazor.addEventListener('enhancedload', function() {
                // Reset active state for fresh initialization after navigation
                isActive = false;
                // Small delay to ensure DOM is ready after enhanced navigation
                setTimeout(tryInit, 10);
            });
            window._homeFiltersBlazorListenerAdded = true;
        }
    }

    // Try to register now if Blazor is ready
    registerBlazorListener();

    // If Blazor isn't ready yet, wait for it via polling
    if (!window._homeFiltersBlazorListenerAdded) {
        // Watch for Blazor to become available
        const checkBlazor = setInterval(() => {
            if (typeof Blazor !== 'undefined' && Blazor.addEventListener) {
                registerBlazorListener();
                clearInterval(checkBlazor);
            }
        }, 50);
        
        // Stop checking for Blazor after 5 seconds
        setTimeout(() => clearInterval(checkBlazor), 5000);
    }

    // Also use MutationObserver as a reliable fallback for any DOM changes
    // This handles cases where Blazor events don't fire or fire too early
    if (!window._homeFiltersMutationObserverAdded) {
        window._homeFiltersMutationObserverAdded = true;
        const observer = new MutationObserver(function (mutations) {
            // Only initialize if not already active
            // (using closure variable instead of DOM attribute which can be lost by Blazor patching)
            if (!isActive) {
                const pageState = document.getElementById('page-state');
                if (pageState) {
                    tryInit();
                }
            }
        });

        // Observe body for any child changes
        observer.observe(document.body, { childList: true, subtree: true });
    }
})();
