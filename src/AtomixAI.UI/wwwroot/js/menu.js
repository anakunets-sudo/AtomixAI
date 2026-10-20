// menu.js
window.Atomix = window.Atomix || {};
window.Atomix.MenuController = {
    VISIBLE_ROWS: 4,
    get el() { return document.getElementById('ai-menu'); },
    get backHost() { return document.getElementById('menu-back-host'); },
    get searchEl() { return document.getElementById('menu-search'); },
    get searchInput() { return document.getElementById('menu-search-input'); },
    get searchBtn() { return document.getElementById('menu-search-btn'); },
    get pinnedActionHost() { return document.getElementById('menu-pinned-action'); },
    get listContainer() { return document.getElementById('menu-items'); },
    items: [],
    history: [], // Стек состояний: [{ items: [], index: 0, title: '' }]
    selectedIndex: -1,
    loadingIndex: -1,
    isOpen: false,
    currentTitle: 'BIM Context',
    initialHeight: 0,
    searchQuery: '',
    collapsedGroups: {},
    _searchBound: false,
    chipRequestSequence: 0,
    pendingChipRequestId: null,
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
        if (this.items.some(item => this._isCollapsibleGroup(item))) {
            this.collapsedGroups = {};
        }
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
    _isCollapsibleGroup(item) {
        return !!(item && item.isCollapsibleGroup);
    },
    _nameMatches(item, q) {
        return this._plainName(item && item.name).toLowerCase().includes(q);
    },
    _firstSelectableIndex(list) {
        const items = list || [];
        for (let i = 0; i < items.length; i++) {
            if (!items[i].isPinnedAction && (!this._isGroup(items[i]) || this._isCollapsibleGroup(items[i]))) return i;
        }
        for (let i = 0; i < items.length; i++) {
            if (items[i].isPinnedAction) return i;
        }
        return -1;
    },
    _toggleCollapsibleGroup(group) {
        const currentView = this._viewItems();
        const selectedItem = currentView[this.selectedIndex];
        this.collapsedGroups[group.id] = !this.collapsedGroups[group.id];

        const nextView = this._viewItems();
        const selectedIndex = nextView.indexOf(selectedItem);
        this.selectedIndex = selectedIndex >= 0 ? selectedIndex : nextView.indexOf(group);
        this._renderItems();
    },
    _viewItems() {
        const q = (this.searchQuery || '').trim().toLowerCase();
        if (!this.items.some(item => this._isGroup(item))) {
            return q ? this.items.filter(item => item.isPinnedAction || this._nameMatches(item, q)) : this.items;
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
                const isCollapsed = this._isCollapsibleGroup(item) && this.collapsedGroups[item.id];
                if (!q || groupMatch || matched.length) {
                    result.push(item);
                    if (q || !isCollapsed) result.push(...(q ? matched : children));
                }
                i = j;
            } else {
                if (item.isPinnedAction || !q || this._nameMatches(item, q)) result.push(item);
                i++;
            }
        }
        return result;
    },
    _needsSearch() {
        return this.items.filter(item => !item.isPinnedAction).length > this.VISIBLE_ROWS;
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
            return false;
        }
        const item = this._viewItems()[this.selectedIndex];
        if (!item || this.loadingIndex !== -1) return false;
        if (this._isCollapsibleGroup(item)) {
            this._toggleCollapsibleGroup(item);
            return false;
        }
        if (this._isGroup(item)) return false;

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
            return false;
        }

        let inserted = false;
        if (item.insertAlias) {
            window.Atomix.ChipsController.insertAlias(item.insertAlias);
            inserted = true;
        } else if (item.isParameterChip) {
            if (this.pendingChipRequestId) return false;
            const requestId = `chip-request-${++this.chipRequestSequence}`;
            this.pendingChipRequestId = requestId;
            this.loadingIndex = this.selectedIndex;
            this._renderItems();
            window.Atomix.RevitBridge.send('CREATE_CHIP', { id: item.id, requestId });
            return false;
        } else if (item.chipPayload) {
            window.Atomix.ChipsController.insertChip(item.chipPayload);
            inserted = true;
        }
        this.hide();
        // true = тег/чипс уже сам вернул фокус и поставил каретку за пробелом,
        // повторный focus() в _attachItemEvents только сбросил бы каретку.
        return inserted;
    },
    completeParameterChip(data) {
        if (!data.requestId || data.requestId !== this.pendingChipRequestId) return;
        this.pendingChipRequestId = null;
        if (data.action === 'CHIP_CREATED' && data.chipPayload) {
            window.Atomix.ChipsController.insertChip(data.chipPayload);
            this.hide();
        } else if (data.action === 'CHIP_CREATE_ERROR') {
            this.loadingIndex = -1;
            this._renderItems();
            if (typeof appendMessage === 'function') appendMessage('ai', data.message || 'Unable to create the selected chip.');
        }
    },

    goBack() {
        if (this.history.length === 0) return;
        if (this.items.some(item => String(item.id || '').startsWith('el_'))) {
            this.history = [];
            this.update();
            return;
        }
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
            if (idx === -1 || !this._isGroup(view[idx]) || this._isCollapsibleGroup(view[idx])) break;
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
            this.backHost.innerHTML = `<div class="menu-back-row menu-close-row" id="menu-header-close"><span class="menu-back">&#xE70D;</span><span class="menu-label">${window.Atomix.t('menu.close', 'Close')}</span><span class="menu-kbd">Esc</span></div>`;
            const close = document.getElementById('menu-header-close');
            if (close) {
                close.addEventListener('mousedown', (e) => {
                    if (e.button !== 0) return;
                    e.preventDefault();
                    this.hide();
                    window.Atomix.EditorController.el.focus();
                });
            }
            return;
        }
        const isHeaderSelected = this.selectedIndex === -1 ? 'selected' : '';
        this.backHost.innerHTML = `<div class="menu-back-row ${isHeaderSelected}" id="menu-header-back"><span class="menu-back">&#xE76B;</span><span class="menu-label">${window.Atomix.t('menu.back', 'Back')}</span><span class="menu-kbd">Esc</span></div>`;
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
        const pinnedIndex = view.findIndex(item => item.isPinnedAction);
        const pinnedAction = pinnedIndex >= 0 ? view[pinnedIndex] : null;
        if (this.pinnedActionHost) {
            this.pinnedActionHost.innerHTML = pinnedAction
                ? `<div class="menu-back-row menu-pick-action ${pinnedIndex === this.selectedIndex ? 'selected' : ''}" data-index="${pinnedIndex}" role="button" tabindex="0"><span class="menu-label">${this._withIconSpan(pinnedAction.name)}</span><span class="menu-arrow">&#xE76C;</span></div>`
                : '';
        }
        const listView = view.filter(item => !item.isPinnedAction);
        if (!listView.length) {
            const hasSearchableItems = this.items.some(item => !item.isPinnedAction);
            this.listContainer.innerHTML = this.searchQuery && hasSearchableItems
                ? `<div class="menu-empty">${window.Atomix.t('menu.noResults', 'No results')}</div>`
                : '';
        } else {
            let inCollapsibleGroup = false;
            this.listContainer.innerHTML = listView.map((item) => {
                const i = view.indexOf(item);
                const isGroup = this._isGroup(item);
                const isCollapsibleGroup = this._isCollapsibleGroup(item);
                if (isGroup) inCollapsibleGroup = isCollapsibleGroup;
                const isGroupChild = !isGroup && inCollapsibleGroup;
                const isSel = (!isGroup || isCollapsibleGroup) && i === this.selectedIndex ? 'selected' : '';
                const isLoad = i === this.loadingIndex;
                const menuChip = item.chipPayload && window.Atomix.ChipsController.createElement(item.chipPayload);
                let label = menuChip ? menuChip.outerHTML : this._withIconSpan(item.name);
                if (isLoad) {
                    label = label.includes('class="menu-icon"')
                        ? label.replace(/<span class="menu-icon">[\s\S]*?<\/span>/, '<span class="menu-icon">⏳</span>') + '...'
                        : `<span class="menu-icon">⏳</span> ${label}...`;
                }
                const arrowIcon = (item.hasChildren && !isLoad && !isGroup) ? '<span class="menu-arrow">&#xE76C;</span>' : '';
                const groupClass = isGroup ? ' is-group' : '';
                const collapsibleClass = isCollapsibleGroup ? ' is-collapsible-group' : '';
                const childClass = isGroupChild ? ' is-group-child' : '';
                const modelDataClass = menuChip ? ' menu-model-data' : '';
                const collapsed = isCollapsibleGroup && this.collapsedGroups[item.id];
                const groupArrow = isCollapsibleGroup
                    ? `<span class="menu-group-arrow">${collapsed ? '&#xE76C;' : '&#xE70D;'}</span>`
                    : '';
                const expandedAttribute = isCollapsibleGroup ? ` aria-expanded="${!collapsed}" role="button" tabindex="0"` : '';
                return `<div class="menu-item ${isSel}${groupClass}${collapsibleClass}${childClass} ${modelDataClass}" data-index="${i}"${expandedAttribute}><span class="menu-item-text">${label}</span>${groupArrow}${arrowIcon}</div>`;
            }).join('');
        }
        this.listContainer.classList.toggle('is-scrollable', this._needsSearch());
        this._attachItemEvents();
        this._syncSelection();
    },
    _syncSelection() {
        const header = document.getElementById('menu-header-back');
        if (header) header.classList.toggle('selected', this.selectedIndex === -1);
        const pinnedAction = this.pinnedActionHost && this.pinnedActionHost.querySelector('.menu-pick-action');
        if (pinnedAction) pinnedAction.classList.toggle('selected', parseInt(pinnedAction.dataset.index, 10) === this.selectedIndex);
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
        const attach = el => {
            if (el.classList.contains('is-collapsible-group')) {
                el.addEventListener('mousedown', (e) => {
                    if (e.button !== 0) return;
                    e.preventDefault();
                    const index = parseInt(el.dataset.index, 10);
                    const group = this._viewItems()[index];
                    if (!group) return;
                    this._toggleCollapsibleGroup(group);
                });
                return;
            }
            if (el.classList.contains('is-group') && !el.classList.contains('is-collapsible-group')) return;
            el.addEventListener('mousedown', (e) => {
                if (e.button !== 0) return;
                e.preventDefault();
                this.selectedIndex = parseInt(el.dataset.index, 10);
                // insertAlias/create сами возвращают фокус в поле и ставят курсор за
                // пробелом; повторный focus() сбросил бы каретку в начало строки.
                if (!this.drillDown()) window.Atomix.EditorController.el.focus();
            });
        };
        Array.from(this.listContainer.querySelectorAll('.menu-item')).forEach(attach);
        if (this.pinnedActionHost) {
            const pinnedAction = this.pinnedActionHost.querySelector('.menu-pick-action');
            if (pinnedAction) attach(pinnedAction);
        }
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
