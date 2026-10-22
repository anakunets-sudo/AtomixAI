// editor.js
window.Atomix = window.Atomix || {};
window.Atomix.EditorController = {
    el: document.getElementById('userInput'),
    triggerChar: '#',
    savedRange: null,
    _mouseDownInEditor: false,
    init() {
        if (!this.el) return;
        this.el.addEventListener('mousedown', (e) => {
            if (e.button === 0) this._mouseDownInEditor = true;
        }, true);
        document.addEventListener('mouseup', () => {
            this._mouseDownInEditor = false;
        }, true);
        document.addEventListener('selectionchange', () => this.saveSelection());
        this.el.addEventListener('blur', () => this.saveSelection());
        this.el.addEventListener('focus', () => {
            if (this._mouseDownInEditor) return;
            this.restoreSelection();
        });
        this.el.addEventListener('input', () => {
            if (!this._selectionIsInEditor()) this.savedRange = null;
        });
        // Глобальный перехват клавиш для управления меню (чтобы фокус не терялся)
        document.addEventListener('keydown', (e) => {
            const menu = window.Atomix.MenuController;
            if (!menu || !menu.isOpen) return;
            if (menu.isSearchFocused && menu.isSearchFocused()) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    menu.applySearch();
                    return;
                }
                if (e.key !== 'Escape') return;
            }
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
    _selectionIsInEditor() {
        const selection = window.getSelection();
        return !!(selection && selection.rangeCount > 0 &&
            this.el.contains(selection.anchorNode) &&
            this.el.contains(selection.focusNode));
    },
    saveSelection() {
        if (!this._selectionIsInEditor()) return;
        this.savedRange = window.getSelection().getRangeAt(0).cloneRange();
    },
    getCaretRange() {
        if (document.activeElement !== this.el && this.savedRange &&
            this.el.contains(this.savedRange.startContainer) &&
            this.el.contains(this.savedRange.endContainer)) {
            return this.savedRange.cloneRange();
        }
        const selection = window.getSelection();
        if (selection && selection.rangeCount > 0) {
            const range = selection.getRangeAt(0);
            if (this.el.contains(range.startContainer) && this.el.contains(range.endContainer)) {
                this.savedRange = range.cloneRange();
                return range;
            }
        }
        return this.savedRange && this.el.contains(this.savedRange.startContainer) &&
            this.el.contains(this.savedRange.endContainer)
            ? this.savedRange.cloneRange()
            : null;
    },
    restoreSelection() {
        if (!this.savedRange || !this.el.contains(this.savedRange.startContainer) ||
            !this.el.contains(this.savedRange.endContainer)) return;
        const selection = window.getSelection();
        selection.removeAllRanges();
        selection.addRange(this.savedRange.cloneRange());
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