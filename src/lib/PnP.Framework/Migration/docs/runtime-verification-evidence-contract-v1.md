# Runtime verification evidence contract v1

`pnp-migration-runtime-verification-receipt/v1` is admissible only when the
consumer can recompute its runtime evidence from exact artifact bytes. A
receipt digest or an artifact-store membership result is not sufficient.

The receipt binds:

- the admitted plan, source-version identity, operation envelope, target
  identity, and native import receipt;
- the canonical `RuntimeVerificationManifest` SHA-256;
- a full Git implementation SHA, repeated by each result;
- a fresh isolated browser context identity, isolation-mode flag, and
  creation/navigation timeline;
- per-result HTTP request/response observations and a `cache: no-store`
  request policy (`CacheDisabled`, `Cache-Control`, `Pragma`, and cache-source
  flags);
- the actual decoded document response bytes by locator, byte length, and
  SHA-256;
- a DOM-probe artifact, and an all-or-none screenshot artifact binding.

`RuntimeVerificationReceiptValidator.ValidateEvidence` opens each bound
artifact through `IMigrationArtifactStore`, recomputes its length and SHA-256,
and rejects missing, stale, foreign, or corrupt bytes. It also rejects a
foreign requirements manifest, implementation ref, browser context, target,
cache policy, HTTP observation, partial screenshot binding, and stale capture
timeline. Required-result coverage and receipt/status consistency remain
enforced by `PublishingPageCompareReconciler`.

HTTP `200` is necessary for a positive HTML result, but it is never sufficient:
the producer must set `Passed` from the bound DOM/error-shell probes. A stable
SharePoint error shell therefore remains `Failed` even when transport returns
HTTP `200`.
