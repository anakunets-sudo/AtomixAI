// chips.js
window.Atomix = window.Atomix || {};
window.Atomix.ChipsController = {
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

        const element = chip instanceof HTMLElement
            ? (chip.classList.contains('bim-chip') ? chip.cloneNode(true) : null)
            : this.createElement(chip);
        if (element) {
            element.contentEditable = 'false';
            this._insertTag(element, '\u00A0');
        }
    },

    createElement(item) {
        if (!item || !item.chipId) return null;
        const chip = document.createElement('span');
        chip.className = 'bim-chip';
        chip.contentEditable = 'false';
        chip.setAttribute('data-chip-id', item.chipId);
        chip.setAttribute('data-tooltip', item.chipTooltip || '');
        chip.textContent = item.chipLabel || item.name || '';
        return chip;
    },

    restoreMessageTooltip(element, chipsById) {
        if (!element || element.tagName !== 'SPAN' || !chipsById) return false;
        const chip = chipsById.get(element.getAttribute('data-chip-id'));
        if (!chip) return false;
        element.classList.add('bim-chip');
        element.contentEditable = 'false';
        element.setAttribute('data-tooltip', chip.chipTooltip || '');
        return true;
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

    // Создание и вставка чипса по объекту из меню
    create(item) {
        const input = window.Atomix.EditorController.el;
        if (!input || !item.meta || !item.chipId) return;

        const meta = item.meta;

        // Формируем красивый многострочный тултип с защитой от отсутствующих полей
        const tooltipParts = [];
        if (meta.paramType) tooltipParts.push(`${meta.paramType}`);
        if (meta.paramName != null && meta.paramName !== '' && meta.paramValue != null && meta.paramValue !== '') {
            const unitLabel = meta.unitLabel;
            tooltipParts.push(`Parameter: ${meta.paramName} = ${meta.paramValue}` + (unitLabel != null && unitLabel !== '' ? ` ${unitLabel}` : ''));
        }
        if (meta.elementName) tooltipParts.push(`Element: ${meta.elementName}`);
        if (meta.category) tooltipParts.push(`Category: ${meta.category}`);        
        if (meta.elementId) tooltipParts.push(`Id: ${meta.elementId}`);       

        const tooltipText = tooltipParts.join('\n');

        // Создаем DOM-элемент чипса
        const chip = document.createElement('span');
        chip.className = 'bim-chip';
        chip.contentEditable = 'false'; // Защита от редактирования по буквам

        chip.setAttribute('data-chip-id', item.chipId);

        // Тултип и имя оставляем как было
        chip.setAttribute('data-tooltip', tooltipText);
        chip.innerText = `#${meta.paramName}`;

        // Тот же шаг вставки, что и у тегов: span + пробел вне span + курсор за пробелом.
        // У чипса пробел неразрывный, чтобы он не «прилипал» к следующему слову.
        this._insertTag(chip, '\u00A0');

        // Триггерим событие изменения высоты поля, как в main.js
        input.dispatchEvent(new Event('input'));
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
