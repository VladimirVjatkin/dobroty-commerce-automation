# n8n Workflow Samples

Last updated: 2026-09-22

These are **sanitized portfolio copies** of two real n8n workflows that back the storefront's AI support integration (see [../../docs/AI_ASSISTANT.md](../../docs/AI_ASSISTANT.md)). They are provided so a reviewer can inspect the actual node-level architecture — not just a description of it — without any operational or personal data attached.

## What "sanitized" means here, precisely

- **Credentials are intentionally removed/unconfigured.** Neither file contains a `credentials` binding on any node; every Postgres, Qdrant, OpenAI, Ollama, Telegram and Gmail node will show as "not connected" if opened in n8n, by design.
- **Node IDs were regenerated.** The source exports contained malformed, comma-prefixed node identifiers (an artifact of how they were originally exported). Every one of them was replaced with a freshly generated, valid, unique identifier — see [Node ID remapping](#node-id-remapping-summary) below. Well-formed IDs that were already valid were left untouched.
- **Production identifiers were removed or replaced with placeholders:** the storefront webhook path is now the placeholder `portfolio-chat-demo`; a support-contact email address was already a non-routable placeholder (`support@example.invalid`) in the source and remains so; Google Sheets document/tab references were already placeholder values (`REPLACE_WITH_GOOGLE_SHEET_ID` / `REPLACE_WITH_SHEET_NAME`) and remain so.
- **The owner's personal name was removed from the AI system prompt.** The prompt originally illustrated a personalized-greeting rule using the owner's own first name as a worked example (e.g. *"Dobrý den, \<name\>!"*). Both example greetings were replaced with a bracketed placeholder — `[First Name]` / `[Имя]` — so the *rule* (greet the customer by the first word of their name, in their own language, only when a name is present) is still fully demonstrated, without using anyone's real name as the example.
- **Workflow names were changed** from their internal working titles to neutral portfolio names (see below).
- **Nothing else was changed.** No nodes were added or removed, no connections were altered, and no model/collection/limit configuration was edited. This was checked programmatically, not just asserted — see [Source vs. public node counts](#source-vs-public-node-counts) in the main response for this phase.

## What this is not

- **This is not proof that this exact JSON is the currently running production version.** It is a snapshot, reviewed statically. Whether production has since diverged has not been checked here.
- **This has not been imported into a running n8n instance as part of producing this sample.** The files were validated as well-formed JSON with internally consistent node references (see below), which is strong structural evidence of importability, but it is not the same as a confirmed successful import. If you import either file, do so into a disposable/test n8n instance and do not attach real credentials before inspecting it.
- **No customer data is included.** No real conversation content, no real names, no real order or contact information appears in either file — everything customer-shaped in these workflows (message text, user identifiers, memory keys) is a runtime *expression* (e.g. `{{ $json.body.message }}`) describing how the data flows, not a captured value.

## `assistant-workflow.json`

This is the workflow behind the storefront chat and the Telegram bot. It demonstrates:

- **Channels:** a storefront webhook trigger (`Webhook` → the placeholder path above) and a Telegram trigger, both normalized into a common shape (`user_id`, `user_name`, `message_text`, `source`, `message_type`) before reaching the AI Agent.
- **Voice path:** Telegram voice messages are downloaded and transcribed through OpenAI's audio/transcribe endpoint before being routed the same way as text messages.
- **AI Agent:** a LangChain-style agent node running the sanitized system prompt described above, with a maximum of 3 tool-use iterations.
- **Model:** OpenAI `gpt-4o-mini`, temperature `0.2`.
- **Retrieval (RAG):** five Qdrant collections wired in as agent tools — `company_info`, `product_info`, `recipes`, `dumpling_articles`, and `chat_logs` (used specifically to recall prior interactions with the same user) — each with its own `topK` (10 for most, 20 for `product_info`) and each backed by an Ollama embeddings node (`mistral:latest`, except the `dumpling_articles` path, which uses `nomic-embed-text:latest`).
- **Memory:** Postgres-backed chat memory, keyed by a custom session key (the same `user_id` used elsewhere in the workflow), with a context window of 10 messages.
- **Escalation to a human:** if the agent's reply signals that a manager is needed, the workflow branches to collect contact details (via a Telegram form) and notify a mailbox, rather than trying to resolve the request itself.

## `knowledge-loader.json`

This is the companion workflow that populates the Qdrant collections the assistant retrieves from. It demonstrates:

- **PostgreSQL knowledge sources:** four `SELECT` queries against source tables for company info, product info, recipes, and dumpling-related articles.
- **Document preparation:** each source is reshaped into a single `pageContent` field (with per-collection metadata such as category, product name, or tags attached) before chunking.
- **Chunking:** a recursive character text splitter with `chunkSize: 3000`, applied per source.
- **Embeddings:** Ollama, `mistral:latest` for company/product/recipes, `nomic-embed-text:latest` for the dumpling-articles path — matching the embedding models used on the retrieval side in `assistant-workflow.json`.
- **Qdrant insertion:** one `insert`-mode Qdrant vector store node per collection, each paired with its own embeddings and document-loader/splitter nodes.

This workflow is triggered manually (`When clicking 'Test workflow'`) in the source export, consistent with knowledge loading being a manual, on-demand process today rather than an automated catalog sync — see the "not yet independently validated" / "planned improvements" sections of [../../docs/AI_ASSISTANT.md](../../docs/AI_ASSISTANT.md).

## Node ID remapping summary

| Workflow | Total nodes | Malformed IDs found | Malformed IDs replaced | Remaining malformed IDs after remapping |
| --- | --- | --- | --- | --- |
| `assistant-workflow.json` | 44 | 34 (31 node-level, 3 nested rule/condition-level) | 34 | 0 |
| `knowledge-loader.json` | 25 | 7 (all node-level) | 7 | 0 |

The replacement was done with a small script, not by hand, specifically so the mapping could be applied consistently and verified programmatically rather than trusted by eye. Because n8n's `connections` object (and every in-workflow expression such as `$('Node Name')`) refers to nodes **by name, not by ID**, remapping IDs required no changes anywhere else in either file — there was nothing else to update. This was confirmed, not assumed: after remapping, every connection target in both files was checked against the actual node list and all resolved correctly (see the validation results reported alongside this phase's response).

## How to inspect / import

To read the architecture without n8n at all, both files are plain, indented JSON — open them in any editor or JSON viewer and look at the `nodes` array (what each step does) and the `connections` object (how steps are wired together, by node name).

To import into n8n for a closer look:

1. Use a disposable or local test n8n instance — not a production one.
2. In the n8n editor, use **Import from File** and select `assistant-workflow.json` or `knowledge-loader.json`.
3. Every credential-requiring node (Postgres, Qdrant, OpenAI, Ollama, Telegram, Gmail) will show as unconfigured. **Do not attach real credentials** unless you specifically intend to run it against your own infrastructure.
4. Both workflows import with `active: false`; leave them inactive for inspection.

## Source

These files are derived from `RESEARCH/n8n/LiveChatALL - TEST2.json` and `RESEARCH/n8n/LoadToQdrant.json` in the private project repository, which are themselves already-sanitized exports reviewed for the project's internal technical audit. This phase applied an additional, portfolio-specific sanitization pass on top of that (documented above) before these copies were created.
