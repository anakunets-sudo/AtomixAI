// bridge.js
window.Atomix = window.Atomix || {};
window.Atomix.RevitBridge = {
    send(action, payload = {}) {
        // УДАЛЯЕМ JSON.stringify! Передаем объект напрямую
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({ action, payload });
        }
    },
    onMessage(callback) {
        window.chrome.webview.addEventListener('message', (event) => {
            const data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
            callback(data);
        });
    }
};

