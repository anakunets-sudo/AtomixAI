// main.js
const chatContainer = document.getElementById('chat');
const inputField = document.getElementById('userInput');
const themeToggle = document.getElementById('themeToggle');
const micBtn = document.getElementById('micBtn');
let isMicActive = false;
let statusMsgId = null;
let thinkingMsgId = null;
let isWorking = false;
let chatTurn = 0;
let cancelledTurn = 0;

// Одна строка при пустом поле и при тексте. 42px после отправки поднимало блок:
// плейсхолдер оказывался над кнопками, а набранный текст — в один ряд с ними.
function resizeInputField() {
    inputField.style.height = 'auto';
    const lineHeight = parseFloat(getComputedStyle(inputField).lineHeight) || 0;
    const next = Math.min(Math.max(inputField.scrollHeight, lineHeight), 150);
    inputField.style.height = next + 'px';
}

inputField.addEventListener('input', function () {
    // Для div выкарыстоўваецца textContent, а не value!
    if (this.textContent.trim() === '') {
        inputField.innerHTML = '';
    }
    resizeInputField();
});

resizeInputField();

// Слушаем событие ПЕРЕХВАТА вставки на уровне всего документа
document.addEventListener('paste', function (e) {
    // Проверяем, что фокус находится в каком-то поле ввода
    const target = e.target;
    if (target.hasAttribute('contenteditable') || target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') {

        // КРИТИЧЕСКИЙ ШАГ: Останавливаем дефолтную вставку браузера И 
        // всплытие события наружу в WPF-контейнер Revit
        e.preventDefault();
        e.stopPropagation();

        // Читаем буфер обмена асинхронно (Revit сюда дотянуться не может)
        navigator.clipboard.readText().then(text => {
            if (!text) return;

            if (target.hasAttribute('contenteditable')) {
                // Для contenteditable элементов:
                const selection = window.getSelection();
                if (!selection.rangeCount) return;

                // Удаляем выделенный текст, если пользователь что-то выделил перед Ctrl+V
                selection.deleteFromDocument();

                // Вставляем чистый текст в позицию курсора
                const range = selection.getRangeAt(0);
                const textNode = document.createTextNode(text);
                range.insertNode(textNode);

                // Перемещаем каретку (курсор) строго в конец вставленного текста
                selection.collapseToEnd();
            } else {
                // Для обычных <input> и <textarea>:
                const start = target.selectionStart;
                const end = target.selectionEnd;
                const currentVal = target.value;

                // Собираем строку обратно, вставив текст из буфера ровно между start и end
                target.value = currentVal.substring(0, start) + text + currentVal.substring(end);

                // Возвращаем курсор на место после вставленного текста
                target.selectionStart = target.selectionEnd = start + text.length;
            }
        }).catch(err => {
            console.error('Ошибка доступа к буферу обмена:', err);
        });
    }
}, true); // <- TRUE ТУТ ОБЯЗАТЕЛЕН. Это включает фазу перехвата (capture)


// --- 3. МИКРОФОН (ФИЗИЧЕСКИЙ КОННЕКТ + ЭКВАЛАЙЗЕР) ---
function toggleVoice() {
    if (isWorking) return;
    isMicActive = !isMicActive;
    const micIcon = document.getElementById('micIcon');
    if (isMicActive) {
        // Стиль активной записи
        micBtn.style.setProperty('background', 'var(--danger)', 'important');
        micBtn.style.color = "#fff";
        micIcon.innerText = "\uF12E"; // Segoe MDL2: Mic с волнами
        // Сообщение "Слушаю..." с прыгающими барами
        const listenHtml = `
 <div class="listening-bars"><span></span><span></span><span></span></div>
 <span>${window.Atomix.t('chat.listening', 'Listening for commands...')}</span>
 `;
        const msgDiv = document.createElement('div');
        statusMsgId = 'status-' + Math.random().toString(36).substr(2, 9);
        msgDiv.id = statusMsgId;
        msgDiv.className = 'msg ai listening-state';
        msgDiv.innerHTML = listenHtml;
        chatContainer.appendChild(msgDiv);
        chatContainer.scrollTop = chatContainer.scrollHeight;
    } else {
        // Возврат в режим ожидания
        micBtn.style.removeProperty('background');
        micBtn.style.removeProperty('color');
        micIcon.innerText = "\uEC71"; // Segoe MDL2: Microphone, как в index.html
        if (statusMsgId) {
            const el = document.getElementById(statusMsgId);
            if (el) el.remove();
            statusMsgId = null;
        }
    }
    // КРИТИЧЕСКИЙ ВЫЗОВ: Отправка сигнала в C# для активации аудио-потока
    sendAction('toggle_voice', { active: isMicActive });
}

