// i18n.js — словарь приходит из C# (Localizer) до загрузки страницы: window.__ATOMIX_I18N / __ATOMIX_LANG
window.Atomix = window.Atomix || {};
window.Atomix.I18n = {
    strings: window.__ATOMIX_I18N || {},
    lang: window.__ATOMIX_LANG || 'en',

    t(key, fallback) {
        const value = this.strings[key];
        if (typeof value === 'string') return value;
        return fallback !== undefined ? fallback : key;
    },

    greeting(fallback) {
        const list = this.strings['chat.greetings'];
        if (Array.isArray(list) && list.length) return list[Math.floor(Math.random() * list.length)];
        return fallback;
    },

    // Разметка: data-i18n (текст), data-i18n-tooltip, data-i18n-placeholder, data-i18n-aria, data-i18n-greeting
    apply(root = document) {
        try { this._apply(root); } catch (e) { console.error('[i18n]', e); }
    },

    _apply(root) {
        root.querySelectorAll('[data-i18n]').forEach(el => {
            el.textContent = this.t(el.dataset.i18n, el.textContent);
        });
        root.querySelectorAll('[data-i18n-tooltip]').forEach(el => {
            el.setAttribute('data-tooltip', this.t(el.dataset.i18nTooltip, el.getAttribute('data-tooltip')));
        });
        root.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
            el.setAttribute('placeholder', this.t(el.dataset.i18nPlaceholder, el.getAttribute('placeholder')));
        });
        root.querySelectorAll('[data-i18n-aria]').forEach(el => {
            el.setAttribute('aria-label', this.t(el.dataset.i18nAria, el.getAttribute('aria-label')));
        });
        root.querySelectorAll('[data-i18n-greeting]').forEach(el => {
            el.textContent = this.greeting(el.textContent);
        });
    }
};
window.Atomix.t = (key, fallback) => window.Atomix.I18n.t(key, fallback);

document.documentElement.lang = window.Atomix.I18n.lang;
