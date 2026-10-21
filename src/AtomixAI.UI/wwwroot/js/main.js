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
const chatChips = new Map();

// Прокрутка чата к последнему сообщению. Повторяем в следующем кадре и
// коротким таймаутом: после вставки содержимого (markdown, иконки рейтинга,
// подгрузка истории) высота сообщения может вырасти уже после синхронного
// присвоения scrollTop, из-за чего низ оставался за кадром.
function scrollChatToBottom() {
    if (!chatContainer) return;
    const jump = () => { chatContainer.scrollTop = chatContainer.scrollHeight; };
    jump();
    requestAnimationFrame(jump);
    setTimeout(jump, 0);
}

// Любое изменение списка сообщений (новый пузырь, замена "думаю" ответом,
// загрузка истории, стриминг текста) автоматически прокручивает чат вниз.
if (chatContainer && typeof MutationObserver !== 'undefined') {
    const chatObserver = new MutationObserver(() => scrollChatToBottom());
    chatObserver.observe(chatContainer, {
        childList: true,
        subtree: true,
        characterData: true
    });
}

if (chatContainer) {
    chatContainer.addEventListener('click', async event => {
        const button = event.target.closest('.copy-user-message');
        if (!button) return;

        const message = button.closest('.chat-user-message')?.querySelector('.msg.user');
        if (!message) return;

        const labels = {
            copy: window.Atomix.t('chat.copyMessage', 'Copy message'),
            copied: window.Atomix.t('chat.messageCopied', 'Copied'),
            failed: window.Atomix.t('chat.copyFailed', 'Could not copy message')
        };
        const content = message.cloneNode(true);
        const html = content.innerHTML;
        const plain = content.innerText || content.textContent || '';
        let copied = false;

        try {
            if (navigator.clipboard?.write && typeof ClipboardItem !== 'undefined') {
                const item = new ClipboardItem({
                    'text/html': new Blob([html], { type: 'text/html' }),
                    'text/plain': new Blob([plain], { type: 'text/plain' })
                });
                await navigator.clipboard.write([item]);
                copied = true;
            }
        } catch (error) {
            console.warn('Rich clipboard write failed; trying a fallback:', error);
        }

        if (!copied) copied = copyRichTextWithLegacyCommand(message, html, plain);
        if (!copied && navigator.clipboard?.writeText) {
            try {
                await navigator.clipboard.writeText(plain);
                copied = true;
            } catch (error) {
                console.warn('Clipboard write failed:', error);
            }
        }

        clearTimeout(button._copyFeedbackTimer);
        button.classList.toggle('is-copied', copied);
        button.classList.toggle('is-error', !copied);
        button.setAttribute('aria-label', copied ? labels.copied : labels.failed);
        button.title = copied ? labels.copied : labels.failed;
        button.dataset.tooltip = button.title;
        button._copyFeedbackTimer = setTimeout(() => {
            button.classList.remove('is-copied', 'is-error');
            button.setAttribute('aria-label', labels.copy);
            button.title = labels.copy;
            button.dataset.tooltip = labels.copy;
        }, 1500);
    });
}

function copyRichTextWithLegacyCommand(message, html, plain) {
    if (typeof document.execCommand !== 'function') return false;

    const selection = window.getSelection();
    const savedRanges = selection
        ? Array.from({ length: selection.rangeCount }, (_, index) => selection.getRangeAt(index).cloneRange())
        : [];
    let clipboardWritten = false;
    const handleCopy = event => {
        if (!event.clipboardData) return;
        try {
            event.clipboardData.setData('text/html', html);
            event.clipboardData.setData('text/plain', plain);
            event.preventDefault();
            clipboardWritten = true;
        } catch (error) {
            console.warn('Legacy clipboard format is unavailable:', error);
        }
    };

    document.addEventListener('copy', handleCopy, true);
    try {
        if (selection) {
            const range = document.createRange();
            range.selectNodeContents(message);
            selection.removeAllRanges();
            selection.addRange(range);
        }
        return document.execCommand('copy') && clipboardWritten;
    } catch (error) {
        console.warn('Legacy clipboard copy failed:', error);
        return false;
    } finally {
        document.removeEventListener('copy', handleCopy, true);
        if (selection) {
            selection.removeAllRanges();
            savedRanges.forEach(range => selection.addRange(range));
        }
    }
}

