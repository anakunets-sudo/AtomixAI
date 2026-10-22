// chips.js
window.Atomix = window.Atomix || {};
window.Atomix.ChipsController = {
    _aliasActionMenu: null,
    _activeAlias: null,

    _selectActionLabel() {
        return window.Atomix.t('alias.select', 'Select');
    },

    initAliasActionMenu() {
        const input = window.Atomix.EditorController.el;
        if (!input || this._aliasActionMenu) return;

        const menu = document.createElement('div');
        menu.className = 'alias-action-menu';
        menu.setAttribute('role', 'menu');
        menu.hidden = true;

        const selectAction = document.createElement('button');
        selectAction.type = 'button';
        selectAction.className = 'alias-action-item';
        selectAction.setAttribute('role', 'menuitem');
        selectAction.textContent = this._selectActionLabel();
        selectAction.addEventListener('mousedown', event => event.preventDefault());
        selectAction.addEventListener('click', () => {
            this.insertSelectTextAfterAlias(this._activeAlias);
            this._hideAliasActionMenu();
        });

        menu.appendChild(selectAction);
        document.body.appendChild(menu);
        this._aliasActionMenu = menu;

        input.addEventListener('mousedown', event => {
            if (event.button !== 0) return;
            const target = event.target;
            const alias = target && target.closest ? target.closest('.alias-btn') : null;
            if (!alias || !input.contains(alias)) {
                this._hideAliasActionMenu();
                return;
            }

            event.preventDefault();
            this._activeAlias = alias;
            this._showAliasActionMenu(alias);
        });

        document.addEventListener('mousedown', event => {
            if (!menu.contains(event.target) && !this._activeAlias?.contains(event.target)) {
                this._hideAliasActionMenu();
            }
        });
        document.addEventListener('keydown', event => {
            if (!menu.hidden && event.key === 'Escape') {
                this._hideAliasActionMenu();
            }
        });
    },

    _showAliasActionMenu(alias) {
        const menu = this._aliasActionMenu;
        if (!menu) return;

        menu.hidden = false;
        const rect = alias.getBoundingClientRect();
        const menuRect = menu.getBoundingClientRect();
        const left = Math.max(8, Math.min(rect.left, window.innerWidth - menuRect.width - 8));
        const top = Math.max(8, rect.top - menuRect.height - 6);
        menu.style.left = `${left}px`;
        menu.style.top = `${top}px`;
    },

    _hideAliasActionMenu() {
        if (this._aliasActionMenu) this._aliasActionMenu.hidden = true;
        this._activeAlias = null;
    },

    insertSelectTextAfterAlias(alias) {
        const input = window.Atomix.EditorController.el;
        if (!input || !alias || !input.contains(alias)) return;

        input.focus();
        const next = alias.nextSibling;
        const range = document.createRange();
        const command = this._selectActionLabel();
        let commandOffset;
        let insertedText;

        if (next && next.nodeType === Node.TEXT_NODE) {
            const remaining = next.textContent;
            const whitespace = /^\s*/.exec(remaining)[0].length;
            const textAfterWhitespace = remaining.slice(whitespace);
            range.setStart(next, whitespace);
            range.collapse(true);
            const prefix = whitespace ? '' : '\u00A0';
            insertedText = document.createTextNode(`${prefix}${command}${/^\s/.test(textAfterWhitespace) ? '' : '\u00A0'}`);
            commandOffset = prefix.length + command.length;
        } else {
            range.setStartAfter(alias);
            range.collapse(true);
            insertedText = document.createTextNode(`\u00A0${command}\u00A0`);
            commandOffset = command.length + 1;
        }

        range.insertNode(insertedText);
        const caret = document.createRange();
        caret.setStart(insertedText, commandOffset);
        caret.collapse(true);
        const selection = window.getSelection();
        selection.removeAllRanges();
        selection.addRange(caret);
        input.dispatchEvent(new Event('input', { bubbles: true }));
    },

    insertAlias(alias) {
        const input = window.Atomix.EditorController.el;
        if (!input || !alias) return;

        // Тег — это span.alias-btn. Меню Recent ▸ Tags (menu.js) и клик по тегу
        // в сообщении ИИ (main.js) зовут один и тот же метод, поэтому вставка
        // всегда идёт по одному сценарию.
        const aliasElement = document.createElement('span');
        aliasElement.className = 'alias-btn';
        aliasElement.contentEditable = 'false';
        aliasElement.dataset.alias = alias;
        aliasElement.textContent = alias;

        this._insertTag(aliasElement, '\u00A0');
    },

    insertChip(chip) {
        const input = window.Atomix.EditorController.el;
        if (!input || !chip) return;

        // Чипсы, которые попадают в поле ввода, редактируемые. Тултип остаётся
        // исходным, поэтому при копировании из сообщения переносим сохранённый оригинал.
        const originalTooltip = chip instanceof HTMLElement ? chip._originalTooltip : undefined;
        const payload = this.getPayload(chip);
        const element = payload
            ? this.createElement(payload, { editable: true, originalTooltip })
            : chip instanceof HTMLElement && chip.classList.contains('bim-chip')
                ? chip.cloneNode(true)
                : null;
        if (element) {
            element.contentEditable = 'false';
            this._insertTag(element, '\u00A0');
        }
    },

    getPayload(item) {
        const source = item instanceof HTMLElement ? item._chipPayload : item?.chipPayload || item;
        if (!source || typeof source.chipId !== 'string' || !source.chipId ||
            typeof source.chipLabel !== 'string' || !source.chipData ||
            typeof source.chipData !== 'object' || Array.isArray(source.chipData)) return null;
        return {
            chipId: source.chipId,
            chipLabel: source.chipLabel,
            chipData: source.chipData
        };
    },

    createElement(item, options = {}) {
        const payload = this.getPayload(item);
        if (!payload) return null;
        const chip = document.createElement('span');
        chip.className = 'bim-chip';
        chip.contentEditable = 'false';
        chip.setAttribute('data-chip-id', payload.chipId);

        // Тултип всегда показывает исходное значение чипса и не меняется после правки.
        const tooltip = options.originalTooltip || this.formatTooltip(payload.chipData);
        chip.setAttribute('data-tooltip', tooltip);
        chip._chipPayload = payload;
        chip._originalTooltip = tooltip;

        const label = document.createElement('span');
        label.className = 'chip-label';
        label.textContent = payload.chipLabel;
        chip.appendChild(label);

        if (options.editable) {
            chip.classList.add('chip-editable');
            chip.appendChild(this._createEditAffordance());
            this._attachChipEditTrigger(chip);
        }
        return chip;
    },

    // Клик по любому месту чипса (при наличии карандаша) входит в режим правки,
    // а не только клик по самому карандашу.
    _attachChipEditTrigger(chip) {
        if (!chip || chip._editTriggerBound) return;
        chip._editTriggerBound = true;
        chip.addEventListener('mousedown', (e) => {
            if (chip._editing) return;
            const target = e.target;
            if (target && target.classList && target.classList.contains('chip-edit-input')) return;
            e.preventDefault();
            e.stopPropagation();
            this.beginEdit(chip);
        });
    },

    _createEditAffordance() {
        const edit = document.createElement('span');
        edit.className = 'chip-edit';
        edit.contentEditable = 'false';
        edit.setAttribute('role', 'button');
        edit.setAttribute('aria-label', 'Edit value');
        edit.innerHTML = '<span class="chip-edit-text"></span><svg viewBox="0 0 24 24" width="12" height="12" aria-hidden="true" focusable="false"><path fill="currentColor" d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04a1 1 0 0 0 0-1.41l-2.34-2.34a1 1 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z"/></svg>';
        edit.querySelector('.chip-edit-text').textContent = window.Atomix?.t
            ? window.Atomix.t('chip.edit', 'Edit')
            : 'Edit';
        edit.addEventListener('mousedown', (e) => {
            e.preventDefault();
            e.stopPropagation();
            this.beginEdit(edit.parentElement);
        });
        return edit;
    },

    // Кнопка подтверждения правки: появляется вместо карандаша в режиме редактирования.
    _createConfirmAffordance(chip) {
        const confirm = document.createElement('span');
        confirm.className = 'chip-confirm';
        confirm.contentEditable = 'false';
        confirm.setAttribute('role', 'button');
        confirm.setAttribute('aria-label', 'Confirm value');
        confirm.innerHTML = '<svg aria-hidden="true" focusable="false" viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12 4 4L19 6"/></svg>';
        confirm.addEventListener('mousedown', (e) => {
            e.preventDefault();
            e.stopPropagation();
            const input = chip._editInput;
            this.finishEdit(chip, input ? input.value : '');
        });
        return confirm;
    },

    // Имя параметра для подписи. Берём из данных, с откатом на разбор текущей подписи.
    _chipName(payload) {
        const fromData = payload && payload.chipData && payload.chipData.parameter_Name;
        if (fromData) return String(fromData);
        const label = (payload && payload.chipLabel) || '';
        const match = /^#([^:]+?)(?::\s.*)?$/.exec(label);
        return match ? match[1].trim() : label.replace(/^#/, '');
    },

    // Вход в режим правки: значение становится редактируемым прямо в пилле.
    beginEdit(chip) {
        if (!chip || !chip.classList.contains('bim-chip') || chip._editing) return;
        const payload = chip._chipPayload;
        const label = chip.querySelector('.chip-label');
        if (!payload || !label) return;

        const name = this._chipName(payload);
        const current = chip._confirmedValue !== undefined
            ? chip._confirmedValue
            : (payload.chipData && payload.chipData.parameter_Value != null
                ? String(payload.chipData.parameter_Value) : '');

        chip._editing = true;
        chip.classList.add('chip-editing');
        label.textContent = '#' + name + ': ';

        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'chip-edit-input';
        input.value = current;
        input.setAttribute('spellcheck', 'false');
        input.addEventListener('keydown', (e) => {
            // Не даём Enter/Escape всплыть до поля ввода (иначе уйдёт сообщение или сработает меню).
            e.stopPropagation();
            if (e.key === 'Enter') {
                e.preventDefault();
                this.finishEdit(chip, input.value);
            } else if (e.key === 'Escape') {
                e.preventDefault();
                this.cancelEdit(chip);
            }
        });
        input.addEventListener('mousedown', (e) => e.stopPropagation());
        input.addEventListener('blur', () => {
            if (chip._editing) this.finishEdit(chip, input.value);
        });

        label.appendChild(input);
        chip._editInput = input;

        const confirm = this._createConfirmAffordance(chip);
        chip.appendChild(confirm);
        chip._confirmEl = confirm;

        input.focus();
        input.select();
    },

    // Подтверждение правки: единственный источник правды — введённое значение.
    finishEdit(chip, rawValue) {
        if (!chip || !chip._editing) return;
        const payload = chip._chipPayload;
        const value = String(rawValue == null ? '' : rawValue).trim();
        const name = this._chipName(payload);
        const label = chip.querySelector('.chip-label');

        chip._editing = false;
        chip._editInput = null;
        if (chip._confirmEl) { chip._confirmEl.remove(); chip._confirmEl = null; }
        chip.classList.remove('chip-editing');
        if (label) label.textContent = value ? `#${name}: ${value}` : `#${name}`;

        chip._confirmedValue = value;
        const original = payload && payload.chipData && payload.chipData.parameter_Value != null
            ? String(payload.chipData.parameter_Value) : '';
        chip.classList.toggle('chip-edited', value !== original);

        if (payload) {
            payload.chipLabel = label ? label.textContent : payload.chipLabel;
            if (payload.chipData) payload.chipData.parameter_Value = value;
        }

        // Сообщаем C#: снапшот для ИИ должен содержать только подтверждённое значение.
        if (payload && payload.chipId && window.Atomix.RevitBridge) {
            window.Atomix.RevitBridge.send('update_chip', { id: payload.chipId, value });
        }
        this._refocusInput();
    },

    cancelEdit(chip) {
        if (!chip || !chip._editing) return;
        const payload = chip._chipPayload;
        chip._editing = false;
        chip._editInput = null;
        if (chip._confirmEl) { chip._confirmEl.remove(); chip._confirmEl = null; }
        chip.classList.remove('chip-editing');
        const label = chip.querySelector('.chip-label');
        if (label) label.textContent = payload ? payload.chipLabel : '';
        this._refocusInput();
    },

    _refocusInput() {
        const input = window.Atomix.EditorController && window.Atomix.EditorController.el;
        if (input) input.focus();
    },

    formatTooltip(chipData) {
        if (!chipData || typeof chipData !== 'object') return '';
        const value = key => {
            const raw = chipData[key];
            return raw == null ? '' : String(raw);
        };
        if (value('kind') !== 'revit_parameter') return '';

        const parts = [];
        const parameterName = value('parameter_Name');
        const parameterValue = value('parameter_Value');
        const unitLabel = value('parameter_Unit_Label');

        if (parameterName) {
            parts.push(chipData.is_Type ? 'Type Parameter' : 'Instance Parameter');
            if (parameterValue) {
                parts.push(`Parameter: ${parameterName} = ${parameterValue}` + (unitLabel ? ` ${unitLabel}` : ''));
            }
        }
        if (value('element_Name')) parts.push(`Element: ${value('element_Name')}`);
        const category = value('element_Сategory') || value('element_Category');
        if (category) parts.push(`Category: ${category}`);
        if (value('element_Id')) parts.push(`Id: ${value('element_Id')}`);
        return parts.join('\n');
    },

    restoreMessageTooltip(element, chipsById, options = {}) {
        if (!element || element.tagName !== 'SPAN' || !chipsById) return false;
        const entry = chipsById.get(element.getAttribute('data-chip-id'));
        const payload = this.getPayload(entry);
        if (!payload) return false;
        element.classList.add('bim-chip');
        element.contentEditable = 'false';
        // В отправленных сообщениях показываем исходное значение, а не подтверждённое.
        const tooltip = (entry && entry.originalTooltip) || this.formatTooltip(payload.chipData);
        element.setAttribute('data-tooltip', tooltip);
        element._chipPayload = payload;
        element._originalTooltip = tooltip;
        if (options.editable) {
            this._ensureEditableStructure(element);
        } else {
            element.classList.remove('chip-editable');
            const stale = element.querySelector('.chip-edit');
            if (stale) stale.remove();
        }
        return true;
    },

    // Приводит чипс в поле ввода к структуре с подписью и кнопкой правки.
    _ensureEditableStructure(chip) {
        let label = chip.querySelector('.chip-label');
        if (!label) {
            label = document.createElement('span');
            label.className = 'chip-label';
            while (chip.firstChild) label.appendChild(chip.firstChild);
            chip.appendChild(label);
        }
        chip.classList.add('chip-editable');
        const stale = chip.querySelector('.chip-edit');
        if (stale) stale.remove();
        chip.appendChild(this._createEditAffordance());
        this._attachChipEditTrigger(chip);
    },

    // Вставляет тег на место каретки (или в конец поля) и гарантирует, что СНАРУЖИ
    // span стоит ровно один пробел, а курсор — сразу за этим пробелом.
    _insertTag(element, spaceChar = ' ') {
        const input = window.Atomix.EditorController.el;
        if (!input || !element) return;

        // Каретку снимаем ДО focus(): в WebView2 переключение фокуса сбрасывает
        // выделение contenteditable в начало, и тег улетал бы в начало строки.
        const range = this._captureCaretIn(input) || this._caretAtEnd(input);
        input.focus();

        range.deleteContents();
        this._removeTriggerBeforeCaret(input, range);
        range.insertNode(element);

        const space = this._ensureTrailingSpace(element, spaceChar);

        // Курсор — строго за пробелом, уже вне span.
        const caret = document.createRange();
        caret.setStart(space, spaceChar.length);
        caret.collapse(true);
        const selection = window.getSelection();
        selection.removeAllRanges();
        selection.addRange(caret);

        input.dispatchEvent(new Event('input', { bubbles: true }));
    },

    // Текущий Range, только если он внутри поля ввода.
    _captureCaretIn(input) {
        const selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) return null;
        const range = selection.getRangeAt(0);
        return input.contains(range.startContainer) ? range : null;
    },

    _caretAtEnd(input) {
        const range = document.createRange();
        range.selectNodeContents(input);
        range.collapse(false);
        return range;
    },

    // Ровно один пробел сразу после тега, вне span. Повторная вставка не задваивает его.
    _ensureTrailingSpace(element, spaceChar) {
        // Range.insertNode расщепляет текстовый узел и оставляет после тега пустой
        // узел — убираем его, чтобы при повторных вставках не копился мусор.
        let next = element.nextSibling;
        while (next && next.nodeType === Node.TEXT_NODE && next.textContent.length === 0) {
            const empty = next;
            next = next.nextSibling;
            empty.remove();
        }
        if (next && next.nodeType === Node.TEXT_NODE && next.textContent.startsWith(spaceChar)) {
            return next;
        }
        const space = document.createTextNode(spaceChar);
        element.after(space);
        return space;
    },

    serialize(input) {
        const chipIds = new Set();
        const walk = node => {
            if (node.nodeType === Node.TEXT_NODE) return node.textContent;
            if (node.nodeType !== Node.ELEMENT_NODE) return '';
            if (node.classList.contains('bim-chip')) {
                const id = node.getAttribute('data-chip-id');
                if (!/^chip_[1-9]\d*$/.test(id || '')) {
                    throw new Error('Stale chip. Remove it and select it again from the menu.');
                }
                chipIds.add(id);
                return `[${id}]`;
            }
            if (node.tagName === 'BR') return '\n';
            let text = '';
            for (const child of node.childNodes) {
                const block = child.nodeType === Node.ELEMENT_NODE && /^(DIV|P|LI)$/.test(child.tagName);
                if (block && text && !text.endsWith('\n')) text += '\n';
                text += walk(child);
                if (block && !text.endsWith('\n')) text += '\n';
            }
            return text;
        };
        const prompt = walk(input).trim();
        return { prompt, chipIds: Array.from(chipIds) };
    },

    _triggerChar() {
        return (window.Atomix.EditorController && window.Atomix.EditorController.triggerChar) || '#';
    },

    _deleteTriggerAtEnd(textNode) {
        const trigger = this._triggerChar();
        if (!textNode || textNode.nodeType !== Node.TEXT_NODE) return false;
        const text = textNode.textContent;
        if (!text.endsWith(trigger)) return false;
        textNode.deleteData(text.length - 1, 1);
        if (textNode.textContent.length === 0) textNode.remove();
        return true;
    },

    _removeTriggerBeforeCaret(input, range) {
        const trigger = this._triggerChar();
        const container = range.startContainer;
        const offset = range.startOffset;

        if (container.nodeType === Node.TEXT_NODE) {
            if (offset > 0 && container.textContent.charAt(offset - 1) === trigger) {
                container.deleteData(offset - 1, 1);
                if (container.textContent.length === 0) {
                    const parent = container.parentNode || input;
                    const index = Array.prototype.indexOf.call(parent.childNodes, container);
                    container.remove();
                    range.setStart(parent, Math.max(0, index));
                } else {
                    range.setStart(container, offset - 1);
                }
                range.collapse(true);
                return;
            }
            if (offset === 0) this._deleteTriggerAtEnd(container.previousSibling);
            return;
        }

        if (container.nodeType === Node.ELEMENT_NODE && offset > 0) {
            this._deleteTriggerAtEnd(container.childNodes[offset - 1]);
        }
    }
};
