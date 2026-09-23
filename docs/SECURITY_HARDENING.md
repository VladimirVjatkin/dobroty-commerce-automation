# Security Hardening: The LiveChat AI Integration

Last updated: 2026-09-23

This document is the technical write-up of the principal security case study in this portfolio: hardening the storefront's AI chat integration. A narrative version aimed at a general reader is in [../portfolio/CASE_STUDY.md](../portfolio/CASE_STUDY.md); this document lists the specific controls that were added, and what is deliberately still open.

**Scope note:** everything below covers **this one integration** — the chat widget and its backend transport to the AI workflow. No other part of the store has received the same review depth.

## Engineering progression

1. **Initial integration.** A working chat widget forwarded customer messages to a webhook and rendered the reply, including Markdown formatting, directly into the page.
2. **Audit findings.** A security-focused review of that integration found: an XSS exposure (AI-generated replies were parsed as Markdown and inserted into the DOM without sanitization, meaning HTML or script content in a reply could execute in the customer's browser), no server-side message length or request-size limits, no request timeout on the outbound webhook call, and logging that could capture sensitive request/response content and the webhook's own URL.
3. **Backend hardening.** Server-side validation, bounded timeouts, structured error handling and safe logging were added — see below.
4. **Frontend / XSS hardening.** The rendering path was rebuilt around explicit output sanitization, and several client-side robustness gaps were closed at the same time.
5. **Runtime tests.** Both passes were exercised against a local mock of the webhook and, for the frontend fix, an automated harness driving the actual rendering code against a batch of XSS and malformed-input payloads.
6. **Controlled production deployment.** Each change was deployed manually, one verified step at a time, with a backup of the previous version kept until the new version was confirmed healthy.
7. **Documentation.** The resulting contract, controls and verification evidence were written up for future maintenance — the basis for this document.

## Backend hardening — verified controls

- **Message length limit:** messages longer than 1000 characters are rejected before any outbound call is made.
- **Request body limit:** the endpoint enforces a 16 KB request-size cap.
- **Server-side webhook configuration:** the outbound webhook address is read from server configuration, not sent by the client; if it is missing or invalid, the endpoint fails safely without attempting a network call.
- **Bounded timeout:** the outbound call to the webhook is bounded to 30 seconds.
- **Status and error mapping:** transport failures, upstream errors, timeouts and malformed upstream responses are each mapped to a distinct, localized error response rather than surfacing a generic failure or a stack trace.
- **Safe technical logging:** logs record outcome, HTTP status, duration and response size — never the webhook URL, the message content, or customer-identifying fields.
- **Cancellation handling:** if the client disconnects before a response arrives, the in-flight request is cancelled cleanly rather than left running.

## Frontend hardening — verified controls

- **User-entered text and error messages are rendered as plain text**, never parsed as Markdown or HTML.
- **Assistant replies are parsed as Markdown** (using `marked`, version 15.0.12) only when the response was successful and usable.
- **The resulting HTML is sanitized** (using `DOMPurify`, version 3.4.15) before it is inserted into the page, using an explicit allowlist of safe tags and attributes rather than the library's broader defaults — this is what closes the original XSS exposure, including stripping executable URL schemes such as `javascript:`.
- **Both libraries are vendored locally** with pinned versions and verified checksums, rather than loaded from a third-party CDN at runtime, removing a supply-chain dependency on an external host being available and unmodified.
- **Duplicate-send protection:** the send control is disabled while a request is in flight, preventing accidental duplicate submissions.
- **Client-side timeout:** the frontend gives up on a stalled request after 45 seconds, independent of the backend's own timeout.
- **Malformed-response and network-failure handling:** a non-JSON response, a missing/unusable reply field, or a network error all fall back to a single, safe, localized error message rather than throwing an unhandled error.
- **IME-safe Enter handling:** the Enter key does not submit a message while an IME composition (used for languages such as Chinese, Japanese or Korean input methods) is in progress.
- **Storage fallback:** if the browser's local storage is unavailable or throws, the widget falls back to an in-memory identifier for the current page load rather than failing to load.
- **Accessibility improvements:** proper semantic roles and labels were added to the chat panel, its close control and its log region, along with focus management on open/close.

## Boundaries of this work

- Server-side rate limiting for the chat endpoint is planned, not implemented.
- There is no authentication between the storefront and the AI workflow beyond the webhook address itself not being public.
- The customer/session identifier sent to the AI workflow has not been hardened against spoofing; that is separate, unscoped work.
- A Content Security Policy review for the storefront as a whole has not been completed.
- Beyond the targeted tests described above, there is no versioned regression suite yet for this integration.

## Remaining work

- Server-side rate limiting for the chat endpoint.
- Server-to-server authentication for the webhook call (for example, a shared secret validated by the external workflow).
- A hardened, non-spoofable customer/session identifier for the chat integration.
- A Content Security Policy review covering the storefront.
- Broader custom regression testing beyond this one integration.

## Why this is presented as a case study rather than a checklist

The reason this is written up in detail — rather than just stated as "chat is now secure" — is that the useful engineering signal here is the *process*: how the issue was found, scoped, fixed in two independent passes (backend, then frontend), verified with tests before deployment, and deployed with a rollback plan. That process is described narratively in [the case study](../portfolio/CASE_STUDY.md).

## Standalone, runnable demonstration

This repository includes a standalone, buildable and runnable executable demonstration of the backend and frontend patterns described above — not just this document's description of them — at [`samples/livechat/`](../samples/livechat/). It is a rewritten sample with no dependency on nopCommerce, reproducing the same category of controls (validation, timeouts, safe error mapping, safe logging on the backend; the `marked` → `DOMPurify` → sanitized-HTML rendering path, duplicate-send prevention, IME-safe input and accessible markup on the frontend), verified by 28 backend tests and a 56-assertion frontend/XSS test harness covering all twelve payload categories referenced above. See [`samples/livechat/README.md`](../samples/livechat/README.md) for how to build, run and test it.
