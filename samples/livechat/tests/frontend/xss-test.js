// Isolated Node/jsdom security and robustness test harness for the LiveChat sample frontend.
//
// This is NOT part of the .NET build or solution. It has its own package.json in this folder
// with jsdom as its only dependency. Run it with:
//
//   cd samples/livechat/tests/frontend
//   npm install
//   npm test
//
// It loads the REAL index.html, the REAL vendored marked/DOMPurify, and the REAL js/chat.js from
// wwwroot, and drives them through actual DOM events (typing, Enter, button clicks) with a
// mocked `fetch` — it does not re-implement or duplicate any sanitization logic itself.
'use strict';

const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const WWWROOT = path.resolve(__dirname, '..', '..', 'src', 'Portfolio.LiveChat', 'wwwroot');
const indexHtml = fs.readFileSync(path.join(WWWROOT, 'index.html'), 'utf8');
const markedSrc = fs.readFileSync(path.join(WWWROOT, 'vendor', 'marked.min.js'), 'utf8');
const purifySrc = fs.readFileSync(path.join(WWWROOT, 'vendor', 'purify.min.js'), 'utf8');
const chatSrc = fs.readFileSync(path.join(WWWROOT, 'js', 'chat.js'), 'utf8');

let pass = 0;
let fail = 0;
const failures = [];

function check(name, condition, detail) {
  if (condition) {
    pass++;
    console.log('PASS  ' + name);
  } else {
    fail++;
    failures.push(name);
    console.log('FAIL  ' + name + (detail ? '  <-- ' + detail : ''));
  }
}

function tick(ms = 10) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// Builds a fresh, fully isolated window for each test: real markup, real vendored libraries,
// real chat.js, with only `fetch` replaced by the caller-supplied implementation.
//
// jsdom finishes constructing the Document synchronously with readyState still "loading"; it
// fires "DOMContentLoaded" asynchronously afterwards (timing is not guaranteed to be immediate).
// chat.js correctly waits for that event itself when needed — a real browser behaves the same
// way — so this harness must wait for it too before dispatching any DOM events, otherwise a
// dispatch can race chat.js's own listener and be silently dropped before initChat() attaches
// its handlers.
async function createChatWindow(fetchImpl) {
  const dom = new JSDOM(indexHtml, {
    runScripts: 'outside-only',
    url: 'http://localhost/',
    pretendToBeVisual: true
  });
  const window = dom.window;
  const errors = [];
  window.addEventListener('error', (e) => errors.push(String(e.message || e.error)));

  if (typeof window.AbortController === 'undefined') {
    window.AbortController = AbortController;
  }
  window.fetch = fetchImpl;

  window.eval(markedSrc);
  window.eval(purifySrc);
  window.eval(chatSrc); // registers its own DOMContentLoaded listener if the document is still loading

  if (window.document.readyState === 'loading') {
    await new Promise((resolve) => window.document.addEventListener('DOMContentLoaded', resolve));
  }

  return { window, document: window.document, errors };
}

function jsonResponse(status, bodyObj) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => bodyObj
  };
}

function malformedJsonResponse(status) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => {
      throw new SyntaxError('Unexpected token');
    }
  };
}

async function sendUserMessage({ document }, text) {
  const input = document.getElementById('chat-input');
  const form = document.getElementById('chat-form');
  input.value = text;
  form.dispatchEvent(new document.defaultView.Event('submit', { bubbles: true, cancelable: true }));
  await tick(30);
}

function lastAssistantBubble(document) {
  const log = document.getElementById('chat-log');
  return log.lastElementChild ? log.lastElementChild.querySelector('.chat-message__body') : null;
}

const DANGEROUS = /<(script|img|iframe|svg|math|object|embed|style|form|input|button)\b|on\w+\s*=|javascript:|style\s*=/i;

