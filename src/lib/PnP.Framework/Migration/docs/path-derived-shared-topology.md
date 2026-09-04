# Path-derived shared topology

> Status: Draft
> Implementation status: Partial source fidelity, generic action signatures, global scheduling, durable checkpoints, and page proof validation implemented
> Scope: Target Web preparation when source ancestor reads return literal HTTP `401` or `403`

## Partial source evidence

An ancestor-read denial does not erase facts captured before the failure. `PathDerivedSourceTopologyEvidence` v3 retains the real source Site ID, every root-to-leaf Web captured by successful earlier passes, the primary leaf identity, and the exact decoded path. Only the still-unknown paths become authorization-limited fidelity ingredients. The literal response is validated against externally derived operation, primary-leaf request URI, source authority, and source-closure action; a self-consistent wrong-host reseal, message text, or CSOM error payload is insufficient.

The model does not invent intermediate Web GUIDs. Source-owner bindings use an authority/Site/path key at every level, carry real GUIDs for the known root and leaf, and leave unknown ancestor GUIDs empty. This preserves root-scoped content types and `Style Library` resources while keeping unknown intermediate identity explicit.

## Target actions and identities

The target Site root is a verified external logical action. Each target child Web is another action. Five identities serve different purposes:

| Identity | Purpose |
| --- | --- |
| Target slot key | Canonical target authority + expected Site GUID fence + Site path + Web path. The same path in another tenant or Site is a different slot. |
| Logical action key | Stable producer identity derived from the target slot, normalized semantic state, ownership, and logical parent. Capture timestamps and page-specific 403 evidence are excluded. |
| Execution grant (`MigrationActionSignature`) | Exact per-capture authorization evidence binding source evidence, reviewed selection, the logical target, semantic state, and logical-parent digest. |
| Execution-group digest | Hash of the stable logical action keys needed by one shared plan. |
| Support-cohort digest | Normalized capability/action/target-profile signature. It excludes URLs and GUIDs so equivalent supported shapes group across tenants. |

The global DAG deduplicates equal target-slot/logical-semantic pairs and retains every distinct per-capture execution grant on that producer. Independent capture timestamps and different leaves sharing a root therefore reuse the same producer, while the same slot with an incompatible profile remains a blocker.

## Ownership

Migration-owned child Webs use the existing `pnp_reserved_web_original_identifier` and `pnp_reserved_web_migration_digest` properties. The second value is a stable semantic mapping digest, not a mutable planning probe. Exact owned reuse requires current title, template, configuration, language, permission inheritance, description, original identity, and mapping digest to match the generic action semantic digest.

Existing unowned Webs are collisions by default. An external host is reusable only when the plan binds its exact Web GUID and target profile. External root/child actions retain `ExternalApprovedHost` ownership and never receive migration markers.

## Durable execution and convergence

Planning observations are review evidence, not write authority. Every logical action is freshly probed immediately before execution and freshly read back afterward. The action plan selects one exact PR #5 execution grant for the journal intent/receipt; verified no-op reuse records a grant-bound `AlreadySatisfied` receipt without inventing a mutation intent. Both paths then record a matching `MigrationMutationVerificationReceipt` checkpoint. Lost ownership-recovery responses use the same fresh convergence rule as lost create responses. Only a successful parent query that returns an exact absent child is `CreateMissing`; transport-level HTTP `404` is an inspection failure, not proof that a child slot is empty.

Safe transitions remain narrow:

- `CreateMissing` may still be missing, may expose the exact interrupted-create fingerprint, or may already be exact owned state;
- `RecoverInterruptedCreate` may become exact owned state;
- owned reuse must remain exact owned reuse;
- external reuse must remain the same approved Web GUID and external profile.

When a create request loses its response after the target changed, the executor keeps the mutation inside the same signed action attempt, freshly probes, and accepts only exact recovery or exact owned convergence. The domain receipt records `MutationAttempted=true` and `OutcomeUnknownButConverged`; the generic journal receipt uses the matching outcome. Any foreign marker, semantic drift, target Site/parent change, or unavailable probe stops for replan/reapproval.

## Page reference and import proof

`SharedTopologyPageReference` v4 pins the shared-plan digest, global-DAG digest, action-plan digest, execution-group digest, support-cohort digest, partial source-fidelity records, and every required logical action's target slot, exact per-page execution grant, original identifier, expected ownership, URL, and path.

Page import must receive a complete `SharedTopologyExecutionProof`: all source plans, the DAG, action plan, and materialization receipt. The receipt is prior evidence only. Admission batch-probes every required root/intermediate/leaf action again, checks runtime Site/Web/parent IDs, logical/grant membership, ownership markers, external-host boundaries, and semantic state, then confirms the current target connection is the freshly verified leaf. Recomputing only an aggregate receipt digest cannot hide a tampered nested action receipt.

Source authorization limits remain visible after successful target storage. The page receipt is `PartiallyAccepted` rather than claiming full source topology fidelity.

## Non-goals

The capability does not create Site Collections, reconstruct denied source metadata, adopt unowned Webs automatically, or encode any project name, Wiki path, page ordinal, or cohort-specific rule. Hashes provide integrity inside the local journal trust boundary; they are not a MAC or external writer authentication mechanism.
