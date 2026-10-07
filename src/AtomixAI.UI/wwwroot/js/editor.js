// editor.js
window.Atomix = window.Atomix || {};
window.Atomix.EditorController = {
    el: document.getElementById('userInput'),
    triggerChar: '#',
    init() {
        if (!this.el) return;
        // Глобальный перехват клавиш для управления меню (чтобы фокус не терялся)
        document.addEventListener('keydown', (e) => {
            const menu = window.Atomix.MenuController;
            if (!menu || !menu.isOpen) return;
            const navKeys = ['ArrowUp', 'ArrowDown', 'ArrowRight', 'ArrowLeft', 'Enter', 'Escape'];
            if (navKeys.includes(e.key)) {
                // Если фокус улетел из поля ввода (например, кликнули на меню), возвращаем его
                if (document.activeElement !== this.el) {
                    this.el.focus();
                }
                this.handleKeyDown(e);
            }
        }, true);
        // Очистка при вставке
        this.el.addEventListener('paste', (e) => {
            e.preventDefault();
            const text = (e.clipboardData || window.clipboardData).getData('text');
            document.execCommand('insertText', false, text);
        });
        console.log("EditorController: Initialized");
    },
    handleKeyDown(e) {
        const menu = window.Atomix.MenuController;
        // Блокируем стандартное поведение стрелок внутри userInput, когда открыто меню
        e.preventDefault();
        e.stopImmediatePropagation();
        switch (e.key) {
            case 'ArrowDown':
                menu.moveSelection('down');
                break;
            case 'ArrowUp':
                menu.moveSelection('up');
                break;
            case 'ArrowRight':
            case 'Enter':
                menu.drillDown();
                break;
            case 'ArrowLeft':
                menu.goBack();
                break;
            case 'Escape':
                if (menu.history.length > 0) menu.goBack();
                else menu.hide();
                break;
        }
    },
    getTriggerState() {
        const selection = window.getSelection();
        if (selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            const container = range.startContainer;
            if (container.nodeType === Node.TEXT_NODE) {
                const textBefore = container.textContent.substring(0, range.startOffset);
                return { isTriggered: textBefore.endsWith(this.triggerChar) };
            } else if (container === this.el && range.startOffset > 0) {
                const lastNode = container.childNodes[range.startOffset - 1];
                if (lastNode && lastNode.nodeType === Node.TEXT_NODE) {
                    return { isTriggered: lastNode.textContent.endsWith(this.triggerChar) };
                }
            }
        }
        return { isTriggered: false };
    }
};
document.addEventListener('DOMContentLoaded', () => window.Atomix.EditorController.init());