// Одна строка при пустом поле и при тексте. 42px после отправки поднимало блок:
// плейсхолдер оказывался над кнопками, а набранный текст — в один ряд с ними.
function resizeInputField() {
    const isEmpty = inputField.textContent.trim() === '';
    inputField.style.height = 'auto';
    const lineHeight = parseFloat(getComputedStyle(inputField).lineHeight) || 0;
    if (isEmpty) {
        inputField.style.height = Math.ceil(lineHeight) + 'px';
        return;
    }
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

function openMenu() {
    const menu = window.Atomix.MenuController;
    if (menu.isOpen) {
        menu.hide();
        inputField.focus();
        return;
    }
    inputField.focus();
    menu.update();
}

// Перехватываем вставку, чтобы WebView2/Revit мог читать буфер и сохранять HTML-чипсы.
document.addEventListener('paste', function (event) {
    const target = event.target;
    const editable = target.closest?.('[contenteditable="true"]');
    const isInput = target.tagName === 'INPUT' || target.tagName === 'TEXTAREA';
    if (!editable && !isInput) return;

    event.preventDefault();
    event.stopPropagation();

    readClipboardPayload(event, Boolean(editable)).then(payload => {
        const text = payload.text || (payload.html ? htmlToPlainText(payload.html) : '');
        if (editable) {
            insertClipboardContent(editable, payload.html, text);
        } else if (text) {
            const start = target.selectionStart;
            const end = target.selectionEnd;
            target.value = target.value.substring(0, start) + text + target.value.substring(end);
            target.selectionStart = target.selectionEnd = start + text.length;
        }
    }).catch(error => {
        console.error('Ошибка доступа к буферу обмена:', error);
    });
}, true);

function readClipboardPayload(event, includeHtml) {
    const clipboardData = event.clipboardData || window.clipboardData;
    let html = '';
    let text = '';
    try {
        if (includeHtml) html = clipboardData?.getData('text/html') || '';
        text = clipboardData?.getData('text/plain') || clipboardData?.getData('text') || '';
    } catch (error) {
        console.warn('Paste event clipboard data is unavailable:', error);
    }
    if (html || text) return Promise.resolve({ html, text });

    const clipboard = navigator.clipboard;
    if (includeHtml && clipboard?.read) {
        return clipboard.read().then(async items => {
            let richHtml = '';
            let plainText = '';
            for (const item of items) {
                if (item.types.includes('text/html')) {
                    richHtml = await (await item.getType('text/html')).text();
                }
                if (item.types.includes('text/plain')) {
                    plainText = await (await item.getType('text/plain')).text();
                }
            }
            return richHtml || plainText ? { html: richHtml, text: plainText } : readClipboardText();
        }).catch(() => readClipboardText());
    }
    return readClipboardText();
}

function readClipboardText() {
    if (!navigator.clipboard?.readText) return Promise.resolve({ html: '', text: '' });
    return navigator.clipboard.readText().then(text => ({ html: '', text }));
}

function htmlToPlainText(html) {
    const safeHtml = typeof DOMPurify !== 'undefined' && DOMPurify.isSupported
        ? sanitizeClipboardHtml(html)
        : '';
    const template = document.createElement('template');
    template.innerHTML = safeHtml;
    return template.content.textContent || '';
}

function insertClipboardContent(editable, html, text) {
    const selection = window.getSelection();
    let range = selection?.rangeCount ? selection.getRangeAt(0) : null;
    if (!range || !editable.contains(range.startContainer)) {
        range = document.createRange();
        range.selectNodeContents(editable);
        range.collapse(false);
    }
    range.deleteContents();

    const template = document.createElement('template');
    const safeHtml = html && typeof DOMPurify !== 'undefined' && DOMPurify.isSupported
        ? sanitizeClipboardHtml(html)
        : '';
    template.innerHTML = safeHtml;
    if (window.Atomix.ChipsController) {
        template.content.querySelectorAll('span[data-chip-id]').forEach(chip => {
            // Вставка в поле ввода — чипсы остаются редактируемыми.
            window.Atomix.ChipsController.restoreMessageTooltip(chip, chatChips, { editable: true });
        });
    }
    template.content.querySelectorAll('span.alias-btn').forEach(tag => {
        tag.contentEditable = 'false';
    });

    const fragment = template.content;
    if (!fragment.childNodes.length && text) fragment.appendChild(document.createTextNode(text));
    const lastInserted = fragment.lastChild;
    if (!lastInserted) return;

    range.insertNode(fragment);
    range.setStartAfter(lastInserted);
    range.collapse(true);
    if (selection) {
        selection.removeAllRanges();
        selection.addRange(range);
    }
    editable.dispatchEvent(new Event('input', { bubbles: true }));
}


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
        scrollChatToBottom();
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

// HTML для эхо-сообщения: без кнопки правки, которая есть только в поле ввода.
function buildEchoHtml(input) {
    const clone = input.cloneNode(true);
    clone.querySelectorAll('.chip-edit').forEach(node => node.remove());
    // Состояния правки относятся только к полю ввода и не должны попадать в сообщение.
    clone.querySelectorAll('.chip-editable, .chip-edited, .chip-editing').forEach(node => {
        node.classList.remove('chip-editable', 'chip-edited', 'chip-editing');
    });
    return clone.innerHTML.trim();
}

// --- 2. ЛОГИКА ЧАТА ---
function sendMessage() {
    const text = inputField.innerHTML.trim();
    if (!text || isWorking) return;

    let serialized;
    try {
        serialized = window.Atomix.ChipsController.serialize(inputField);
    } catch (error) {
        appendMessage('ai', error.message);
        return;
    }
    if (!serialized.prompt) return;

    inputField.querySelectorAll('.bim-chip').forEach(chip => {
        const chipId = chip.getAttribute('data-chip-id');
        if (!/^chip_[1-9]\d*$/.test(chipId || '')) return;
        const payload = window.Atomix.ChipsController.getPayload(chip);
        if (payload && payload.chipId === chipId) {
            // Оригинал храним только для тултипа; в ИИ уходит подтверждённое значение.
            chatChips.set(chipId, { ...payload, originalTooltip: chip._originalTooltip });
        }
    });

    appendMessage('user', buildEchoHtml(inputField));
    chatTurn += 1;
    thinkingMsgId = appendThinkingMessage();
    setBusy(true);

    // 3. Отправляем строго структурированный пакет в C#
    sendAction('chat_request', {
        payload: {
            prompt: serialized.prompt,
            chipIds: serialized.chipIds,
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
    ADD_ATTR: ['data-chip-id', 'data-tooltip'],
    ALLOW_DATA_ATTR: false,
    ALLOW_ARIA_ATTR: false,
    SAFE_FOR_TEMPLATES: true
};

function sanitizeClipboardHtml(html) {
    return DOMPurify.sanitize(html, {
        ...messageSanitizeConfig,
        ADD_ATTR: [...messageSanitizeConfig.ADD_ATTR, 'data-alias']
    });
}

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
        if (!parent || parent.closest('code, pre, a, .alias-btn, .bim-chip')) continue;
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

function decorateChipReferences(container) {
    if (!window.Atomix.ChipsController) return;
    container.querySelectorAll('span[data-chip-id]').forEach(chip => {
        window.Atomix.ChipsController.restoreMessageTooltip(chip, chatChips);
    });
}

function renderMessageContent(container, text) {
    container.innerHTML = sanitizeMessageHtml(text);

    // Restore chip tooltips from the UI-owned chip snapshot, never from AI HTML.
    decorateChipReferences(container);

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
            scrollChatToBottom();
        }
        return;
    }

    // Стрим ответа: снапшоты накопленного текста перезаписывают пузырь «думаю».
    if (data.phase === 'stream' && thinkingMsgId) {
        // Устаревший/отменённый ход уже отфильтрован выше (isStaleOrCancelledReply).
        const msgEl = document.getElementById(thinkingMsgId);
        if (!msgEl) return;
        if (!data.content) return;
        msgEl.classList.add('streaming-state');
        let label = msgEl.querySelector('.streaming-label');
        if (!label) {
            msgEl.replaceChildren();               // убрать shimmer-«thinking»
            label = document.createElement('span');
            label.className = 'streaming-label';
            msgEl.appendChild(label);
        }
        // Cancel после начала ответа не нужен: ответ уже не остановить.
        const cancel = msgEl.querySelector('.cancel-btn-link');
        if (cancel) cancel.remove();
        label.textContent = data.content;           // без markdown: перерисовка на каждый снапшот
        scrollChatToBottom();
        return;
    }

    setBusy(false);
    if (thinkingMsgId && data.role === 'ai') {
        const msgEl = document.getElementById(thinkingMsgId);
        if (msgEl) {
            msgEl.classList.remove('thinking-state', 'streaming-state');
            renderMessageContent(msgEl, data.content);
            initAliasButtons(msgEl);
            if (data.final_report_created === true) addRating(msgEl);
            scrollChatToBottom();
        }
        thinkingMsgId = null;
    } else {
        // Если это обычное сообщение (не из состояния "thinking")
        appendMessage(data.role, data.content, data.role === 'ai' && data.final_report_created === true);
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
    scrollChatToBottom();
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
    scrollChatToBottom();
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

    if (role === 'user') {
        const label = window.Atomix.t('chat.copyMessage', 'Copy message');
        const row = document.createElement('div');
        row.className = 'chat-user-message';
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'copy-user-message';
        button.setAttribute('aria-label', label);
        button.title = label;
        button.dataset.tooltip = label;
        button.innerHTML = `
            <svg class="copy-icon-copy" aria-hidden="true" viewBox="0 0 24 24">
                <rect x="8" y="8" width="12" height="12" rx="2"></rect>
                <path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2"></path>
            </svg>
            <svg class="copy-icon-check" aria-hidden="true" viewBox="0 0 24 24">
                <path d="m5 12 4 4L19 6"></path>
            </svg>`;
        row.append(button, msgDiv);
        chatContainer.appendChild(row);
    } else {
        chatContainer.appendChild(msgDiv);
    }
    scrollChatToBottom();
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

    container.querySelectorAll('.bim-chip').forEach(chip => {
        chip.addEventListener('mousedown', (e) => {
            e.preventDefault();
            e.stopPropagation();

            window.Atomix.ChipsController.insertChip(chip);

            chip.style.background = 'var(--accent)';
            chip.style.color = '#fff';
            setTimeout(() => {
                chip.style.background = '';
                chip.style.color = '';
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
        <span class="rate-btn" onclick="const p=this.parentElement; const ok=p.querySelector('.rating-success'); p.innerHTML=\`<span data-tooltip='Marked as solution' data-i18n-tooltip='rate.markedSolution'><svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' fill='currentColor' class='bi bi-hand-thumbs-up' viewBox='0 0 16 16'><path d='M8.864.046C7.908-.193 7.02.53 6.956 1.466c-.072 1.051-.23 2.016-.428 2.59-.125.36-.479 1.013-1.04 1.639-.557.623-1.282 1.178-2.131 1.41C2.685 7.288 2 7.87 2 8.72v4.001c0 .845.682 1.464 1.448 1.545 1.07.114 1.564.415 2.068.723l.048.03c.272.165.578.348.97.484.397.136.861.217 1.466.217h3.5c.937 0 1.599-.477 1.934-1.064a1.86 1.86 0 0 0 .254-.912c0-.152-.023-.312-.077-.464.201-.263.38-.578.488-.901.11-.33.172-.762.004-1.149.069-.13.12-.269.159-.403.077-.27.113-.568.113-.857 0-.288-.036-.585-.113-.856a2 2 0 0 0-.138-.362 1.9 1.9 0 0 0 .234-1.734c-.206-.592-.682-1.1-1.2-1.272-.847-.282-1.803-.276-2.516-.211a10 10 0 0 0-.443.05 9.4 9.4 0 0 0-.062-4.509A1.38 1.38 0 0 0 9.125.111zM11.5 14.721H8c-.51 0-.863-.069-1.14-.164-.281-.097-.506-.228-.776-.393l-.04-.024c-.555-.339-1.198-.731-2.49-.868-.333-.036-.554-.29-.554-.55V8.72c0-.254.226-.543.62-.65 1.095-.3 1.977-.996 2.614-1.708.635-.71 1.064-1.475 1.238-1.978.243-.7.407-1.768.482-2.85.025-.362.36-.594.667-.518l.262.066c.16.04.258.143.288.255a8.34 8.34 0 0 1-.145 4.725.5.5 0 0 0 .595.644l.003-.001.014-.003.058-.014a9 9 0 0 1 1.036-.157c.663-.06 1.457-.054 2.11.164.175.058.45.3.57.65.107.308.087.67-.266 1.022l-.353.353.353.354c.043.043.105.141.154.315.048.167.075.37.075.581 0 .212-.027.414-.075.582-.05.174-.111.272-.154.315l-.353.353.353.354c.047.047.109.177.005.488a2.2 2.2 0 0 1-.505.805l-.353.353.353.354c.006.005.041.05.041.17a.9.9 0 0 1-.121.416c-.165.288-.503.56-1.066.56z'/></svg></span>\`; if(ok) p.appendChild(ok); window.Atomix.I18n.apply(p)">
            <span data-tooltip="Mark as solution" data-i18n-tooltip="rate.markSolution"><svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" class="bi bi-hand-thumbs-up" viewBox="0 0 16 16">
                <path d="M8.864.046C7.908-.193 7.02.53 6.956 1.466c-.072 1.051-.23 2.016-.428 2.59-.125.36-.479 1.013-1.04 1.639-.557.623-1.282 1.178-2.131 1.41C2.685 7.288 2 7.87 2 8.72v4.001c0 .845.682 1.464 1.448 1.545 1.07.114 1.564.415 2.068.723l.048.03c.272.165.578.348.97.484.397.136.861.217 1.466.217h3.5c.937 0 1.599-.477 1.934-1.064a1.86 1.86 0 0 0 .254-.912c0-.152-.023-.312-.077-.464.201-.263.38-.578.488-.901.11-.33.172-.762.004-1.149.069-.13.12-.269.159-.403.077-.27.113-.568.113-.857 0-.288-.036-.585-.113-.856a2 2 0 0 0-.138-.362 1.9 1.9 0 0 0 .234-1.734c-.206-.592-.682-1.1-1.2-1.272-.847-.282-1.803-.276-2.516-.211a10 10 0 0 0-.443.05 9.4 9.4 0 0 0-.062-4.509A1.38 1.38 0 0 0 9.125.111zM11.5 14.721H8c-.51 0-.863-.069-1.14-.164-.281-.097-.506-.228-.776-.393l-.04-.024c-.555-.339-1.198-.731-2.49-.868-.333-.036-.554-.29-.554-.55V8.72c0-.254.226-.543.62-.65 1.095-.3 1.977-.996 2.614-1.708.635-.71 1.064-1.475 1.238-1.978.243-.7.407-1.768.482-2.85.025-.362.36-.594.667-.518l.262.066c.16.04.258.143.288.255a8.34 8.34 0 0 1-.145 4.725.5.5 0 0 0 .595.644l.003-.001.014-.003.058-.014a9 9 0 0 1 1.036-.157c.663-.06 1.457-.054 2.11.164.175.058.45.3.57.65.107.308.087.67-.266 1.022l-.353.353.353.354c.043.043.105.141.154.315.048.167.075.37.075.581 0 .212-.027.414-.075.582-.05.174-.111.272-.154.315l-.353.353.353.354c.047.047.109.177.005.488a2.2 2.2 0 0 1-.505.805l-.353.353.353.354c.006.005.041.05.041.17a.9.9 0 0 1-.121.416c-.165.288-.503.56-1.066.56z"/>
            </svg></span>
        </span>
        
        <!-- Кнопка Палец Вниз (Дизлайк) -->
        <span class="rate-btn" onclick="const p=this.parentElement; const ok=p.querySelector('.rating-success'); p.innerHTML=\`<span data-tooltip='Marked as error' data-i18n-tooltip='rate.markedError'><svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' fill='currentColor' class='bi bi-hand-thumbs-down-flipped-x' viewBox='0 0 16 16'><g transform='translate(16, 16) scale(-1, -1)'><path d='M8.864.046C7.908-.193 7.02.53 6.956 1.466c-.072 1.051-.23 2.016-.428 2.59-.125.36-.479 1.013-1.04 1.639-.557.623-1.282 1.178-2.131 1.41C2.685 7.288 2 7.87 2 8.72v4.001c0 .845.682 1.464 1.448 1.545 1.07.114 1.564.415 2.068.723l.048.03c.272.165.578.348.97.484.397.136.861.217 1.466.217h3.5c.937 0 1.599-.477 1.934-1.064a1.86 1.86 0 0 0 .254-.912c0-.152-.023-.312-.077-.464.201-.263.38-.578.488-.901.11-.33.172-.762.004-1.149.069-.13.12-.269.159-.403.077-.27.113-.568.113-.857 0-.288-.036-.585-.113-.856a2 2 0 0 0-.138-.362 1.9 1.9 0 0 0 .234-1.734c-.206-.592-.682-1.1-1.2-1.272-.847-.282-1.803-.276-2.516-.211a10 10 0 0 0-.443.05 9.4 9.4 0 0 0-.062-4.509A1.38 1.38 0 0 0 9.125.111zM11.5 14.721H8c-.51 0-.863-.069-1.14-.164-.281-.097-.506-.228-.776-.393l-.04-.024c-.555-.339-1.198-.731-2.49-.868-.333-.036-.554-.29-.554-.55V8.72c0-.254.226-.543.62-.65 1.095-.3 1.977-.996 2.614-1.708.635-.71 1.064-1.475 1.238-1.978.243-.7.407-1.768.482-2.85.025-.362.36-.594.667-.518l.262.066c.16.04.258.143.288.255a8.34 8.34 0 0 1-.145 4.725.5.5 0 0 0 .595.644l.003-.001.014-.003.058-.014a9 9 0 0 1 1.036-.157c.663-.06 1.457-.054 2.11.164.175.058.45.3.57.65.107.308.087.67-.266 1.022l-.353.353.353.354c.043.043.105.141.154.315.048.167.075.37.075.581 0 .212-.027.414-.075.582-.05.174-.111.272-.154.315l-.353.353.353.354c.047.047.109.177.005.488a2.2 2.2 0 0 1-.505.805l-.353.353.353.354c.006.005.041.05.041.17a.9.9 0 0 1-.121.416c-.165.288-.503.56-1.066.56z'/></g></svg></span>\`; if(ok) p.appendChild(ok); window.Atomix.I18n.apply(p)">
            <span data-tooltip="Mark as error" data-i18n-tooltip="rate.markError"><svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" class="bi bi-hand-thumbs-down-flipped-x" viewBox="0 0 16 16">
              <!-- Оборачиваем path в группу и зеркалим по горизонтали относительно сетки 16x16 -->
              <g transform="translate(16, 16) scale(-1, -1)">
                <path d="M8.864.046C7.908-.193 7.02.53 6.956 1.466c-.072 1.051-.23 2.016-.428 2.59-.125.36-.479 1.013-1.04 1.639-.557.623-1.282 1.178-2.131 1.41C2.685 7.288 2 7.87 2 8.72v4.001c0 .845.682 1.464 1.448 1.545 1.07.114 1.564.415 2.068.723l.048.03c.272.165.578.348.97.484.397.136.861.217 1.466.217h3.5c.937 0 1.599-.477 1.934-1.064a1.86 1.86 0 0 0 .254-.912c0-.152-.023-.312-.077-.464.201-.263.38-.578.488-.901.11-.33.172-.762.004-1.149.069-.13.12-.269.159-.403.077-.27.113-.568.113-.857 0-.288-.036-.585-.113-.856a2 2 0 0 0-.138-.362 1.9 1.9 0 0 0 .234-1.734c-.206-.592-.682-1.1-1.2-1.272-.847-.282-1.803-.276-2.516-.211a10 10 0 0 0-.443.05 9.4 9.4 0 0 0-.062-4.509A1.38 1.38 0 0 0 9.125.111zM11.5 14.721H8c-.51 0-.863-.069-1.14-.164-.281-.097-.506-.228-.776-.393l-.04-.024c-.555-.339-1.198-.731-2.49-.868-.333-.036-.554-.29-.554-.55V8.72c0-.254.226-.543.62-.65 1.095-.3 1.977-.996 2.614-1.708.635-.71 1.064-1.475 1.238-1.978.243-.7.407-1.768.482-2.85.025-.362.36-.594.667-.518l.262.066c.16.04.258.143.288.255a8.34 8.34 0 0 1-.145 4.725.5.5 0 0 0 .595.644l.003-.001.014-.003.058-.014a9 9 0 0 1 1.036-.157c.663-.06 1.457-.054 2.11.164.175.058.45.3.57.65.107.308.087.67-.266 1.022l-.353.353.353.354c.043.043.105.141.154.315.048.167.075.37.075.581 0 .212-.027.414-.075.582-.05.174-.111.272-.154.315l-.353.353.353.354c.047.047.109.177.005.488a2.2 2.2 0 0 1-.505.805l-.353.353.353.354c.006.005.041.05.041.17a.9.9 0 0 1-.121.416c-.165.288-.503.56-1.066.56z"/>
              </g>
            </svg></span>
        </span>

        <!-- Иконка успеха: зелёная галочка -->
        <span class="rating-success" aria-hidden="true">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" stroke-linejoin="round" viewBox="0 0 16 16">
                <circle cx="8" cy="8" r="6.5"/>
                <path d="m5 8.2 2 2 4-4.4"/>
            </svg>
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
        const tipHeight = tip.offsetHeight;
        let left = rect.left + (rect.width / 2);
        let top = rect.top - tipHeight - 8;
        if (top < 8) top = rect.bottom + 8;
        const tipWidth = tip.offsetWidth;
        if (left + (tipWidth / 2) > window.innerWidth - 10) {
            left = window.innerWidth - (tipWidth / 2) - 10;
        }
        if (left - (tipWidth / 2) < 10) {
            left = 10 + (tipWidth / 2);
        }
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