async function main() {
  // ---------------------------------------------------------------------------------------
  // 1-12: malicious payload categories, delivered as an assistant reply (the only path that
  // ever runs Markdown + sanitizer). Each payload is checked twice: the rendered HTML must not
  // contain the dangerous construct, AND no `error` event (i.e. no thrown/uncaught exception)
  // may have reached the window while rendering it.
  // ---------------------------------------------------------------------------------------
  const payloads = [
    ['1. <script>', '<script>alert(1)</script>'],
    ['2. <img onerror>', '<img src=x onerror=alert(1)>'],
    ['3. javascript: link', '[click](javascript:alert(1))'],
    ['4. mixed-case JaVaScRiPt:', '<a href="JaVaScRiPt:alert(1)">x</a>'],
    ['5. event attribute on allowed tag', '<a href="https://example.com" onclick="alert(1)" onerror="alert(2)">link</a>'],
    ['6. style attribute', '<p style="background:url(javascript:alert(1))">x</p>'],
    ['7. iframe', '<iframe src="javascript:alert(1)"></iframe>'],
    ['8. svg payload', '<svg onload=alert(1)><circle/></svg>'],
    ['9. math payload', '<math><mi xlink:href="javascript:alert(1)">x</mi></math>'],
    ['10. data: link', '<a href="data:text/html,<script>alert(1)</script>">data</a>'],
    ['11. protocol-relative link', '<a href="//evil.example/phish">click</a>'],
    ['12. entity-encoded javascript:', '<a href="&#x6A;avascript:alert(1)">hex</a>']
  ];

  for (const [label, payload] of payloads) {
    const ctx = await createChatWindow(async () => jsonResponse(200, { reply: payload }));
    await sendUserMessage(ctx, 'trigger');
    const bubble = lastAssistantBubble(ctx.document);
    const html = bubble ? bubble.innerHTML : '';
    check(label + ' -> sanitized (no dangerous construct)', !DANGEROUS.test(html), html);
    check(label + ' -> no uncaught window error', ctx.errors.length === 0, ctx.errors.join('; '));
  }

  // Control: confirm the payloads are ACTUALLY dangerous through marked alone (no sanitizer),
  // so the checks above are not vacuously passing against already-harmless input.
  {
    const ctx = await createChatWindow(async () => jsonResponse(200, { reply: 'noop' }));
    const raw = ctx.window.marked;
    let dangerousCount = 0;
    for (const [, payload] of payloads) {
      if (DANGEROUS.test(raw.parse(payload))) dangerousCount++;
    }
    check('control: unsanitized marked output is dangerous for most payloads (' + dangerousCount + '/' + payloads.length + ')', dangerousCount >= 8);
  }

  // ---------------------------------------------------------------------------------------
  // Legitimate Markdown must still render.
  // ---------------------------------------------------------------------------------------
  {
    const ctx = await createChatWindow(async () => jsonResponse(200, {
      reply: '**bold text**\n\n- item one\n- item two\n\n`inline code`\n\n[safe link](https://example.com/docs)'
    }));
    await sendUserMessage(ctx, 'formatting please');
    const bubble = lastAssistantBubble(ctx.document);
    const html = bubble.innerHTML;
    check('bold Markdown survives', /<strong>bold text<\/strong>/.test(html), html);
    check('list survives', /<ul>/.test(html) && /<li>item one<\/li>/.test(html), html);
    check('inline code survives', /<code>inline code<\/code>/.test(html), html);
    const link = bubble.querySelector('a');
    check('safe https link kept with correct href', !!link && link.getAttribute('href') === 'https://example.com/docs', html);
    check('safe link gets target=_blank and rel=noopener noreferrer', !!link && link.getAttribute('target') === '_blank' && link.getAttribute('rel') === 'noopener noreferrer', html);
  }

  // ---------------------------------------------------------------------------------------
  // User-entered text (even HTML-shaped) must render as literal text, never parsed.
  // ---------------------------------------------------------------------------------------
  {
    const ctx = await createChatWindow(async () => jsonResponse(200, { reply: 'ok' }));
    await sendUserMessage(ctx, '<img src=x onerror=alert(1)> & <b>bold</b>');
    const log = ctx.document.getElementById('chat-log');
    const userBubble = log.firstElementChild.querySelector('.chat-message__body');
    check('user text has no parsed elements', userBubble.querySelector('img, b') === null, userBubble.innerHTML);
    check('user text visible verbatim via textContent', userBubble.textContent.includes('<img src=x onerror=alert(1)> & <b>bold</b>'), userBubble.textContent);
  }

  // ---------------------------------------------------------------------------------------
  // Non-2xx application responses (400/502/503/504-shaped) still carry a legitimate {reply};
  // must render as PLAIN TEXT, never as Markdown/HTML, even if the text contained markup.
  // ---------------------------------------------------------------------------------------
  for (const status of [400, 502, 503, 504]) {
    const ctx = await createChatWindow(async () => jsonResponse(status, { reply: '<b>Unavailable</b>, try later' }));
    await sendUserMessage(ctx, 'hello');
    const bubble = lastAssistantBubble(ctx.document);
    check('HTTP ' + status + ' reply rendered as plain text', bubble.querySelector('b') === null && bubble.textContent.includes('Unavailable'), bubble.innerHTML);
  }

  // ---------------------------------------------------------------------------------------
  // Failure handling: malformed JSON, missing/blank/non-string reply, network error — all must
  // fall back to the generic error message, with no uncaught exception.
  // ---------------------------------------------------------------------------------------
  const failureCases = [
    ['malformed JSON body', async () => malformedJsonResponse(200)],
    ['missing reply field', async () => jsonResponse(200, { note: 'no reply here' })],
    ['blank reply', async () => jsonResponse(200, { reply: '   ' })],
    ['non-string reply', async () => jsonResponse(200, { reply: 12345 })],
    ['null body', async () => jsonResponse(200, null)],
    ['network failure', async () => { throw new TypeError('Failed to fetch'); }]
  ];
  for (const [label, impl] of failureCases) {
    const ctx = await createChatWindow(impl);
    await sendUserMessage(ctx, 'hello');
    const bubble = lastAssistantBubble(ctx.document);
    check(label + ' -> generic error shown, no exception', bubble.textContent.includes('Something went wrong') && ctx.errors.length === 0, bubble.innerHTML + ' | errors: ' + ctx.errors.join(';'));
  }

  // ---------------------------------------------------------------------------------------
  // Duplicate-send prevention, Enter/Shift+Enter, IME guard, focus return, disabled state.
  // ---------------------------------------------------------------------------------------
  {
    let calls = 0;
    let release;
    const gate = new Promise((r) => { release = r; });
    const ctx = await createChatWindow(async () => { calls++; await gate; return jsonResponse(200, { reply: 'done' }); });
    const { document, window } = ctx;
    const input = document.getElementById('chat-input');
    const button = document.getElementById('chat-send');
    const form = document.getElementById('chat-form');

    input.value = 'first message';
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    await tick(10);
    check('button becomes disabled while in flight', button.disabled === true);

    // A second attempt while busy (button click AND direct submit) must not add a second call.
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    input.value = 'second message';
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    await tick(10);
    check('duplicate submissions while busy are ignored (exactly one fetch call)', calls === 1, 'calls=' + calls);

    release();
    await tick(30);
    check('button re-enabled after completion', button.disabled === false);
  }

  {
    let calls = 0;
    const ctx = await createChatWindow(async () => { calls++; return jsonResponse(200, { reply: 'ok' }); });
    const { document, window } = ctx;
    const input = document.getElementById('chat-input');

    input.value = 'line one';
    input.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, bubbles: true, cancelable: true }));
    await tick(10);
    check('Shift+Enter does not send', calls === 0, 'calls=' + calls);

    input.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true, cancelable: true }));
    await tick(10);
    check('Enter during IME composition does not send', calls === 0, 'calls=' + calls);

    input.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', keyCode: 229, bubbles: true, cancelable: true }));
    await tick(10);
    check('Enter with keyCode 229 does not send', calls === 0, 'calls=' + calls);

    input.value = 'real message';
    input.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await tick(20);
    check('plain Enter sends exactly once', calls === 1, 'calls=' + calls);
  }

  {
    const ctx = await createChatWindow(async () => jsonResponse(200, { reply: 'ok' }));
    const { document } = ctx;
    const input = document.getElementById('chat-input');
    await sendUserMessage(ctx, 'hello');
    check('focus returns to the input after completion', document.activeElement === input);
  }

  {
    const ctx = await createChatWindow(async () => jsonResponse(200, { reply: 'ok' }));
    await sendUserMessage(ctx, '   ');
    check('empty/whitespace-only input is never sent', ctx.document.getElementById('chat-log').children.length === 0);
  }

  // ---------------------------------------------------------------------------------------
  // Static/document-level checks.
  // ---------------------------------------------------------------------------------------
  {
    const ctx = await createChatWindow(async () => jsonResponse(200, { reply: 'ok' }));
    const input = ctx.document.getElementById('chat-input');
    check('input has maxlength=1000', input.getAttribute('maxlength') === '1000');
    check('input has an associated label', !!ctx.document.querySelector('label[for="chat-input"]'));
    check('send button has visible accessible text', ctx.document.getElementById('chat-send').textContent.trim().length > 0);
    check('log has role=log and aria-live=polite', ctx.document.getElementById('chat-log').getAttribute('role') === 'log' && ctx.document.getElementById('chat-log').getAttribute('aria-live') === 'polite');
  }

  // ---------------------------------------------------------------------------------------
  // Libraries unavailable: chat.js must degrade to plain text, never throw, never insert HTML.
  // ---------------------------------------------------------------------------------------
  {
    const dom = new JSDOM(indexHtml, { runScripts: 'outside-only', url: 'http://localhost/', pretendToBeVisual: true });
    const window = dom.window;
    const errors = [];
    window.addEventListener('error', (e) => errors.push(String(e.message || e.error)));
    if (typeof window.AbortController === 'undefined') window.AbortController = AbortController;
    window.fetch = async () => jsonResponse(200, { reply: '**bold** <img src=x onerror=alert(1)>' });
    // Deliberately DO NOT eval marked/purify — only chat.js.
    window.eval(chatSrc);
    if (window.document.readyState === 'loading') {
      await new Promise((resolve) => window.document.addEventListener('DOMContentLoaded', resolve));
    }
    await sendUserMessage({ document: window.document }, 'hi');
    const bubble = lastAssistantBubble(window.document);
    check('libraries missing -> falls back to plain text, no throw', bubble.querySelector('img') === null && bubble.textContent.includes('**bold**') && errors.length === 0, bubble.innerHTML + ' | ' + errors.join(';'));
  }

  console.log('\n' + pass + ' passed, ' + fail + ' failed');
  if (fail > 0) {
    console.log('Failed checks: ' + failures.join(', '));
    process.exitCode = 1;
  }
}

main().catch((err) => {
  console.error('HARNESS ERROR', err);
  process.exitCode = 2;
});
