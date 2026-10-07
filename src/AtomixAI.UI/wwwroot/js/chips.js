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
        /*
        const chip = document.createElement('span');
        chip.className = 'bim-chip';
        chip.contentEditable = 'false'; // Защита: чипс нельзя редактировать по буквам
        chip.setAttribute('data-tooltip', tooltipText);
        chip.innerText = `#${meta.paramName}`;
        */

        // Вставляем чипс в userInput в позицию курсора
        input.focus();
        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            range.deleteContents(); // Удаляем триггерный символ '#'

            // Вставляем чипс и пробел после него для удобства дальнейшего ввода
            range.insertNode(document.createTextNode(' '));
            range.insertNode(chip);

            // Переносим курсор в конец вставленного пробела
            range.setStartAfter(chip.nextSibling);
            range.setEndAfter(chip.nextSibling);
            selection.removeAllRanges();
            selection.addRange(range);
        } else {
            // Если курсор потерян, просто кидаем в конец
            input.appendChild(chip);
            input.appendChild(document.createTextNode(' '));
        }

        // Триггерим событие изменения высоты поля, как в main.js
        input.dispatchEvent(new Event('input'));
    }
};