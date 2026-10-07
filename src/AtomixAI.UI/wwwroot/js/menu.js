// menu.js
window.Atomix = window.Atomix || {};
window.Atomix.MenuController = {
    get el() { return document.getElementById('ai-menu'); },
    get listContainer() { return document.getElementById('menu-items'); },
    items: [],
    history: [], // Стек состояний: [{ items: [], index: 0, title: '' }]
    selectedIndex: 0,
    loadingIndex: -1,
    isOpen: false,
    currentTitle: 'BIM Context',
    initialHeight: 0,
    update(newItems, isBackAction = false, title = null) {
        // Если мы вернулись на главный уровень (Level 0), можно сбросить min-height
        // или оставить зафиксированной до закрытия меню #.
        if (!isBackAction && this.history.length === 0) {
            this.history = [];
            this.currentTitle = 'BIM Context';
            // Не сбрасываем minHeight здесь, чтобы не было прыжка при очистке списка
        }
        if (title) this.currentTitle = title;
        this.items = (newItems && newItems.length) ? newItems : this._getDefaultItems();
        // При переходе вглубь сбрасываем на 0 (первый элемент списка)
        if (!isBackAction) this.selectedIndex = 0;
        this.loadingIndex = -1;
        this.render();
        this.show();
    },
    _getDefaultItems() {
        return [
            { id: 'recent', name: '🕒 Recent', hasChildren: true },
            { id: 'selection', name: '🔍 Selection', hasChildren: true },
            { id: 'commands', name: '⚡ Commands', hasChildren: true }
        ];
    },
    _splitLeadingIcon(name) {
        const text = String(name || '');
        const match = text.match(/^(\p{Extended_Pictographic}(?:\uFE0F|\u200D\p{Extended_Pictographic})*)\s+([\s\S]*)$/u);
        if (!match) return { icon: '', label: text };
        return { icon: match[1], label: match[2] };
    },
    drillDown() {
        if (this.selectedIndex === -1) {
            this.goBack();
            return;
        }
        const item = this.items[this.selectedIndex];
        if (!item || this.loadingIndex !== -1) return;

        if (item.hasChildren) {
            this.history.push({ items: [...this.items], index: this.selectedIndex, title: this.currentTitle });
            this.currentTitle = item.name;
            this.loadingIndex = this.selectedIndex;
            this.render();

            window.Atomix.RevitBridge.send('GET_SUB_CONTEXT', { id: item.id });
        } else {
            // МОДИФИКАЦИЯ: Если это конечный параметр — прячем меню и генерируем чипс            
            if (item.meta) {
                window.Atomix.ChipsController.create(item);
            } else if (item.id === "action_pick_objects") {
                // Задел под интерактивный выбор элементов в Revit, если selection был пуст
                window.Atomix.RevitBridge.send('PICK_ELEMENTS_REQUEST');
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
        this.render();
    },
    moveSelection(direction) {
        const hasHistory = this.history.length > 0;
        const minIndex = hasHistory ? -1 : 0; // -1 это заголовок
        const maxIndex = this.items.length - 1;
        if (direction === 'down') {
            this.selectedIndex = (this.selectedIndex >= maxIndex) ? minIndex : this.selectedIndex + 1;
        } else {
            this.selectedIndex = (this.selectedIndex <= minIndex) ? maxIndex : this.selectedIndex - 1;
        }
        this.render();
    },
    render() {
        if (!this.listContainer) return;
        let html = "";
        const hasHistory = this.history.length > 0;
        // Логика подсветки заголовка
        const isHeaderSelected = (this.selectedIndex === -1 && hasHistory) ? 'selected' : '';
        const arrow = hasHistory ? '← ' : '';
        html += `<div class="menu-header ${hasHistory ? 'can-go-back' : ''} ${isHeaderSelected}" id="menu-header-back"> ${arrow}${this.currentTitle} </div>`;
        // Список элементов
        html += this.items.map((item, i) => {
            const isSel = i === this.selectedIndex ? 'selected' : '';
            const isLoad = i === this.loadingIndex;
            const parts = this._splitLeadingIcon(item.name);
            const icon = isLoad ? '⏳' : parts.icon;
            const label = isLoad ? `${parts.label}...` : parts.label;
            const iconHtml = icon ? `<span class="menu-icon">${icon}</span>` : '';
            const arrowIcon = (item.hasChildren && !isLoad) ? '<span class="menu-arrow">→</span>' : '';
            return `<div class="menu-item ${isSel}" data-index="${i}">${iconHtml}<span class="menu-label">${label}</span>${arrowIcon}</div>`;
        }).join('');
        this.listContainer.innerHTML = html;
        this._attachEvents();
    },
    _attachEvents() {
        // Клик по заголовку
        const header = document.getElementById('menu-header-back');
        if (header && this.history.length > 0) {
            header.addEventListener('mousedown', (e) => {
                if (e.button !== 0) return;
                e.preventDefault();
                this.goBack();
                window.Atomix.EditorController.el.focus();
            });
        }
        // Клики по пунктам
        Array.from(this.listContainer.querySelectorAll('.menu-item')).forEach(el => {
            el.addEventListener('mousedown', (e) => {
                if (e.button !== 0) return;
                e.preventDefault();
                this.selectedIndex = parseInt(el.dataset.index);
                this.drillDown();
                window.Atomix.EditorController.el.focus();
            });
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

