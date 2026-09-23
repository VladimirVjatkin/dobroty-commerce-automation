# Business Automation

Last updated: 2026-09-22

This document describes the custom business-automation logic built on top of nopCommerce, at the architecture and business-flow level. No original source code, real account numbers, business registration identifiers or production configuration are included — for the two most sensitive pieces (Czech invoicing and QR payments) this is deliberate: they are described here as architecture and business flow only, without the underlying implementation.

## ARES company lookup

Czech businesses are identified by an 8-digit company registration number (IČO). During B2B checkout, a plugin calls the Czech public business registry (ARES) with the number the customer enters, and — if found — autofills the company name, VAT number and registered address. The lookup is a simple, stateless, read-only call to a fixed government endpoint with a bounded timeout; it does not cache or store third-party responses beyond the current request. This removes a manual, error-prone data-entry step for B2B customers.

## Customer loyalty tiers

Customers are automatically classified into loyalty tiers based on their lifetime completed order value, with a scheduled background task re-evaluating and updating tier assignments on a recurring basis rather than on every request. Tiers can also downgrade after a period of inactivity. Wholesale customers are explicitly excluded from this automatic classification, since their pricing is handled separately. An administrator can still manually override a customer's tier for edge cases.

This is deterministic, threshold-based business logic — not a machine-learning classifier — and it is described here as such.

## Delivery eligibility by postal code

Checkout enforces a whitelist of postal codes that the store currently delivers to, together with configurable delivery time slots and a free-shipping order-value threshold. This keeps delivery promises limited to areas the business can actually service, without requiring a full logistics/routing system.

## First-order discount rule

A custom discount requirement rule targets customers who have no prior non-cancelled orders, enabling a "first order" incentive to be configured through the standard nopCommerce discount system rather than through bespoke checkout code.

## Order-event driven document automation

nopCommerce raises in-process domain events when an order is placed or marked as paid. Custom event consumers react to these events:

- **On order placement**, eligible orders trigger generation of a Czech tax/proforma document and queue an email with the document attached for asynchronous delivery.
- **On order payment confirmation**, any gift cards purchased with that order are activated, and a notification email is queued to the recipient.

Both paths are described in more detail below, and both currently rely on the request-scoped, in-process event pipeline rather than a durable message broker — a deliberate simplicity trade-off appropriate to the store's current scale, with known edge cases noted below.

### Czech invoicing (architecture only)

The invoicing plugin generates sequentially numbered Czech proforma and tax documents as PDFs, using order data captured at the time of the order (so a document remains reproducible even if the customer's profile changes later) rather than always re-querying live customer data. It integrates with the ARES lookup above to correctly identify B2B buyers, and distinguishes personal from company purchases explicitly at checkout rather than inferring it.

Known, not-yet-closed edge cases in this area: two concurrent requests could in principle both attempt to create a document for the same order before a uniqueness constraint catches it, and a small number of fields in the regenerated PDF are recalculated from current data rather than fully sourced from an immutable snapshot. Both are flagged here rather than glossed over, and are on the list of things to fix with dedicated regression tests before this piece should be considered fully hardened.

Original source code, real invoice numbering configuration, and any real business registration or bank account details are **not** included in this repository.

### QR payment instructions (architecture only)

For bank-transfer orders, the store generates a scannable QR code encoding a standard Czech payment string (SPD format) with the order's payment details, so a customer can pay by scanning it in their banking app rather than typing in account details manually. This is a payment *instruction*, not a payment gateway — there is no automated confirmation that the transfer was received; that is currently a manual administrative step.

Original source code and any real account information are **not** included in this repository.

## Reliability notes

This is presented as engineering honesty, not a weakness list to be hidden: the order-event consumers described above run synchronously within the request that raised the event, using the database as the durable component (the email queue). This means a failure between two related writes (for example, gift-card activation succeeding but its notification email failing to queue) can leave a partially completed operation that requires an explicit retry or reconciliation strategy — which is on the roadmap rather than implemented today. Being upfront about this is more useful in an interview than claiming full idempotency that hasn't been tested.
