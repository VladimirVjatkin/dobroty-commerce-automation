# Portfolio Sample: Hardened Chat Endpoint

Last updated: 2026-09-22

This is a **rewritten, standalone portfolio sample**, built specifically for this repository. It demonstrates the engineering patterns behind the LiveChat security-hardening work described in [../../docs/SECURITY_HARDENING.md](../../docs/SECURITY_HARDENING.md) and [../../portfolio/CASE_STUDY.md](../../portfolio/CASE_STUDY.md).

**It is not the production plugin.** It has no dependency on nopCommerce (`Nop.Core`, `Nop.Data`, `Nop.Services`, `Nop.Web`, or any nopCommerce package or project), no production configuration, no real endpoint URLs, and no customer data of any kind. Every file here was written fresh for this repository; the production source was not copied.

## What this demonstrates

**Backend:** a plain ASP.NET Core Web API (MVC-style controller + service + DI, not Minimal API endpoints) that accepts a chat message and forwards it to a configurable external endpoint, with the same category of server-side controls the production integration was hardened to have:

- Input validation (empty/whitespace rejected, length-limited, request-body-size-limited).
- An externally configured endpoint with **no hard-coded fallback URL** — a missing or invalid configuration fails safely (503) instead of silently defaulting anywhere.
- A bounded backend timeout on the outbound call, using `CancellationToken` throughout.
- A typed `HttpClient` registered through `IHttpClientFactory` (`AddHttpClient<IChatService, ChatService>()`), not a manually managed `HttpClient` instance.
- Explicit, distinct handling for: a non-success upstream response, a malformed (non-JSON) upstream response, a JSON response missing a usable `reply` field, an upstream timeout, and the *caller* disconnecting — the last two are deliberately told apart (see `ChatService.SendAsync`), because conflating "we gave up waiting" with "the client walked away" is a common and misleading bug.
- Safe technical logging: every log entry records outcome, HTTP status, duration and response size — never the message text, never the endpoint URL, never the upstream response body. This is enforced by an automated test, not just a stated intention (see [Backend test coverage](#backend-test-coverage)).

**Frontend:** a small static page, served by the same app, that demonstrates the client-side half of the same hardening story — most importantly, the safe `marked` → `DOMPurify` → sanitized-HTML rendering path for AI-generated replies, alongside the client-side robustness controls (duplicate-send prevention, timeouts, IME-safe input, accessible markup) that came out of the same real-world hardening effort. See [Frontend](#frontend) below.

## What is intentionally different from a production system

This sample is deliberately scoped down. The following are **not implemented here**, and are called out explicitly rather than glossed over:

- **Rate limiting.** There is no per-client or per-IP request budget on this endpoint.
- **Server-to-server authentication for the outbound call.** The sample does not sign or authenticate the request to the configured endpoint; in a real deployment, this is a planned improvement (see [../../docs/AI_ASSISTANT.md](../../docs/AI_ASSISTANT.md) and [../../docs/SECURITY_HARDENING.md](../../docs/SECURITY_HARDENING.md)).
- **Production identity handling.** There is no concept of a logged-in customer, session, or client identifier in this sample — the request model is a bare `{ "message": "..." }`, on purpose, to keep the sample focused on the transport/validation/sanitization pattern rather than reproducing identity logic.
- **Full observability.** Logging is structured but minimal; there is no metrics/tracing integration.
- **Deployment configuration.** There is no Dockerfile, CI pipeline, or environment-specific configuration here — see [../../docs/DEPLOYMENT_OVERVIEW.md](../../docs/DEPLOYMENT_OVERVIEW.md) for the deployment *process* this project follows elsewhere.
- **Retries and circuit breakers.** Deliberately not added — the production integration this is modeled on does not have them either, and adding resilience patterns that were never part of the verified production behavior would misrepresent what this sample demonstrates.
- **Content Security Policy.** Not configured here; a CSP is a page/server-wide concern and out of scope for a single sample page, even though it is a natural complement to the sanitization work described below.
- **Client-side identity/session state.** No `clientId`, no `localStorage` usage — see [Identity / session state](#identity--session-state) below for why.
- **Smaller sanitizer allowlist than production.** See [Sanitizer allowlist](#sanitizer-allowlist) below.

## Frontend

The sample now ships a small static frontend, served by the same ASP.NET Core app from `wwwroot/` (`index.html`, `css/chat.css`, `js/chat.js`, `vendor/`). It is a plain HTML/CSS/JavaScript page — no framework, no bundler, no build step. Every file can be read directly and understood top to bottom.

### End-to-end flow

```text
Browser
  |  types a message, presses Enter (or clicks Send)
  v
js/chat.js  --POST /api/chat/send-->  ASP.NET Core (ChatController)
                                          |
                                          v
                                    ChatService --> configured AI endpoint
                                          |
  <-------------- { "reply": "..." } or a mapped error --------------

Rendering the reply, in js/chat.js:
  reply string --> marked.parse() --> DOMPurify.sanitize(explicit allowlist) --> container.innerHTML
```

### Why user text and assistant text are rendered differently

This is the central design decision of the whole sample, so it is worth stating plainly:

- **The user's own message is rendered with `textContent`.** There is no reason to interpret a customer's own typed input as Markdown or HTML, and doing so would be a self-XSS risk for no benefit.
- **The assistant's reply is rendered as Markdown, then sanitized, then inserted as HTML.** The reply text comes from an external AI system (see [../../docs/AI_ASSISTANT.md](../../docs/AI_ASSISTANT.md)) — content the frontend does not control and must treat as untrusted, even though it is also the one place where Markdown formatting (bold, lists, code, links) is genuinely useful. `marked` produces HTML from that text; `DOMPurify` is what makes inserting that HTML safe, by stripping everything not on an explicit, small allowlist before it ever reaches the DOM.
- **Every locally generated error message is rendered with `textContent`.** Error text in this sample is always a fixed, generic string (see below) — never anything derived from a response body, a stack trace, or other internal detail — so there is nothing to sanitize, but the rule is still enforced by code, not just convention.

There is exactly **one** place in `chat.js` that assigns `innerHTML`, and it is fed only by `DOMPurify.sanitize(marked.parse(reply), ALLOWLIST)` — never the raw reply, never `marked`'s raw output. This is called out in a comment at both the top of the file and directly above that one line.

### Sanitizer allowlist

Deliberately small, and explicit rather than relying on DOMPurify's broader defaults:

| Setting | Value |
| --- | --- |
| Allowed tags | `p br strong em ul ol li code pre blockquote a` |
| Allowed attributes | `href` only |
| Allowed link schemes | `http:`, `https:`, or a same-origin root-relative path (`/foo`, not `//host/foo`) |
| Explicitly forbidden (redundant with the allowlist, listed for clarity) | `img iframe script style form input button svg math object embed`, plus the `style`/`onclick`/`onerror` attributes |
| Link handling | Every surviving `<a>` gets `target="_blank"` and `rel="noopener noreferrer"` added by a DOMPurify hook — the two are always set together |

This is intentionally smaller than the original production allowlist (no headings, no `hr`/`del`, no `title` attribute, no `mailto:`/`tel:`) — this sample favors an easy-to-audit minimal allowlist over feature parity with production.

### Client robustness

- `maxlength="1000"` on the input, matching the backend's `ChatOptions.MaxMessageLength`.
- The message is trimmed before sending; empty or whitespace-only input is never sent.
- A `busy` flag (checked at the top of the send function, independent of the button's `disabled` state) prevents a duplicate in-flight request regardless of whether it's triggered by another click or by pressing Enter again.
- **Enter sends; Shift+Enter inserts a newline** (a `<textarea>` is used specifically so this distinction is possible).
- IME-safe: an Enter keystroke that is part of composing text in an IME (checked via `event.isComposing` and the legacy `keyCode === 229`) does not send.
- The request carries a 45-second client-side `AbortController` timeout, independent of the backend's own 30-second timeout.
- `response.ok` is checked explicitly: a non-2xx response still renders its `reply` (as plain text, never Markdown), since the backend's own error responses are legitimate, localized, safe strings — not raw failures.
- A non-JSON response body, a missing/blank/non-string `reply` field, and a network error/timeout all fall back to one fixed, generic message. Backend details are never surfaced to the page.
- `finally` always clears the busy flag, re-enables the button, and returns focus to the input — on every path, including every error path.

### Identity / session state

This sample intentionally has **no `clientId`, no session identifier, and no `localStorage` usage at all**. The backend's request model is a bare `{ "message": "..." }` with no identity field (see [Models/ChatRequest.cs](src/Portfolio.LiveChat/Models/ChatRequest.cs)), so the frontend has nothing to attach. Production identity/session handling is a deliberate omission from this portfolio sample, not an oversight — recreating it here would add complexity without adding to what this sample is meant to demonstrate (see [../../docs/SECURITY_HARDENING.md](../../docs/SECURITY_HARDENING.md) for what identity-related work remains open even in production).

### Accessibility

- The message field is a real `<textarea>` with an associated `<label for="chat-input">`.
- The send button has visible, readable text ("Send"); its `aria-busy` state reflects whether a request is in flight.
- The conversation transcript (`#chat-log`) has `role="log"` and `aria-live="polite"`, so new messages are announced without needing to move focus.
- Fully keyboard-operable: the textarea and button are natively focusable, Enter/Shift+Enter behave as described above, and focus predictably returns to the input after every send attempt.
- No excessive ARIA: roles and attributes are used only where they add real information (the log region, the busy state), not decoratively.

## Project structure

```text
samples/livechat/
├── Portfolio.LiveChat.sln
├── THIRD_PARTY.md                    vendored frontend library versions, licenses, SHA-256
├── src/Portfolio.LiveChat/           the sample application
│   ├── Controllers/ChatController.cs
│   ├── Services/IChatService.cs       (also defines ChatResult / ChatResultStatus)
│   ├── Services/ChatService.cs
│   ├── Models/ChatRequest.cs
│   ├── Models/ChatResponse.cs
│   ├── Options/ChatOptions.cs
│   ├── Program.cs
│   ├── appsettings.Example.json
│   └── wwwroot/                      the static frontend demo
│       ├── index.html
│       ├── css/chat.css
│       ├── js/chat.js
│       └── vendor/                   marked, DOMPurify, and their license files (see THIRD_PARTY.md)
└── tests/
    ├── Portfolio.LiveChat.Tests/      backend automated tests (xUnit, part of the .NET solution)
    │   ├── ChatControllerTests.cs
    │   └── ChatServiceTests.cs
    └── frontend/                      frontend/XSS test harness (Node + jsdom, NOT part of the .NET solution)
        ├── package.json
        └── xss-test.js
```

## Configuration

The outbound endpoint is read from configuration only — never hard-coded, never defaulted to a real host:

```json
{
  "Chat": {
    "EndpointUrl": "https://example.invalid/webhook",
    "RequestTimeout": "00:00:30"
  }
}
```

Copy [`src/Portfolio.LiveChat/appsettings.Example.json`](src/Portfolio.LiveChat/appsettings.Example.json) to `appsettings.json` (or `appsettings.Development.json`) and point `Chat:EndpointUrl` at your own test endpoint before running the app. If you don't configure an endpoint, the app still starts and responds correctly — every chat request will simply and safely return `503`.

## How to build and test

From this directory (`samples/livechat/`), with the .NET 9 SDK installed:

```bash
dotnet restore Portfolio.LiveChat.sln
dotnet build Portfolio.LiveChat.sln --configuration Release
dotnet test Portfolio.LiveChat.sln --configuration Release
```

To run the app itself:

```bash
dotnet run --project src/Portfolio.LiveChat/Portfolio.LiveChat.csproj
```

Then, for example:

```bash
curl -X POST http://localhost:5000/api/chat/send \
  -H "Content-Type: application/json" \
  -d '{"message":"hello"}'
```

With no `Chat:EndpointUrl` configured, this returns `503` with `{"reply":"The assistant is temporarily unavailable. Please try again in a moment."}` — the safe-failure path, not a crash or a stack trace.

Open `http://localhost:5000/` (or whatever port `dotnet run` prints) in a browser to use the actual chat page.

## How to run the frontend / XSS tests

The frontend test harness is isolated from the .NET solution — it is plain Node.js with a single dependency (`jsdom`), kept in its own folder so it never touches the backend build:

```bash
cd tests/frontend
npm install
npm test
```

This loads the real `index.html`, the real vendored `marked`/`DOMPurify`, and the real `js/chat.js` into an isolated jsdom window per test case, and drives them through actual DOM events (typing, Enter, form submission) with a mocked `fetch` — it does not re-implement or duplicate the sanitization logic under test. See [Frontend/XSS test coverage](#frontendxss-test-coverage) below for exactly what it checks.

## Backend test coverage

`tests/Portfolio.LiveChat.Tests` contains 28 automated tests (xUnit), split by responsibility:

**`ChatControllerTests`** — validation and status mapping, calling the controller directly against a hand-written fake `IChatService` (no HTTP layer involved):
- Empty, whitespace and `null`-request messages are rejected with `400` and never reach the service.
- A message of exactly the maximum length (1000 characters) is accepted and forwarded; one character over is rejected.
- The message is trimmed before being forwarded to the service.
- Every `ChatResultStatus` value maps to the documented HTTP status (`200`, `503`, `504`, `502` ×3, `499`), and every response except `499` uses the same `{ "reply": "..." }` envelope shape.

**`ChatServiceTests`** — transport behavior, using a hand-written fake `HttpMessageHandler` (no real network calls):
- No network call is attempted when the endpoint is not configured.
- A valid message is posted as the expected JSON shape to the configured endpoint.
- A successful, well-formed upstream response is surfaced as `Success` with the reply text.
- A non-success upstream status, a non-JSON body, and a JSON body with a missing/blank/non-string `reply` field are each correctly classified as failures (four separate malformed-reply shapes are tested).
- A transport-level failure (`HttpRequestException`) is classified as `Unreachable`.
- **Timeout vs. caller cancellation are tested as two distinct scenarios** that must produce two distinct results — this is the one piece of behavior in this sample that is genuinely easy to get backwards, so it gets a dedicated test for each direction rather than one combined test.
- **Logging never contains the message text or the endpoint URL.** This is tested directly: a small recording `ILogger<ChatService>` test double captures every formatted log message across three different outcomes (success, upstream failure, malformed response), and the test asserts that a deliberately "sensitive-looking" test message and the configured endpoint URL never appear in any of them. This is a real content assertion, not a mock-verification stand-in for one.

**Known limitation, stated rather than hidden:** these tests assert on the *content* of formatted log messages (via the recording logger), not on structured log fields or log levels in isolation. That was a deliberate choice to keep the tests meaningful without being brittle against harmless refactors of the log message templates — but it does mean the tests would not catch, for example, a change that logs the right absence of sensitive data at the wrong `LogLevel`.

## Frontend/XSS test coverage

`tests/frontend/xss-test.js` contains 56 assertions, run against the real shipped files:

- **All 12 required malicious payload categories**, delivered as a mocked assistant reply: `<script>`, `<img onerror>`, a `javascript:` Markdown link, mixed-case `JaVaScRiPt:`, event attributes (`onclick`/`onerror`) on an otherwise-allowed tag, a `style` attribute, `<iframe>`, an SVG payload, a MathML payload, a `data:` link, a protocol-relative (`//host`) link, and an entity-encoded `javascript:` payload. Each is checked twice: the rendered HTML must not contain the dangerous construct, and no uncaught window error may have occurred while rendering it.
- **A control check**, run against `marked`'s raw, unsanitized output for the same 12 payloads, confirming most of them really are dangerous without DOMPurify — so the checks above are not vacuously passing against already-harmless input.
- Legitimate Markdown (bold, a list, inline code, a safe `https://` link) survives, and the surviving link gets `target="_blank"` and `rel="noopener noreferrer"`.
- User-entered text that looks like HTML is rendered completely as literal text, with no parsed elements.
- Non-2xx application responses (400/502/503/504-shaped) still render their `reply`, but always as plain text, never as Markdown/HTML — even when that reply text itself contains markup.
- Malformed JSON, a missing/blank/non-string `reply`, and a network error/timeout each fall back to the one generic error message, with no uncaught exception.
- Duplicate-send prevention (a slow in-flight request blocks a second attempt; the button is disabled while busy and re-enabled after), Enter-sends/Shift+Enter-does-not, the IME composition guard, and the `keyCode === 229` guard.
- The input has `maxlength="1000"`, has an associated `<label>`, the send button has visible text, and the log region has `role="log"` and `aria-live="polite"`.
- Focus returns to the input after a completed send, and empty/whitespace-only input is never sent at all.
- If `marked`/`DOMPurify` are unavailable, the assistant reply still renders — as plain text, with no thrown exception and no HTML insertion.

**A real bug this harness caught in itself, not in the application:** while first writing this harness, several early tests spuriously reported an empty chat log with no error at all. The cause was in the *test harness*, not `chat.js`: jsdom finishes constructing a document with `readyState` still `"loading"` and fires `DOMContentLoaded` asynchronously afterwards, on its own schedule — exactly like a real browser. `chat.js` correctly waits for that event when needed, but the harness was dispatching DOM events immediately after evaluating the scripts, occasionally racing ahead of `chat.js`'s own listener and dropping the very first interaction silently. The fix was to make the harness explicitly await `DOMContentLoaded` (or an already-non-`"loading"` `readyState`) before dispatching anything — the same thing a careful browser-based test would have to do. Left in this README as a specific example of what "run the tests, don't just eyeball the code" actually catches.

## Build and test results (last verified 2026-09-22, local .NET 9.0.318 SDK / Node 22.23.2)

| Command | Result |
| --- | --- |
| `dotnet restore Portfolio.LiveChat.sln` | Succeeded |
| `dotnet build Portfolio.LiveChat.sln --configuration Release` | Succeeded — 0 warnings, 0 errors |
| `dotnet test Portfolio.LiveChat.sln --configuration Release` | **28/28 passed** |
| `cd tests/frontend && npm install && npm test` | **56/56 passed** (confirmed stable across repeated runs) |

The app was also smoke-tested by running it directly and sending real HTTP requests: the page (`/`) and every static asset (`vendor/marked.min.js`, `vendor/purify.min.js`, `js/chat.js`, `css/chat.css`) load with `200`; with no endpoint configured, a valid message correctly returned `503`; an empty message returned `400` with the expected localized text; and the framework's own request logs contained no message content or endpoint information, consistent with the automated logging test above. No production endpoint was contacted at any point.

## Relationship to the production integration

This sample was written independently, using the production integration's *documented behavior and lessons* (see [../../docs/SECURITY_HARDENING.md](../../docs/SECURITY_HARDENING.md)) as a specification — not by copying or adapting the original source file-by-file. Some implementation choices differ on purpose: for example, this sample uses a linked, timeout-scoped `CancellationTokenSource` to distinguish a backend timeout from caller cancellation, which is a more directly testable technique than relying solely on `HttpClient.Timeout`. Where this document and [`../../docs/SECURITY_HARDENING.md`](../../docs/SECURITY_HARDENING.md) describe the same control (for example, the 1000-character / 16 KB / 30-second limits), the numbers match on purpose, because they are the actual limits from the real hardening work — reproducing the *engineering decision*, not the code.
