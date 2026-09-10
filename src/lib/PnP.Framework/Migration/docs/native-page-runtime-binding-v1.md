# Native page runtime acceptance contracts v1

The Classic Wiki profile is `classic-wiki.synthetic.runtime-acceptance/v1`.
It is page-first and binds the reviewed `node:runtime / Runtime / runtime.page`
claim. It does not establish Enterprise Wiki, canonical Reconcile, M5, release,
or customer acceptance.

## Pre-capture binding

`pnp-native-page-runtime-binding/v1` is sealed before browser capture. Its
`contentSha256` is computed with
`SerializeCanonicalWithNullRootProperty(value, nameof(ContentSha256))`.
The binding contains the exact package/source/snapshot, admitted plan, all four
operation IDs, native import receipt, fresh target Site/Web/List/File/item/
version/ETag identity, fixed profile manifest, bounded capture window, and
separate import/contract producer refs. Package, native receipt, target identity,
and policy bytes are stored in CAS and reopened by the consumer.

The fixed required set is:

- `runtime.wiki` / `PageReachability` / `Exact admitted Wiki surface`
- `runtime.wiki.error-shell-absence` / `ErrorShellAbsence` /
  `No login, access-denied or error shell`
- `runtime.wiki.screenshot` / `ScreenshotCapture` /
  `Bound screenshot captured`

All are required and remain in ordinal ID order. A caller cannot clear or make
them optional to obtain `NotRequired`.

## External observation envelope

`pnp-external-page-runtime-evidence/v1` is binding-bound and contains exactly
one of:

- an existing `RuntimeVerificationReceipt`, with complete artifact/context/
  operation lineage; or
- a terminal negative observation such as 401/403, semantic HTTP-200 denial,
  redirect, transport failure, or incomplete capture.

The envelope records a distinct capture producer, pre/post target identity
readbacks, bounded ordered attempts, request-ID availability, detector identity,
and a sealed artifact manifest. It cannot set native acceptance.

## Native evaluation authority

`ClassicWikiNativeRuntimeAcceptance.Evaluate` consumes the immutable package,
admission, native import, pre-capture binding, external envelope, artifact store,
the unique Classic Wiki semantic policy, and a host-owned provenance verifier.
The semantic policy returns only a per-result boolean. The common consumer owns
binding/storage/runtime/provenance gates and calls the existing
`ClassicWikiImportStatusPolicy.Acceptance`.

Positive runtime evidence remains `Pending` unless provenance is independently
`VERIFIED`. The production default verifier returns `UNVERIFIED`. A JSON receipt
or a caller-provided `VERIFIED` string is not an acceptance input. Complete
negative evidence remains `Failed / Rejected`, including 401/403 and semantic
HTTP-200 access-denied shells.

The append-only `pnp-native-page-runtime-acceptance-receipt/v1` records evaluator
identity/ref, all input digests, validation/storage/runtime/acceptance/provenance
statuses, explicit exclusions, reason codes, and evidence refs. The original
native import receipt and its `Pending` fields are not modified.

## Fail-closed behavior

Unknown schema/profile/policy, invalid seals, unnamed extensions, missing CAS,
altered or un-reopened bytes, unsafe locators, stale source or operation IDs,
same-URL/different target identity, capture outside the import-bound window,
pre/post drift, missing screenshot, weakened manifests, and unsupported result
shapes cannot produce positive acceptance. Unknown extensions must be
namespaced and losslessly retained.

## Provenance seam

The v1 provenance manifest binds clean source/tree, toolchain, locked dependency
graph, command, logs, outputs, runtime closure, historical binary request, and
schema compatibility. The receipt records independent verifier identity/ref and
separate source, artifact, rebuild, and historical-binary gates. All applicable
gates must be `VERIFIED` before the overall result can be `VERIFIED`.
