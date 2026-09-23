// Portfolio LiveChat sample — client-side chat logic.
//
// Central safety rule of this file: user-entered text and any locally generated error text are
// ALWAYS rendered with textContent. The ONLY value ever inserted as HTML is the output of
// DOMPurify.sanitize(marked.parse(reply)), and only when that reply came from a successful
// backend response. Search this file for the single `innerHTML =` assignment near the bottom
// of renderAssistantReply — that is the only HTML insertion point in the whole file.
(() => {
  'use strict';

  const ENDPOINT = '/api/chat/send';
  // Keep in sync with ChatOptions.MaxMessageLength in the backend (Options/ChatOptions.cs) and
  // with the textarea's maxlength attribute in index.html. This is a static demo page, so there
  // is no server-rendered configuration; all three must be updated together if the limit changes.
  const MAX_MESSAGE_LENGTH = 1000;
  const REQUEST_TIMEOUT_MS = 45000;
  const GENERIC_ERROR_TEXT = 'Something went wrong. Please try again in a moment.';

  // Explicit, intentionally small allowlist. Anything not listed here is removed by DOMPurify
  // because ALLOWED_TAGS puts it in allowlist mode; FORBID_TAGS is redundant with that but is
  // listed anyway so the exclusions are visible in the code, not just implied by omission.
  const SANITIZER_CONFIG = {
    ALLOWED_TAGS: ['p', 'br', 'strong', 'em', 'ul', 'ol', 'li', 'code', 'pre', 'blockquote', 'a'],
    ALLOWED_ATTR: ['href'],
    ALLOW_DATA_ATTR: false,
    // Absolute http(s) URLs, or a same-origin root-relative path (single leading slash).
    // Explicitly rejects javascript:, data:, vbscript: and protocol-relative (//host) URLs.
    ALLOWED_URI_REGEXP: /^(?:https?:|\/(?!\/))/i,
    FORBID_TAGS: ['img', 'iframe', 'script', 'style', 'form', 'input', 'button', 'svg', 'math', 'object', 'embed'],
    FORBID_ATTR: ['style', 'onclick', 'onerror', 'onload']
  };

  let sanitizerHookRegistered = false;
  function ensureSanitizerHook() {
    if (sanitizerHookRegistered || typeof DOMPurify === 'undefined') return;
    DOMPurify.addHook('afterSanitizeAttributes', (node) => {
      if (node.tagName === 'A') {
        node.setAttribute('target', '_blank');
        node.setAttribute('rel', 'noopener noreferrer');
      }
    });
    sanitizerHookRegistered = true;
  }

  // Renders an assistant reply as sanitized Markdown into `container`. Falls back to plain text
  // (via textContent, not innerHTML) if the Markdown/sanitizer libraries are unavailable or throw
  // — the chat must never depend on a CDN or a third-party library being present to stay safe.
  function renderAssistantReply(container, text) {
    if (typeof marked === 'undefined' || typeof DOMPurify === 'undefined' || !DOMPurify.isSupported) {
      container.textContent = text;
      return;
    }
    ensureSanitizerHook();
    let html;
    try {
      html = DOMPurify.sanitize(marked.parse(text), SANITIZER_CONFIG);
    } catch {
      container.textContent = text;
      return;
    }
    // The only HTML insertion point in this file. `html` is DOMPurify's sanitized output,
    // never the raw `text` and never marked's raw output.
    container.innerHTML = html;
  }

  function appendMessage(log, role, text, kind) {
    const entry = document.createElement('div');
    entry.className = 'chat-message chat-message--' + (role === 'user' ? 'user' : 'assistant');
    if (role === 'error') {
      entry.classList.add('chat-message--error');
    }

    const label = document.createElement('span');
    label.className = 'chat-message__label';
    label.textContent = role === 'user' ? 'You' : 'Assistant';
    entry.appendChild(label);

    const body = document.createElement('div');
    body.className = 'chat-message__body';

    if (kind === 'markdown') {
      renderAssistantReply(body, text);
    } else {
      // User messages and every locally generated / non-2xx message: always textContent.
      body.textContent = text;
    }

    entry.appendChild(body);
    log.appendChild(entry);
    log.scrollTop = log.scrollHeight;
    return entry;
  }

  function initChat(doc) {
    const form = doc.getElementById('chat-form');
    const input = doc.getElementById('chat-input');
    const sendButton = doc.getElementById('chat-send');
    const log = doc.getElementById('chat-log');

    let busy = false;

    async function sendMessage() {
      if (busy) return;

      const message = input.value.trim();
      if (!message) return;

      busy = true;
      sendButton.disabled = true;
      sendButton.setAttribute('aria-busy', 'true');
      input.value = '';

      appendMessage(log, 'user', message, 'text');

      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

      try {
        const response = await fetch(ENDPOINT, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ message }),
          signal: controller.signal
        });

        let data;
        try {
          data = await response.json();
        } catch {
          appendMessage(log, 'error', GENERIC_ERROR_TEXT, 'text');
          return;
        }

        const rawReply = data && typeof data.reply === 'string' ? data.reply : '';
        if (rawReply.trim() === '') {
          appendMessage(log, 'error', GENERIC_ERROR_TEXT, 'text');
          return;
        }

        // Markdown rendering only for a genuinely successful response; every other status
        // (400/502/503/504 in this sample) still carries a legitimate, server-localized
        // message and is shown as plain text, not treated as a client-side failure.
        appendMessage(log, 'assistant', rawReply, response.ok ? 'markdown' : 'text');
      } catch {
        // Network failure, or the 45-second AbortController timeout firing.
        appendMessage(log, 'error', GENERIC_ERROR_TEXT, 'text');
      } finally {
        clearTimeout(timer);
        busy = false;
        sendButton.disabled = false;
        sendButton.setAttribute('aria-busy', 'false');
        input.focus();
      }
    }

    form.addEventListener('submit', (event) => {
      event.preventDefault();
      sendMessage();
    });

    input.addEventListener('keydown', (event) => {
      // Enter sends; Shift+Enter falls through to the textarea's default newline behavior.
      // isComposing / keyCode 229 guards against sending on the Enter keystroke that confirms
      // an IME composition (used by Chinese/Japanese/Korean input methods, among others).
      if (event.key === 'Enter' && !event.shiftKey && !event.isComposing && event.keyCode !== 229) {
        event.preventDefault();
        form.requestSubmit();
      }
    });

    return { sendMessage };
  }

  if (typeof document !== 'undefined') {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', () => initChat(document));
    } else {
      initChat(document);
    }
  }

  // Exposed only so the frontend test harness (tests/frontend/) can drive this module through
  // its real DOM event paths without scraping globals or re-implementing any logic. Not used by
  // index.html itself.
  if (typeof module !== 'undefined' && module.exports) {
    module.exports = { initChat, renderAssistantReply, SANITIZER_CONFIG };
  }
})();