function injectVoiceText(text) {
    if (!text) return;
    const current = inputField.textContent;
    if (current && !/\s$/.test(current)) {
        inputField.appendChild(document.createTextNode(' '));
    }
    inputField.appendChild(document.createTextNode(text));
    inputField.dispatchEvent(new Event('input'));
}

// --- 1. СВЯЗЬ С REVIT (C# AtomixAI.Bridge) ---
function sendAction(action, data) {
    if (window.chrome && window.chrome.webview) {
        // Данные чипсов улетают строго структурированными
        window.chrome.webview.postMessage({ action, ...data });
    }
}

// --- 2. ЛОГИКА ЧАТА ---
function sendMessage() {
    const text = inputField.innerHTML.trim();
    if (!text || isWorking) return;

    // 1. Извлекаем текст сообщения (DOM-структура div с чипсами преобразуется в строку)
    // Пользователь видит плашки, но textContent вернет строку вида: "Измени параметр #Высота на 3000"
    const plainTextPrompt = inputField.textContent.trim();

    // 2. СБОР КОНТЕКСТА: Находим все span.bim-chip, которые реально остались на экране
    // Если чипсов нет, querySelectorAll вернет пустой список, и массив останется пустым []
    const contextArray = Array.from(inputField.querySelectorAll('.bim-chip')).map(chip => {
        const rawJson = chip.getAttribute('data-json');
        return {
            alias: chip.innerText.trim(), // Строковый тег из чата, например "#Высота"
            ...(rawJson ? JSON.parse(rawJson) : {}) // Распаковываем все BIM-метаданные в корень объекта
        };
    });

    appendMessage('user', text);
    chatTurn += 1;
    thinkingMsgId = appendThinkingMessage();
    setBusy(true);

    // 3. Отправляем строго структурированный пакет в C#
    sendAction('chat_request', {
        payload: {
            prompt: plainTextPrompt,
            context: contextArray,
            turn: chatTurn
        }
    });

    inputField.innerHTML = '';
    resizeInputField();
}

const messageSanitizeConfig = {
    ALLOWED_TAGS: [
        'p', 'br', 'strong', 'b', 'em', 'i', 'del', 's', 'code', 'pre',
        'blockquote', 'ul', 'ol', 'li', 'h1', 'h2', 'h3', 'h4', 'h5',
        'h6', 'hr', 'a', 'table', 'thead', 'tbody', 'tr', 'th', 'td', 'span'
    ],
    ALLOWED_ATTR: ['href', 'title', 'class'],
    ALLOW_DATA_ATTR: false,
    ALLOW_ARIA_ATTR: false,
    SAFE_FOR_TEMPLATES: true
};

function sanitizeMessageHtml(text) {
    const source = String(text ?? '');
    const parsedHtml = typeof marked !== 'undefined' ? marked.parse(source) : source;

    // Не вставляем непроверенный HTML, даже если библиотека не загрузилась.
    if (typeof DOMPurify === 'undefined' || !DOMPurify.isSupported) {
        const fallback = document.createElement('div');
        fallback.textContent = source;
        return fallback.innerHTML;
    }

    return DOMPurify.sanitize(parsedHtml, messageSanitizeConfig);
}

