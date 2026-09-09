# Page compare terminal envelope v1

`pnp-page-compare-terminal-envelope/v1` is the versioned aggregate used when a
batch contains both canonical per-page compare reports and terminal outcomes
that cannot honestly satisfy `pnp-page-compare-report/v1`.

The envelope does not rename an adverse or cross-family record as canonical
v1. `canonicalReports` is a lossless pass-through collection: every entry must
still satisfy the frozen Publishing v3 / import receipt v5 / runtime receipt v1
bindings enforced by `PublishingPageCompareReportValidator`. Classic Wiki,
access-denied, and unsupported records remain in `cases` with their distinct
terminal semantics.

## Contract and adapter

- Schema constant: `PageCompareTerminalContract.SchemaVersion`.
- JSON Schema: `docs/schemas/pnp-page-compare-terminal-envelope-v1.schema.json`.
- Native serialization: `PageCompareTerminalEnvelopeSerializer` delegates to
  `MigrationContractSerializer`, then validates the self-digest and every typed
  nested receipt.
- Consumer adapter: `PageCompareTerminalConsumerAdapter.Consume` preserves
  canonical v1 reports byte-semantically, projects only admitted Wiki material
  rows into the existing `IngredientCompareResult` shape, and keeps the complete
  terminal case beside the projection.
- Reverse adapter: `Reconstruct` rebuilds the envelope and requires the original
  envelope digest. A changed or omitted case, ingredient, obligation, order, or
  binding fails closed instead of being silently dropped.

Unknown fields are captured during native deserialization and rejected by the
strict validators. Unknown schema versions are rejected. This makes forward
incompatibility explicit and prevents the default JSON reader from silently
discarding information.

## Three terminal states

| Terminal kind | Required evidence | Consumer result |
| --- | --- | --- |
| `wiki-native-executed` | exact Classic Wiki package schema plus canonical package path/digest/bytes, admitted plan and four operations, typed Wiki receipt, successful mutation, fresh storage readback, runtime receipt and artifacts, strict reconcile status, per-ingredient distinct source/actual evidence | eligible executed material ingredients are projected as `IngredientCompareResult` rows; the typed receipt alone never creates an `exact` row or M5 claim |
| `pre-write-denied` | `unavailable.access_denied`, `ACCESS_DENIED_SKIPPED`, HTTP 401/403 or semantic denial, bounded attempt count, explicit dependent obligations | no target observation, operation IDs, native receipt, runtime receipt, equality row, or success verdict is emitted; required/fidelity-significant denial remains `conditional` |
| `unsupported-no-execution` | `unsupported`, `CAPABILITY_UNSUPPORTED`, `not-executed`, explicit unverified obligations | no mutation/readback/runtime/equality shape is emitted; consumer verdict remains `unverified` |

The exact Classic Wiki package value is
`pnp-classic-wiki-page-migration-package/v1`. The legacy approximation
`pnp-classic-wiki-migration-package/v1` is never accepted as a current product
binding.

## Canonical v1 compatibility boundary

`pnp-page-compare-report/v1` remains unchanged. Its strict semantic reader
rejects wrong schema, unknown runtime/acceptance status, missing evidence,
duplicate ingredient IDs, orphan causes, false-complete material/layout rows,
foreign implementation refs, source-only evidence presented as actual target
evidence, empty operation IDs, and a foreign package/receipt family.

The terminal adapter does not manufacture a Publishing receipt v5 or target
operation ID for a Wiki, denied, or unsupported case. A future cross-family
canonical report version must use a new schema ID instead of widening v1 in
place.

## Compatibility matrix

| Producer payload | Accepted by canonical v1 reader | Accepted by terminal v1 | Canonical material rows |
| --- | --- | --- | --- |
| Valid `pnp-page-compare-report/v1` | yes | yes, under `canonicalReports` | unchanged |
| Successful Wiki native chain with distinct actual evidence | no, because v1 is Publishing-specific | yes | yes, row shape only |
| Wiki typed receipt without fresh runtime/reconcile | no | no | none |
| Root or ingredient access denied before write | no | yes | none for the denied instance |
| Publishing BaseTemplate 101 unsupported/no execution | no | yes | none |
| Legacy approximate Wiki package label | no | no | none |
