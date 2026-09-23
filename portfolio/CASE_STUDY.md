# Case Study: Hardening an AI Customer-Support Integration

Last updated: 2026-09-22

## Context

The storefront includes a chat widget that lets customers ask questions and get answers from an externally hosted, n8n-orchestrated AI support workflow (details in [../docs/AI_ASSISTANT.md](../docs/AI_ASSISTANT.md)). The widget itself — the part running inside the store — is a small piece of the overall system, but it is the part directly exposed to every visitor's browser, which makes it worth taking seriously from a security standpoint.

## Initial Design

The original integration was straightforward: a customer's message was posted to a backend endpoint, forwarded to a configured webhook, and the reply was parsed as Markdown and inserted directly into the page so that formatted answers (lists, bold text, links) would display correctly.

## Risks Identified

A focused security review of this integration surfaced several issues, the most serious of which was that **AI-generated replies were rendered without sanitization**. Because the reply text came from an external system, and Markdown parsers will happily convert raw HTML or script-like syntax straight through to the page, this created a cross-site-scripting exposure: content in a reply could, in principle, execute in the customer's browser. Alongside that, the integration had no server-side message-length or request-size limits, no timeout on the outbound webhook call, and logging that could capture sensitive request content and the webhook address itself.

## Backend Hardening

I scoped the backend fix first, deliberately keeping the external contract (the webhook request/response shape) unchanged so the fix could be verified and deployed independently of anything on the AI-workflow side. This pass added: server-side validation (message length and request-body limits), a bounded timeout on the outbound call, explicit mapping of every failure mode (timeout, unreachable webhook, malformed response, missing configuration) to a distinct, localized error response, and a rewrite of the logging so that outcome and timing are recorded but message content, customer identifiers and the webhook URL never are.

## Frontend / XSS Hardening

The second pass rebuilt the rendering path around explicit sanitization: replies are parsed as Markdown and then passed through an HTML sanitizer configured with a strict, explicit allowlist, rather than trusting the Markdown output directly. At the same time, I vendored the two frontend libraries this depends on (pinned versions, verified checksums, no runtime dependency on a third-party CDN) and closed several smaller robustness gaps: duplicate-send protection, a client-side timeout independent of the backend's, safe handling of malformed or network-failure responses, and correct behavior for IME text input and for browsers with local storage disabled.

## Verification

Before deployment, the backend changes were exercised against a local mock of the webhook covering every error path (missing configuration, timeout, non-success response, malformed JSON, missing reply field), and the frontend change was verified with an automated test harness that ran the actual rendering code against a batch of XSS and malformed-Markdown payloads, confirming the sanitizer removed dangerous content while leaving normal formatting intact.

## Production Deployment

I carried out the production deployment myself, following the checklist described in [../docs/DEPLOYMENT_OVERVIEW.md](../docs/DEPLOYMENT_OVERVIEW.md): backing up the currently deployed version and verifying that backup byte-for-byte, staging and hash-verifying the new files, replacing only the intended component, restarting, and then functionally verifying the live chat with real messages before considering the deployment complete. A rollback plan (restore the verified backup, restart) was kept ready throughout and, in this case, was not needed.

## What I Learned

The most useful lesson was procedural rather than technical: splitting the fix into an independently deployable backend pass and frontend pass made both easier to reason about, test and roll back — and it forced me to be explicit about what the frontend fix would and would not change about the external contract, which is exactly the kind of boundary that matters when part of a system is outside your control. I also used Claude and Codex as part of this process — Claude for analysis, risk review and cross-checking the implementation against the stated requirements, and Codex for implementation drafts — while I remained responsible for the requirements, the acceptance decisions, and the production deployment itself. I was responsible for requirements, review, validation and deployment; I do not claim that every line was written without AI assistance, and I would rather be specific about that than vague.

## Remaining Improvements

This hardening closed the issues found in this specific review; it did not close every open question about this integration. Still outstanding: server-side rate limiting on the chat endpoint, authentication between the storefront and the external AI workflow, a hardened (non-spoofable) customer/session identifier, and a Content Security Policy review for the storefront as a whole. These are listed explicitly in [../docs/SECURITY_HARDENING.md](../docs/SECURITY_HARDENING.md) rather than left implicit.

A reader does not have to take this write-up's word for any of it: [`samples/livechat/`](../samples/livechat/) is a standalone, buildable version of both hardening passes described above — the same validation, timeout, error-mapping and safe-logging controls on the backend, and the same `marked` → `DOMPurify` sanitized rendering path, duplicate-send protection and accessibility work on the frontend — with its own automated tests (28 backend, 56 frontend/XSS) that can be run directly.