function decorateAliases(container) {
    const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
    const textNodes = [];

    while (walker.nextNode()) {
        const parent = walker.currentNode.parentElement;
        if (!parent || parent.closest('code, pre, a, .alias-btn')) continue;
        textNodes.push(walker.currentNode);
    }

    textNodes.forEach(node => {
        const text = node.nodeValue;
        const aliasPattern = /['"]?(#\w+)['"]?/g;
        let match;
        let lastIndex = 0;
        let foundAlias = false;
        const fragment = document.createDocumentFragment();

        while ((match = aliasPattern.exec(text)) !== null) {
            foundAlias = true;
            fragment.appendChild(document.createTextNode(text.slice(lastIndex, match.index)));

            const alias = document.createElement('span');
            alias.className = 'alias-btn';
            alias.dataset.alias = match[1];
            alias.textContent = match[1];
            fragment.appendChild(alias);
            lastIndex = aliasPattern.lastIndex;
        }

        if (!foundAlias) return;
        fragment.appendChild(document.createTextNode(text.slice(lastIndex)));
        node.replaceWith(fragment);
    });
}

function renderMessageContent(container, text) {
    container.innerHTML = sanitizeMessageHtml(text);

    // Класс alias-btn назначается только элементам, созданным этим приложением.
    container.querySelectorAll('.alias-btn').forEach(element => {
        element.classList.remove('alias-btn');
    });
    decorateAliases(container);
}

function isStaleOrCancelledReply(data) {
    const turn = Number(data.turn || 0);
    if (turn && turn !== chatTurn) return true;
    return cancelledTurn !== 0 && cancelledTurn === chatTurn;
}

function handleAIResponse(data) {
    // Отменённый ход уже показан как «Cancelled». Поздний ui_log не подменяет его и не создаёт новое сообщение.
    if (data.role === 'ai' && isStaleOrCancelledReply(data)) return;

    // Превью приказа: текст в пузыре «думаю», Revit ещё выполняет sequence.
    if (data.phase === 'plan' && thinkingMsgId) {
        const msgEl = document.getElementById(thinkingMsgId);
        const label = msgEl && msgEl.querySelector('.thinking-label');
        if (label && data.content) {
            label.textContent = data.content;
            chatContainer.scrollTop = chatContainer.scrollHeight;
        }
        return;
    }

    setBusy(false);
    if (thinkingMsgId && data.role === 'ai') {
        const msgEl = document.getElementById(thinkingMsgId);
        if (msgEl) {
            msgEl.classList.remove('thinking-state');
            renderMessageContent(msgEl, data.content);
            initAliasButtons(msgEl);
            addRating(msgEl);
        }
        thinkingMsgId = null;
    } else {
        // Если это обычное сообщение (не из состояния "thinking")
        appendMessage(data.role, data.content, data.role === 'ai');
    }
}

// Курсивный текст с цветовым переливом и кнопкой отмены
function appendThinkingMessage() {
    const id = 'msg-' + Math.random().toString(36).substr(2, 9);
    const msgDiv = document.createElement('div');
    msgDiv.id = id;
    msgDiv.className = 'msg ai thinking-state';
    msgDiv.innerHTML = `
 <span class="thinking-label">${window.Atomix.t('chat.thinking', 'Synthesizing logic...')}</span>
 <a class="cancel-btn-link" onclick="cancelAction()">${window.Atomix.t('chat.cancel', 'Cancel')}</a>
 `;
    chatContainer.appendChild(msgDiv);
    chatContainer.scrollTop = chatContainer.scrollHeight;
    return id;
}

function cancelAction() {
    cancelledTurn = chatTurn;
    sendAction('stop', {}); // Сигнал в Revit прервать транзакцию
    if (thinkingMsgId) {
        const el = document.getElementById(thinkingMsgId);
        if (el) {
            const span = document.createElement('span');
            span.style.opacity = '1';
            span.textContent = window.Atomix.t('chat.cancelled', 'Cancelled by user');
            el.replaceChildren(span);
        }
        thinkingMsgId = null;
    }
    setBusy(false);
}

function setBusy(busy) {
    isWorking = busy;
    inputField.disabled = busy;
    micBtn.disabled = busy;
    const sendBtn = document.querySelector('.send-btn');
    if (sendBtn) sendBtn.disabled = busy;
}

function appendMessage(role, text, withRating = false) {
    const msgDiv = document.createElement('div');
    const id = 'msg-' + Math.random().toString(36).substr(2, 9);
    msgDiv.id = id;
    msgDiv.className = `msg ${role}`;
    renderMessageContent(msgDiv, text);
    initAliasButtons(msgDiv);
    // Добавляем рейтинг, если это ИИ
    if (withRating && role === 'ai') addRating(msgDiv);
    chatContainer.appendChild(msgDiv);
    chatContainer.scrollTop = chatContainer.scrollHeight;
    return id;
}
// Вынесем инициализацию кнопок в отдельную функцию для удобства
function initAliasButtons(container) {
    container.querySelectorAll('.alias-btn').forEach(btn => {
        // КРИТИЧЕСКИ ВАЖНО: используем mousedown вместо click
        btn.addEventListener('mousedown', (e) => {
            // 1. Намертво глушим событие, чтобы кнопка НЕ забирала фокус у текстового поля
            e.preventDefault();
            e.stopPropagation();

            const alias = btn.getAttribute('data-alias');
            // Тег из сообщения ИИ вставляется тем же шагом, что и тег из меню
            // Recent ▸ Tags: span.alias-btn, пробел вне span, курсор за пробелом.
            if (alias) window.Atomix.ChipsController.insertAlias(alias);

            // 5. Визуальный отклик кнопки (так как mousedown срабатывает мгновенно)
            btn.style.background = 'var(--accent)';
            btn.style.color = '#fff';
            setTimeout(() => {
                btn.style.background = '';
                btn.style.color = '';
            }, 200);
        });
    });
}

// --- 4. ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ ---
function updateStatus(data) {
    const modelLabel = document.querySelector('.status-text');
    const dot = document.querySelector('.status-dot');
    if (modelLabel) modelLabel.innerText = `${window.Atomix.t('footer.aiModel', 'AI model:')} ${data.model}`;
    if (dot) data.status === 'online' ? dot.classList.add('online') : dot.classList.remove('online');
}

function addRating(el) {
    const div = document.createElement('div');
    div.className = 'rating';
    div.innerHTML = `
        <!-- Кнопка Палец Вверх (Лайк) -->
        <span class="rate-btn" onclick="const p=this.parentElement; p.innerHTML=\`<span data-tooltip='Marked as solution' data-i18n-tooltip='rate.markedSolution'><svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' fill='currentColor' class='bi bi-hand-thumbs-up' viewBox='0 0 16 16'><path d='M8.864.046C7.908-.193 7.02.53 6.956 1.466c-.072 1.051-.23 2.016-.428 2.59-.125.36-.479 1.013-1.04 1.639-.557.623-1.282 1.178-2.131 1.41C2.685 7.288 2 7.87 2 8.72v4.001c0 .845.682 1.464 1.448 1.545 1.07.114 1.564.415 2.068.723l.048.03c.272.165.578.348.97.484.397.136.861.217 1.466.217h3.5c.937 0 1.599-.477 1.934-1.064a1.86 1.86 0 0 0 .254-.912c0-.152-.023-.312-.077-.464.201-.263.38-.578.488-.901.11-.33.172-.762.004-1.149.069-.13.12-.269.159-.403.077-.27.113-.568.113-.857 0-.288-.036-.585-.113-.856a2 2 0 0 0-.138-.362 1.9 1.9 0 0 0 .234-1.734c-.206-.592-.682-1.1-1.2-1.272-.847-.282-1.803-.276-2.516-.211a10 10 0 0 0-.443.05 9.4 9.4 0 0 0-.062-4.509A1.38 1.38 0 0 0 9.125.111zM11.5 14.721H8c-.51 0-.863-.069-1.14-.164-.281-.097-.506-.228-.776-.393l-.04-.024c-.555-.339-1.198-.731-2.49-.868-.333-.036-.554-.29-.554-.55V8.72c0-.254.226-.543.62-.65 1.095-.3 1.977-.996 2.614-1.708.635-.71 1.064-1.475 1.238-1.978.243-.7.407-1.768.482-2.85.025-.362.36-.594.667-.518l.262.066c.16.04.258.143.288.255a8.34 8.34 0 0 1-.145 4.725.5.5 0 0 0 .595.644l.003-.001.014-.003.058-.014a9 9 0 0 1 1.036-.157c.663-.06 1.457-.054 2.11.164.175.058.45.3.57.65.107.308.087.67-.266 1.022l-.353.353.353.354c.043.043.105.141.154.315.048.167.075.37.075.581 0 .212-.027.414-.075.582-.05.174-.111.272-.154.315l-.353.353.353.354c.047.047.109.177.005.488a2.2 2.2 0 0 1-.505.805l-.353.353.353.354c.006.005.041.05.041.17a.9.9 0 0 1-.121.416c-.165.288-.503.56-1.066.56z'/></svg></span>\`; window.Atomix.I18n.apply(p)">
            <span data-tooltip="Mark as solution" data-i18n-tooltip="rate.markSolution"><svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" class="bi bi-hand-thumbs-up" viewBox="0 0 16 16">
                <path d="M8.864.046C7.908-.193 7.02.53 6.956 1.466c-.072 1.051-.23 2.016-.428 2.59-.125.36-.479 1.013-1.04 1.639-.557.623-1.282 1.178-2.131 1.41C2.685 7.288 2 7.87 2 8.72v4.001c0 .845.682 1.464 1.448 1.545 1.07.114 1.564.415 2.068.723l.048.03c.272.165.578.348.97.484.397.136.861.217 1.466.217h3.5c.937 0 1.599-.477 1.934-1.064a1.86 1.86 0 0 0 .254-.912c0-.152-.023-.312-.077-.464.201-.263.38-.578.488-.901.11-.33.172-.762.004-1.149.069-.13.12-.269.159-.403.077-.27.113-.568.113-.857 0-.288-.036-.585-.113-.856a2 2 0 0 0-.138-.362 1.9 1.9 0 0 0 .234-1.734c-.206-.592-.682-1.1-1.2-1.272-.847-.282-1.803-.276-2.516-.211a10 10 0 0 0-.443.05 9.4 9.4 0 0 0-.062-4.509A1.38 1.38 0 0 0 9.125.111zM11.5 14.721H8c-.51 0-.863-.069-1.14-.164-.281-.097-.506-.228-.776-.393l-.04-.024c-.555-.339-1.198-.731-2.49-.868-.333-.036-.554-.29-.554-.55V8.72c0-.254.226-.543.62-.65 1.095-.3 1.977-.996 2.614-1.708.635-.71 1.064-1.475 1.238-1.978.243-.7.407-1.768.482-2.85.025-.362.36-.594.667-.518l.262.066c.16.04.258.143.288.255a8.34 8.34 0 0 1-.145 4.725.5.5 0 0 0 .595.644l.003-.001.014-.003.058-.014a9 9 0 0 1 1.036-.157c.663-.06 1.457-.054 2.11.164.175.058.45.3.57.65.107.308.087.67-.266 1.022l-.353.353.353.354c.043.043.105.141.154.315.048.167.075.37.075.581 0 .212-.027.414-.075.582-.05.174-.111.272-.154.315l-.353.353.353.354c.047.047.109.177.005.488a2.2 2.2 0 0 1-.505.805l-.353.353.353.354c.006.005.041.05.041.17a.9.9 0 0 1-.121.416c-.165.288-.503.56-1.066.56z"/>
            </svg></span>
        </span>
        
        <!-- Кнопка Палец Вниз (Дизлайк) -->
        <span class="rate-btn" onclick="const p=this.parentElement; p.innerHTML=\`<span data-tooltip='Marked as error' data-i18n-tooltip='rate.markedError'><svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' fill='currentColor' class='bi bi-hand-thumbs-down-flipped-x' viewBox='0 0 16 16'><g transform='translate(16, 16) scale(-1, -1)'><path d='M8.864.046C7.908-.193 7.02.53 6.956 1.466c-.072 1.051-.23 2.016-.428 2.59-.125.36-.479 1.013-1.04 1.639-.557.623-1.282 1.178-2.131 1.41C2.685 7.288 2 7.87 2 8.72v4.001c0 .845.682 1.464 1.448 1.545 1.07.114 1.564.415 2.068.723l.048.03c.272.165.578.348.97.484.397.136.861.217 1.466.217h3.5c.937 0 1.599-.477 1.934-1.064a1.86 1.86 0 0 0 .254-.912c0-.152-.023-.312-.077-.464.201-.263.38-.578.488-.901.11-.33.172-.762.004-1.149.069-.13.12-.269.159-.403.077-.27.113-.568.113-.857 0-.288-.036-.585-.113-.856a2 2 0 0 0-.138-.362 1.9 1.9 0 0 0 .234-1.734c-.206-.592-.682-1.1-1.2-1.272-.847-.282-1.803-.276-2.516-.211a10 10 0 0 0-.443.05 9.4 9.4 0 0 0-.062-4.509A1.38 1.38 0 0 0 9.125.111zM11.5 14.721H8c-.51 0-.863-.069-1.14-.164-.281-.097-.506-.228-.776-.393l-.04-.024c-.555-.339-1.198-.731-2.49-.868-.333-.036-.554-.29-.554-.55V8.72c0-.254.226-.543.62-.65 1.095-.3 1.977-.996 2.614-1.708.635-.71 1.064-1.475 1.238-1.978.243-.7.407-1.768.482-2.85.025-.362.36-.594.667-.518l.262.066c.16.04.258.143.288.255a8.34 8.34 0 0 1-.145 4.725.5.5 0 0 0 .595.644l.003-.001.014-.003.058-.014a9 9 0 0 1 1.036-.157c.663-.06 1.457-.054 2.11.164.175.058.45.3.57.65.107.308.087.67-.266 1.022l-.353.353.353.354c.043.043.105.141.154.315.048.167.075.37.075.581 0 .212-.027.414-.075.582-.05.174-.111.272-.154.315l-.353.353.353.354c.047.047.109.177.005.488a2.2 2.2 0 0 1-.505.805l-.353.353.353.354c.006.005.041.05.041.17a.9.9 0 0 1-.121.416c-.165.288-.503.56-1.066.56z'/></g></svg></span>\`; window.Atomix.I18n.apply(p)">
            <span data-tooltip="Mark as error" data-i18n-tooltip="rate.markError"><svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" class="bi bi-hand-thumbs-down-flipped-x" viewBox="0 0 16 16">
              <!-- Оборачиваем path в группу и зеркалим по горизонтали относительно сетки 16x16 -->
              <g transform="translate(16, 16) scale(-1, -1)">
                <path d="M8.864.046C7.908-.193 7.02.53 6.956 1.466c-.072 1.051-.23 2.016-.428 2.59-.125.36-.479 1.013-1.04 1.639-.557.623-1.282 1.178-2.131 1.41C2.685 7.288 2 7.87 2 8.72v4.001c0 .845.682 1.464 1.448 1.545 1.07.114 1.564.415 2.068.723l.048.03c.272.165.578.348.97.484.397.136.861.217 1.466.217h3.5c.937 0 1.599-.477 1.934-1.064a1.86 1.86 0 0 0 .254-.912c0-.152-.023-.312-.077-.464.201-.263.38-.578.488-.901.11-.33.172-.762.004-1.149.069-.13.12-.269.159-.403.077-.27.113-.568.113-.857 0-.288-.036-.585-.113-.856a2 2 0 0 0-.138-.362 1.9 1.9 0 0 0 .234-1.734c-.206-.592-.682-1.1-1.2-1.272-.847-.282-1.803-.276-2.516-.211a10 10 0 0 0-.443.05 9.4 9.4 0 0 0-.062-4.509A1.38 1.38 0 0 0 9.125.111zM11.5 14.721H8c-.51 0-.863-.069-1.14-.164-.281-.097-.506-.228-.776-.393l-.04-.024c-.555-.339-1.198-.731-2.49-.868-.333-.036-.554-.29-.554-.55V8.72c0-.254.226-.543.62-.65 1.095-.3 1.977-.996 2.614-1.708.635-.71 1.064-1.475 1.238-1.978.243-.7.407-1.768.482-2.85.025-.362.36-.594.667-.518l.262.066c.16.04.258.143.288.255a8.34 8.34 0 0 1-.145 4.725.5.5 0 0 0 .595.644l.003-.001.014-.003.058-.014a9 9 0 0 1 1.036-.157c.663-.06 1.457-.054 2.11.164.175.058.45.3.57.65.107.308.087.67-.266 1.022l-.353.353.353.354c.043.043.105.141.154.315.048.167.075.37.075.581 0 .212-.027.414-.075.582-.05.174-.111.272-.154.315l-.353.353.353.354c.047.047.109.177.005.488a2.2 2.2 0 0 1-.505.805l-.353.353.353.354c.006.005.041.05.041.17a.9.9 0 0 1-.121.416c-.165.288-.503.56-1.066.56z"/>
              </g>
            </svg></span>
        </span>
    `;
    window.Atomix.I18n.apply(div);
    el.appendChild(div);
}

// Слушатели событий ввода
inputField.addEventListener('keydown', e => {
    if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        sendMessage();
    }
});


