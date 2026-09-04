# Protected asset capture and selectable actions

> Status: Draft
> Applies to: Publishing Page package contracts v3

## Security boundary

Protected document handling begins during source capture, before SharePoint is
asked for file bytes. `ListItemSnapshotReader` explicitly includes the List
field inventory in its CAML view, projects `_IpLabelId`,
`_HasUserDefinedProtection`, and related fields into
`ListDocumentInformationProtectionSnapshot`, and passes that metadata to
`ProtectedAssetCaptureGate`.

The `MicrosoftTenantMetadataOnly` profile behaves as follows:

| Metadata result | Binary request | Snapshot result |
| --- | --- | --- |
| Protected | Never | Metadata plus sealed `MetadataOnly` decision |
| Unknown | Never (fail closed) | Metadata plus sealed `MetadataOnly` decision |
| Explicitly unprotected | Allowed | Ordinary captured binary |

`PageCaptureOptions` deliberately defaults to `MicrosoftTenantMetadataOnly`.
This is a security-first public default: an omitted profile may reduce fidelity,
but it cannot silently export a protected or indeterminate payload. OOCL and any
other source environment whose policy permits full fidelity must opt in with
`ProtectedAssetCapturePolicy.FidelityAllowed(...)` before capture. The selected
profile, policy ID, and fail-closed flag round-trip in package JSON; they are not
inferred from tenant naming, a missing label, or a planning-time choice.

The capture decision and its digest are part of the immutable snapshot;
planning cannot retroactively make a forbidden binary capture safe.

## Ingredient model

A policy-controlled document is represented by four nodes:

```text
ProtectedAsset
├── DocumentIdentity                 (IdentityRequired)
├── BinaryPayload                    (PayloadRequired)
└── InformationProtectionRelationship (HardRequired when a label is observed)
```

An unknown protection result still creates the asset, identity, and payload
decision nodes so the fail-closed exclusion is auditable, but it does not invent
an Information Protection relationship. This keeps archived-content and other
binary-unavailability evidence separate from an actually observed label.

The source snapshot keeps facts only. Target intent lives in
`ProtectedAssetActionPlan` and each `PageIngredientAction` exposes:

- `candidateActions`: the actions allowed by the active policy;
- `selectedAction`: the selected candidate and review identity;
- `selectionReceipt`: ingredient ID, source snapshot digest, candidate-set
  digest, policy/reason, scope, dependency effect, comparison rule, approval
  reference, and receipt digest.

Supported selectable actions are `Reproduce`, `Transform`, `Reference`,
`EvidenceOnly`, `Exclude`, and `Defer`. The first protected-asset policy uses
`Reproduce`, `EvidenceOnly`, `Exclude`, and `Defer`; the remaining values keep
the action contract compatible with other ingredient domains.

## Microsoft-tenant decision

The Microsoft profile defaults to the reviewed safety outcome:

| Ingredient | Selected action | Result |
| --- | --- | --- |
| `ProtectedAsset` | `EvidenceOnly` | Boundary is retained without a target claim |
| `DocumentIdentity` | `EvidenceOnly` | Path, source identity, version, and length remain evidence |
| `BinaryPayload` | `Exclude` | `SatisfiedByPolicy`; target comparison expects absence |
| `InformationProtectionRelationship` | `EvidenceOnly` | Label relationship evidence is retained, not remapped |

Callers can attach one `DefaultIngredientSelectionAudit` to the plan input, or
provide per-ingredient `IngredientActionSelections`. Every explicit selection
must name the source snapshot digest and an allowed candidate ID. Replanning
rejects stale digests; package validation re-derives candidate sets and rejects
tampered receipts even if a caller recomputes the top-level plan digest.

`Exclude` does not mean `Reproduced`, `KnownGap`, or `AuthorizationBlocked`.
It is a non-blocking `SatisfiedByPolicy` terminal state. Only retained literal
HTTP 401/403 evidence, including the operation, request URI, timestamp, and a
matching SHA-256 digest, can classify an ingredient branch as authorization
blocked. A status copied into an action is non-authoritative. An authorization
or deferred branch stops the whole item only when the dependency-aware
executable frontier is empty; required consumers are skipped while independent
branches continue.

## Execution and comparison

The List plan carries one item decision for every captured item. A protected
document whose payload is excluded is omitted from the item materializer;
`ProtectedAssetExecutionPolicy` returns the sealed selection receipt without
invoking a mutation delegate, and the execution journal records an
`AlreadySatisfied` policy-exclusion step.

Fresh comparison treats a source-present, target-absent excluded payload as:

```text
Outcome    = ExpectedDifference
Difference = ExpectedAbsent
```

An absent payload without an approved exclusion remains
`UnexpectedDifference`. Receipts and reports list the ingredient, path, reason,
policy, approval reference, and selection receipt. A page that otherwise passes
verification is `ReproducedWithApprovedExclusions`, never `ExactReproduction`.

## Schema compatibility

This change advances Publishing Page export, migration, and receipt envelopes to
v3, the canonical ingredient graph to v2, List dependency snapshots to v2, and
List plans to v2. Older JSON still deserializes, but validators produce an
explicit re-export or re-plan error. They are not silently reinterpreted because
their snapshot digests were computed before the pre-download decision and
selectable-action fields existed.

## Deliberate limits

- No interactive UI is introduced; JSON planning inputs are the review surface.
- No label substitution, decrypt/re-encrypt, or tenant-to-tenant label mapping is
  authorized.
- Archived content remains a separate ingredient and is not classified as
  Information Protection merely because its bytes are unavailable.
- Attachments do not yet have a document-level Information Protection metadata
  contract; their existing exact-byte behavior is unchanged.
