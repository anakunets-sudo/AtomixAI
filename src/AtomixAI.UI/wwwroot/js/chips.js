// chips.js
window.Atomix = window.Atomix || {};
window.Atomix.ChipsController = {
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

        input.focus();
        const selection = window.getSelection();
        const space = document.createTextNode('\u00A0');

        if (selection.rangeCount > 0 && input.contains(selection.anchorNode)) {
            const range = selection.getRangeAt(0);
            range.deleteContents();
            this._removeTriggerBeforeCaret(input, range);

            range.insertNode(chip);
            chip.after(space);

            const caret = document.createRange();
            caret.setStart(space, 1);
            caret.collapse(true);
            selection.removeAllRanges();
            selection.addRange(caret);
        } else {
            this._removeTrailingTrigger(input);
            input.appendChild(chip);
            input.appendChild(space);
            const caret = document.createRange();
            caret.setStart(space, 1);
            caret.collapse(true);
            selection.removeAllRanges();
            selection.addRange(caret);
        }

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
    },

    _removeTrailingTrigger(input) {
        const nodes = input.childNodes;
        if (!nodes.length) return;
        this._deleteTriggerAtEnd(nodes[nodes.length - 1]);
    }
};