// --- SMART TOOLTIP ENGINE ---
const tip = document.createElement('div');
tip.id = 'custom-tooltip';
document.body.appendChild(tip);
let tooltipTimeout; // Переменная для хранения таймера
document.addEventListener('mouseover', e => {
    const target = e.target.closest('[data-tooltip]');
    if (!target) return;
    // 1. Сначала подготавливаем данные, но не показываем
    const text = target.getAttribute('data-tooltip');
    // 2. Запускаем таймер
    tooltipTimeout = setTimeout(() => {
        tip.innerText = text;
        tip.style.display = 'block';
        const rect = target.getBoundingClientRect();
        // --- Твой блок позиционирования (без изменений) ---
        let left = rect.left + (rect.width / 2);
        let top = rect.top - 35;
        if (rect.top < 50) top = rect.bottom + 10;
        const tipWidth = tip.offsetWidth;
        if (left + (tipWidth / 2) > window.innerWidth - 10) {
            left = window.innerWidth - (tipWidth / 2) - 10;
        }
        if (left - (tipWidth / 2) < 10) {
            left = 10 + (tipWidth / 2);
        }
        // ------------------------------------------------
        tip.style.left = `${left - (tipWidth / 2)}px`;
        tip.style.top = `${top}px`;
        tip.style.opacity = '1';
    }, 400); // <-- Задержка в миллисекундах (0.7 сек)
});
document.addEventListener('mouseout', e => {
    if (e.target.closest('[data-tooltip]')) {
        // 3. ОБЯЗАТЕЛЬНО: Отменяем таймер, если пользователь убрал мышь быстрее, чем сработал Tooltip
        clearTimeout(tooltipTimeout);
        tip.style.opacity = '0';
        tip.style.display = 'none';
    }
});

