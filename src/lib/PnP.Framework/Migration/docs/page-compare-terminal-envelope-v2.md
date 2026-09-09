# Page compare terminal envelope v2

`pnp-page-compare-terminal-envelope/v2` and
`pnp-page-compare-consumer-projection/v2` are explicit successors to the
shipped v1 contracts. They do not change `pnp-page-compare-report/v1`,
`pnp-page-compare-terminal-envelope/v1`, or
`pnp-page-compare-consumer-projection/v1`.

## Why v2 exists

Terminal v1 can represent a fully verified native Wiki execution, a pre-write
access denial, or unsupported/no execution. It intentionally cannot rename a
mutation-started failed or partial native receipt as any of those states. V2
adds `native-execution-adverse` so that the original typed native receipt,
admitted plan, operation set, source/version binding, native step ledger,
incomplete obligations, and cleanup receipt can complete a lossless consumer
round trip without entering the successful Compare gate.

The success gate remains `ValidateSuccessfulExecution`. V2 adverse validation
uses `ValidateExecutionEvidence` only to prove receipt lineage and native step
integrity. It then requires `FailedUnexpectedly` or `PartiallySucceeded`,
`MutationStarted=true`, `PartialExecution=true`, non-passing storage/runtime,
no invented runtime receipt, no success-shaped material equality, and at least
one unsatisfied required obligation.

Cleanup evidence stores the exact receipt JSON text plus its UTF-8 SHA-256,
schema, operation ID, and relative artifact locator. The operation ID must be
the admitted cleanup operation, and the embedded JSON must not contain
duplicate property names at any depth. This is a lossless evidence carrier,
not a new cleanup receipt or outcome engine.

## Acceptance and projection integrity

V2 uses `PublishingPageCompareReconciler.DeriveAcceptance` as the shared
acceptance authority for executed material rows. Therefore material
`runtime-pending`, `unknown`, and `source-version-changed` results are
`unverified`, and storage/runtime failure cannot pass. V2 also validates that
`exact` means equal non-empty raw digests and `canonical-equivalent` means equal
non-empty canonical digests through the same final classification seam used by
the Publishing reconciler. The terminal ingredient shape does not carry an
approved transformed digest or a typed non-applicability decision. It therefore
rejects `transformed-as-planned` and `not-applicable` declarations instead of
allowing an unsupported result label to produce `pass`.

The shared terminal ingredient ledger rejects unknown availability, execution,
disposition, and result values. An adverse native case may retain an
ingredient-level access denial, but that ingredient must satisfy the same
bounded-attempt and no-target-evidence invariant as every other denied terminal
case.

The consumer projection is digest sealed. Reverse adaptation rebuilds the
terminal envelope, validates its source digest, re-derives every public case
field and material row, and compares the complete canonical projection. A
caller cannot change `caseId`, `terminalKind`, `pageFamily`,
`acceptanceVerdict`, material rows, ordering, or retained terminal evidence and
then regain acceptance merely by recomputing the projection digest.

## Schemas

- `docs/schemas/pnp-page-compare-terminal-envelope-v2.schema.json`
- `docs/schemas/pnp-page-compare-consumer-projection-v2.schema.json`

Nested admitted plan, typed receipt, runtime, canonical report, ingredient, and
digest semantics remain enforced by the existing product validators. Unknown
JSON fields and unsupported versions fail closed during strict native
deserialization.
