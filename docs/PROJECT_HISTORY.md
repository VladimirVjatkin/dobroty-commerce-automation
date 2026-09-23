# Project History

Last updated: 2026-09-23

This is a milestone-level history of how the project evolved, not a chronological dump of private commit hashes. Where a specific commit materially matters for understanding a decision, it is referenced in [SECURITY_HARDENING.md](SECURITY_HARDENING.md); everywhere else, this document describes what changed and why.

## 1. An existing food-retail business needed a modern storefront

The business already existed and was selling through an older setup. The starting point for this project was a decision to move to a current, supported version of nopCommerce rather than continuing to extend an aging one, and to use that migration as the opportunity to add the business automation the store actually needed.

## 2. Platform migration

The store was migrated from an older nopCommerce generation to nopCommerce 4.90.4 on .NET 9, rebuilding the local development environment, re-validating the solution and plugin set against the new version, and only then porting store-specific customizations forward — rather than trying to upgrade a live, customized installation in place.

## 3. Business automation plugins

With the platform migration in place, seven custom plugins were built to cover the business rules a stock commerce platform does not provide: Czech company lookup and invoicing for B2B customers, automatic customer loyalty tiers, postal-code delivery eligibility, a first-order discount rule, and Czech QR payment instructions. Each was scoped as an independent plugin using nopCommerce's own extension points, rather than as modifications to the core platform.

## 4. AI assistant integration

I designed and built an n8n-orchestrated AI support workflow — OpenAI for responses, Ollama for embeddings, Qdrant for retrieval, PostgreSQL for conversation memory and knowledge sources — and connected it to a storefront chat widget and a Telegram channel, giving customers a way to ask product and order questions without a dedicated support team. Implementation and debugging were AI-assisted (Claude, ChatGPT, Codex); the architecture, configuration decisions, integration and validation are mine.

## 5. Repository and documentation cleanup

As the project grew, platform documentation, operational runbooks and architecture decision records were organized into a consistent structure, separating what was verified from what was reported or still planned — a discipline that carried directly into how this portfolio repository itself is written.

## 6. AI chat security hardening

A dedicated security review of the AI chat integration found a cross-site-scripting exposure in how AI-generated replies were rendered, along with missing server-side limits and unsafe logging. This was fixed in two focused passes — backend hardening, then frontend/XSS hardening — each reviewed, tested and deployed independently. The full story is in [SECURITY_HARDENING.md](SECURITY_HARDENING.md) and the [case study](../portfolio/CASE_STUDY.md).

## 7. Public portfolio extraction

This repository is a curated extraction from the private production project: an audit classified what could safely be shared, and original plugin source code, real business identifiers and production infrastructure details were excluded. Documentation, architecture write-ups and standalone runnable samples ([`samples/livechat/`](../samples/livechat/README.md), [`samples/n8n/`](../samples/n8n/README.md)) are included so the engineering can be reviewed and run directly.
