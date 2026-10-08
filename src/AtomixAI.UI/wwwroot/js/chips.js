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
        aliasElement.dataset.alias = alias;
        aliasElement.textContent = alias;

        this._insertTag(aliasElement, ' ');
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
        if (!input || !item.meta) return;

        const meta = item.meta;

        // Формируем красивый многострочный тултип с защитой от отсутствующих полей
        const tooltipParts = [];
        if (meta.paramType) tooltipParts.push(`[${meta.paramType}]`);
        if (meta.category) tooltipParts.push(`Категория: ${meta.category}`);
        if (meta.elementName) tooltipParts.push(`Элемент: ${meta.elementName}`);
        if (meta.elementId) tooltipParts.push(`ID: ${meta.elementId}`);
        if (meta.paramName && meta.paramValue) tooltipParts.push(`Свойство: ${meta.paramName} = ${meta.paramValue}`);

        const tooltipText = tooltipParts.join('\n');

        // Создаем DOM-элемент чипса
        const chip = document.createElement('span');
        chip.className = 'bim-chip';
        chip.contentEditable = 'false'; // Защита от редактирования по буквам

        // КЛЮЧЕВОЙ ШАГ: Сериализуем весь BIM-паспорт объекта целиком в один атрибут
        chip.setAttribute('data-json', JSON.stringify(item.meta));

        // Тултип и имя оставляем как было
        chip.setAttribute('data-tooltip', tooltipText);
        chip.innerText = `#${meta.paramName}`;

        // Тот же шаг вставки, что и у тегов: span + пробел вне span + курсор за пробелом.
        // У чипса пробел неразрывный, чтобы он не «прилипал» к следующему слову.
        this._insertTag(chip, '\u00A0');

        // Триггерим событие изменения высоты поля, как в main.js
        input.dispatchEvent(new Event('input'));
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