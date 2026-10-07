// app.js
const { EditorController, MenuController, RevitBridge } = window.Atomix;
// Слушаем ввод
// Инициализация при загрузке
document.addEventListener('DOMContentLoaded', () => {
    window.Atomix.EditorController.init();
});
// Слушаем ввод в редакторе
window.Atomix.EditorController.el.addEventListener('input', () => {
    const state = window.Atomix.EditorController.getTriggerState();
    if (state.isTriggered) {
        window.Atomix.MenuController.update(); // Показ дефолтных пунктов
    } else {
        window.Atomix.MenuController.hide();
    }
});
// Единая точка входа для всех сообщений из Revit API через WebView2
window.Atomix.RevitBridge.onMessage((data) => {
    // 1. Обработка контекстных данных для IntelliSense-меню (#)
    if (data.action === 'MENU_DATA') {
        window.Atomix.MenuController.update(data.payload.items);
    }
    // 2. Обработка изменения темы со стороны Revit
    else if (data.action === 'theme_changed') {
        applyTheme(data.theme);
    }
    // 3. Инъекция распознанного голоса в текстовое поле
    else if (data.type === 'voice_input') {
        injectVoiceText(data.content);
    }
    // 4. Обработка ответа от ИИ (вывод в чат и парсинг чипсов обратно)
    else if (data.action === 'ui_log') {
        handleAIResponse(data);
    }
    // 5. Обновление статуса ИИ-модели в статус-баре
    else if (data.type === 'system_status') {
        updateStatus(data);
    }
});
