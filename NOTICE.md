# Notice

Last updated: 2026-09-23

This file is informational. It is **not** a license grant for the repository as a whole; no overall license has been declared yet, and no `LICENSE` file exists at this stage.

## Basis of this portfolio

This project documents engineering work extending **nopCommerce**, an open-source e-commerce platform, for a food-retail business. nopCommerce itself is not included in this repository — it is a separate, independently licensed open-source project (nopCommerce Public License, based on GPLv3). Anyone wishing to review nopCommerce itself should obtain it from its own official source.

## Original source code

Original nopCommerce-derived plugin source code from the production project is **not** included in this repository. Where code samples are added in a later phase, they will be standalone, rewritten examples designed to have no dependency on nopCommerce's own source projects (`Nop.Core`, `Nop.Data`, `Nop.Services`, `Nop.Web`, etc.), so they can be read, understood and run independently of nopCommerce.

## Third-party frontend dependencies

The production security-hardening work described in this repository (see `docs/SECURITY_HARDENING.md`) used the following third-party JavaScript libraries. Both are vendored, byte-for-byte, in the standalone sample at [`samples/livechat/`](samples/livechat/README.md):

- **marked** 15.0.12 — Markdown parser, MIT License.
- **DOMPurify** 3.4.15 — HTML sanitizer, Apache-2.0 License (dual-licensed, Apache-2.0 / MPL-2.0 at the upstream project's option).

Each library's license file is included alongside it under `samples/livechat/src/Portfolio.LiveChat/wwwroot/vendor/`. Exact versions, licenses and SHA-256 checksums are recorded in [`samples/livechat/THIRD_PARTY.md`](samples/livechat/THIRD_PARTY.md).

## Contact

For questions about the scope or licensing of this portfolio repository, please open an issue or contact the repository owner directly.
