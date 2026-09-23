# Third-Party Frontend Dependencies

Last updated: 2026-09-22

Both files below were downloaded independently for this standalone sample directly from their public npm/CDN distribution (jsDelivr, which serves published npm package contents unmodified). They are vendored byte-for-byte — do not reformat or edit them — and are not loaded from any CDN at runtime; the app serves them itself as static files.

| Library | Version | License | File | Source URL | SHA-256 |
| --- | --- | --- | --- | --- | --- |
| marked | 15.0.12 | MIT | `src/Portfolio.LiveChat/wwwroot/vendor/marked.min.js` | `https://cdn.jsdelivr.net/npm/marked@15.0.12/marked.min.js` | `3e7e7d7feb3e5d58cb6c804f68ab5c24cc7e5eb6270fd6e5cbb9124739217d0c` |
| DOMPurify | 3.4.15 | Apache-2.0 (dual-licensed with MPL-2.0 at the upstream project's option; used here under Apache-2.0) | `src/Portfolio.LiveChat/wwwroot/vendor/purify.min.js` | `https://cdn.jsdelivr.net/npm/dompurify@3.4.15/dist/purify.min.js` | `f263b05369e050fa175d4ecb9c9358eb4253602d510297adfb31df48b2f1c4d5` |

License texts are preserved alongside each library:

- `src/Portfolio.LiveChat/wwwroot/vendor/marked.LICENSE.md` (SHA-256 `8e3a3f82f59a60958f56ca08f445647c32a4733dc7ca6c2c46f6eb898471ab9c`)
- `src/Portfolio.LiveChat/wwwroot/vendor/purify.LICENSE` (SHA-256 `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30`)

## Provenance note

These files were acquired independently for this public sample — not copied from the private production repository — even though the pinned versions are the same ones used in the production hardening work described in [../../docs/SECURITY_HARDENING.md](../../docs/SECURITY_HARDENING.md). Because both are the exact same published package version, the resulting bytes and checksums are identical to any other independent download of the same version; that is expected, not a sign of copying, and is exactly what pinning a version is supposed to guarantee.

## Updating either library

1. Decide the target version and expected SHA-256 in advance.
2. Download the new file and its license from an official source (npm registry, jsDelivr, or the project's GitHub releases); do not transform the bytes.
3. Verify the SHA-256 before replacing anything; stop on any mismatch.
4. Update this file's table and the vendored license file.
5. Re-run the frontend/XSS test suite (see `tests/frontend/` and the sample [README](README.md)) before considering the update complete.
