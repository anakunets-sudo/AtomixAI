// menu.js
window.Atomix = window.Atomix || {};
window.Atomix.MenuController = {
    VISIBLE_ROWS: 4,
    get el() { return document.getElementById('ai-menu'); },
    get backHost() { return document.getElementById('menu-back-host'); },
    get searchEl() { return document.getElementById('menu-search'); },
    get searchInput() { return document.getElementById('menu-search-input'); },
    get searchBtn() { return document.getElementById('menu-search-btn'); },
    get listContainer() { return document.getElementById('menu-items'); },
    items: [],
    history: [], // Стек состояний: [{ items: [], index: 0, title: '' }]
    selectedIndex: -1,
    loadingIndex: -1,
    isOpen: false,
    currentTitle: 'BIM Context',
    initialHeight: 0,
    searchQuery: '',
    _searchBound: false,
    update(newItems, isBackAction = false, title = null) {
        // Если мы вернулись на главный уровень (Level 0), можно сбросить min-height
        // или оставить зафиксированной до закрытия меню #.
        if (!isBackAction && this.history.length === 0) {
            this.history = [];
            this.currentTitle = 'BIM Context';
            // Не сбрасываем minHeight здесь, чтобы не было прыжка при очистке списка
        }
        if (title) this.currentTitle = title;
        this.items = Array.isArray(newItems) ? newItems : this._getDefaultItems();
        this._resetSearch();
        // На верхнем уровне ничего не выделено (-1), в подменю — первый кликабельный пункт
        if (!isBackAction) {
            this.selectedIndex = this.history.length > 0
                ? this._firstSelectableIndex(this.items)
                : -1;
        }
        this.loadingIndex = -1;
        this.render();
        this.show();
    },
    _getDefaultItems() {
        const t = window.Atomix.t;
        return [
            { id: 'recent', name: `<span class="menu-icon">🕒</span> ${t('menu.recent', 'Recent')}`, hasChildren: true },
            { id: 'selection', name: `<span class="menu-icon">🔍</span> ${t('menu.selection', 'Selection')}`, hasChildren: true },
            { id: 'commands', name: `<span class="menu-icon">⚡</span> ${t('menu.commands', 'Commands')}`, hasChildren: true }
        ];
    },
    _withIconSpan(name) {
        const text = String(name || '');
        if (text.includes('class="menu-icon"')) return text;
        const match = text.match(/^(\p{Extended_Pictographic}(?:\uFE0F|\u200D\p{Extended_Pictographic})*)\s+([\s\S]*)$/u);
        if (!match) return text;
        return `<span class="menu-icon">${match[1]}</span> ${match[2]}`;
    },
    _plainName(name) {
        return String(name || '').replace(/<[^>]*>/g, '').replace(/\s+/g, ' ').trim();
    },
    _isGroup(item) {
        return !!(item && item.isGroup);
    },
    _nameMatches(item, q) {
        return this._plainName(item && item.name).toLowerCase().includes(q);
    },
    _firstSelectableIndex(list) {
        const items = list || [];
        for (let i = 0; i < items.length; i++) {
            if (!this._isGroup(items[i])) return i;
        }
        return -1;
    },
    _viewItems() {
        const q = (this.searchQuery || '').trim().toLowerCase();
        if (!q) return this.items;
        if (!this.items.some(item => this._isGroup(item))) {
            return this.items.filter(item => this._nameMatches(item, q));
        }
        const result = [];
        let i = 0;
        while (i < this.items.length) {
            const item = this.items[i];
            if (this._isGroup(item)) {
                const children = [];
                let j = i + 1;
                while (j < this.items.length && !this._isGroup(this.items[j])) {
                    children.push(this.items[j]);
                    j++;
                }
                const groupMatch = this._nameMatches(item, q);
                const matched = groupMatch
                    ? children
                    : children.filter(child => this._nameMatches(child, q));
                if (groupMatch || matched.length) {
                    result.push(item);
                    result.push(...matched);
                }
                i = j;
            } else {
                if (this._nameMatches(item, q)) result.push(item);
                i++;
            }
        }
        return result;
    },
    _needsSearch() {
        return this.items.length > this.VISIBLE_ROWS;
    },
    isSearchFocused() {
        const input = this.searchInput;
        return !!(input && document.activeElement === input);
    },
    _resetSearch() {
        this.searchQuery = '';
        if (this.searchInput) this.searchInput.value = '';
    },
    applySearch() {
        const input = this.searchInput;
        this.searchQuery = (input && input.value || '').trim();
        const view = this._viewItems();
        this.selectedIndex = this._firstSelectableIndex(view);
        this._renderItems();
        if (input) input.focus();
    },
    drillDown() {
        if (this.selectedIndex === -1) {
            this.goBack();
            return;
        }
        const item = this._viewItems()[this.selectedIndex];
        if (!item || this.loadingIndex !== -1 || this._isGroup(item)) return;

        if (item.hasChildren) {
            const fullIndex = this.items.indexOf(item);
            this.history.push({
                items: [...this.items],
                index: fullIndex >= 0 ? fullIndex : this.selectedIndex,
                title: this.currentTitle
            });
            this.currentTitle = this._plainName(item.name);
            this.loadingIndex = this.selectedIndex;
            this._renderItems();

            window.Atomix.RevitBridge.send('GET_SUB_CONTEXT', { id: item.id });
        } else {
            // МОДИФИКАЦИЯ: Если это конечный параметр — прячем меню и генерируем чипс
            if (item.meta) {
                window.Atomix.ChipsController.create(item);
            }
            this.hide();
        }
    },

    goBack() {
        if (this.history.length === 0) return;
        const prevState = this.history.pop();
        this.items = prevState.items;
        this.selectedIndex = prevState.index; // Возвращаемся на тот же пункт, с которого ушли
        this.currentTitle = prevState.title || 'BIM Context';
        this.loadingIndex = -1;
        this._resetSearch();
        this.render();
    },
    moveSelection(direction) {
        const view = this._viewItems();
        const hasHistory = this.history.length > 0;
        const minIndex = hasHistory ? -1 : 0; // -1 это Back
        const maxIndex = view.length - 1;
        if (maxIndex < 0 && !hasHistory) return;
        const start = this.selectedIndex;
        let idx = start;
        do {
            if (direction === 'down') {
                idx = (idx >= maxIndex) ? minIndex : idx + 1;
            } else {
                idx = (idx <= minIndex) ? maxIndex : idx - 1;
            }
            if (idx === -1 || !this._isGroup(view[idx])) break;
        } while (idx !== start);
        this.selectedIndex = idx;
        this._syncSelection();
    },
    render() {
        this._ensureSearchBound();
        this._renderBack();
        this._renderSearch();
        this._renderItems();
    },
    _renderBack() {
        if (!this.backHost) return;
        const hasHistory = this.history.length > 0;
        if (!hasHistory) {
            this.backHost.innerHTML = '';
            return;
        }
        const isHeaderSelected = this.selectedIndex === -1 ? 'selected' : '';
        this.backHost.innerHTML = `<div class="menu-back-row ${isHeaderSelected}" id="menu-header-back" title="${this.currentTitle}"><span class="menu-back">&#xE76B;</span><span class="menu-label">${window.Atomix.t('menu.back', 'Back')}</span><span class="menu-kbd">Esc</span></div>`;
        const header = document.getElementById('menu-header-back');
        if (header) {
            header.addEventListener('mousedown', (e) => {
                if (e.button !== 0) return;
                e.preventDefault();
                this.goBack();
                window.Atomix.EditorController.el.focus();
            });
        }
    },
    _renderSearch() {
        const search = this.searchEl;
        if (!search) return;
        const show = this._needsSearch();
        search.hidden = !show;
        if (show && this.searchInput && this.searchInput.value !== this.searchQuery) {
            this.searchInput.value = this.searchQuery;
        }
        if (window.Atomix.I18n && show) window.Atomix.I18n.apply(search);
    },
    _renderItems() {
        if (!this.listContainer) return;
        const view = this._viewItems();
        if (!view.length) {
            this.listContainer.innerHTML = `<div class="menu-empty">${window.Atomix.t('menu.noResults', 'No results')}</div>`;
        } else {
            this.listContainer.innerHTML = view.map((item, i) => {
                const isGroup = this._isGroup(item);
                const isSel = !isGroup && i === this.selectedIndex ? 'selected' : '';
                const isLoad = i === this.loadingIndex;
                let label = this._withIconSpan(item.name);
                if (isLoad) {
                    label = label.includes('class="menu-icon"')
                        ? label.replace(/<span class="menu-icon">[\s\S]*?<\/span>/, '<span class="menu-icon">⏳</span>') + '...'
                        : `<span class="menu-icon">⏳</span> ${label}...`;
                }
                const arrowIcon = (item.hasChildren && !isLoad && !isGroup) ? '<span class="menu-arrow">&#xE76C;</span>' : '';
                const groupClass = isGroup ? ' is-group' : '';
                return `<div class="menu-item ${isSel}${groupClass}" data-index="${i}"><span class="menu-item-text">${label}</span>${arrowIcon}</div>`;
            }).join('');
        }
        this.listContainer.classList.toggle('is-scrollable', this._needsSearch());
        this._attachItemEvents();
        this._syncSelection();
    },
    _syncSelection() {
        const header = document.getElementById('menu-header-back');
        if (header) header.classList.toggle('selected', this.selectedIndex === -1);
        if (!this.listContainer) return;
        this.listContainer.querySelectorAll('.menu-item').forEach((el) => {
            const i = parseInt(el.dataset.index, 10);
            el.classList.toggle('selected', i === this.selectedIndex);
        });
        const sel = this.listContainer.querySelector('.menu-item.selected');
        if (sel) sel.scrollIntoView({ block: 'nearest' });
    },
    _attachItemEvents() {
        if (!this.listContainer) return;
        Array.from(this.listContainer.querySelectorAll('.menu-item')).forEach(el => {
            if (el.classList.contains('is-group')) return;
            el.addEventListener('mousedown', (e) => {
                if (e.button !== 0) return;
                e.preventDefault();
                this.selectedIndex = parseInt(el.dataset.index, 10);
                this.drillDown();
                window.Atomix.EditorController.el.focus();
            });
        });
    },
    _ensureSearchBound() {
        if (this._searchBound) return;
        const input = this.searchInput;
        const btn = this.searchBtn;
        if (!input || !btn) return;
        this._searchBound = true;
        input.addEventListener('keydown', (e) => {
            if (e.key !== 'Enter') return;
            e.preventDefault();
            e.stopPropagation();
            this.applySearch();
        });
        btn.addEventListener('mousedown', (e) => {
            e.preventDefault();
        });
        btn.addEventListener('click', (e) => {
            e.preventDefault();
            this.applySearch();
        });
    },
    show() {
        this.el.classList.remove('menu-hidden');
        this.el.style.display = 'block';
        this.isOpen = true;
        // Мгновенно фиксируем высоту после отрисовки первого уровня
        if (this.history.length === 0) {
            this.el.style.minHeight = '0px'; // Сброс для замера
            const currentHeight = this.el.scrollHeight;
            this.el.style.minHeight = `${currentHeight}px`;
        }
    },
    hide() {
        this.el.classList.add('menu-hidden');
        this.el.style.display = 'none';
        this.isOpen = false;
        this.el.style.minHeight = '0px'; // Полный сброс
        this.history = [];
        this._resetSearch();
        if (this.searchEl) this.searchEl.hidden = true;
    },
    _mockFetch(id) {
        setTimeout(() => {
            if (this.loadingIndex === -1 && !this.isOpen) return;
            const mocks = {
                'selection': [{ id: 'w1', name: 'Wall 1', hasChildren: true }, { id: 'w2', name: 'Wall 2', hasChildren: false }],
                'recent': [{ id: 'p1', name: 'Comments', hasChildren: false }],
                'w1': [{ id: 'p_mark', name: 'Mark', hasChildren: false }, { id: 'p_area', name: 'Area', hasChildren: false }]
            };
            if (mocks[id]) this.update(mocks[id]);
        }, 400);
    }
};
