# Path-derived shared topology

> Status: Draft
> Implementation status: Partial source fidelity, generic action signatures, global scheduling, durable checkpoints, and page proof validation implemented
> Scope: Target Web preparation when source ancestor reads return literal HTTP `401` or `403`

## Partial source evidence

An ancestor-read denial does not erase facts captured before the failure. `PathDerivedSourceTopologyEvidence` v2 retains the real source Site ID, captured root Web identity/metadata, captured leaf Web identity/metadata, and exact decoded root-to-leaf path. Every intermediate path whose Web GUID and metadata could not be read becomes its own authorization-limited fidelity ingredient. The literal response is bound to the expected operation, request URI, authority, and source-closure action; message text or a CSOM error payload is insufficient.

The model does not invent intermediate Web GUIDs. Source-owner bindings use an authority/Site/path key at every level, carry real GUIDs for the known root and leaf, and leave unknown ancestor GUIDs empty. This preserves root-scoped content types and `Style Library` resources while keeping unknown intermediate identity explicit.

## Target actions and identities

The target Site root is a verified external global action. Each target child Web is another action. Four identities serve different purposes:

| Identity | Purpose |
| --- | --- |
| Target slot key | Canonical target authority + expected Site GUID fence + Site path + Web path. The same path in another tenant or Site is a different slot. |
| `MigrationActionSignature` | Generic durable action identity binding source evidence, reviewed selection, target slot, expected semantic state, and parent signature. |
| Execution-group digest | Hash of the concrete target-specific action signatures needed by one shared plan. It replaces the old URL-specific value that was incorrectly called a support cohort. |
| Support-cohort digest | Normalized capability/action/target-profile signature. It excludes URLs and GUIDs so equivalent supported shapes group across tenants. |

The global DAG deduplicates only equal target-slot/action-signature pairs. The same slot with a different signature is a blocker; page ordering, worker arrival, or a lease timeout cannot choose a winner.

## Ownership

Migration-owned child Webs use the existing `pnp_reserved_web_original_identifier` and `pnp_reserved_web_migration_digest` properties. The second value is a stable semantic mapping digest, not a mutable planning probe. Exact owned reuse requires current title, template, configuration, language, permission inheritance, description, original identity, and mapping digest to match the generic action semantic digest.

Existing unowned Webs are collisions by default. An external host is reusable only when the plan binds its exact Web GUID and target profile. External root/child actions retain `ExternalApprovedHost` ownership and never receive migration markers.

## Durable execution and convergence

Planning observations are review evidence, not write authority. Every action is freshly probed immediately before execution and freshly read back afterward. Mutations use the PR #5 generic `MigrationActionSignature`, so the JSON Lines journal records a signature-bound action intent/receipt; verified no-op reuse records a signature-bound `AlreadySatisfied` receipt without inventing a mutation intent. Both paths then record a matching `MigrationMutationVerificationReceipt` checkpoint. Only a successful parent query that returns an exact absent child is `CreateMissing`; transport-level HTTP `404` is an inspection failure, not proof that a child slot is empty.

Safe transitions remain narrow:

- `CreateMissing` may still be missing, may expose the exact interrupted-create fingerprint, or may already be exact owned state;
- `RecoverInterruptedCreate` may become exact owned state;
- owned reuse must remain exact owned reuse;
- external reuse must remain the same approved Web GUID and external profile.

When a create request loses its response after the target changed, the executor keeps the mutation inside the same signed action attempt, freshly probes, and accepts only exact recovery or exact owned convergence. The domain receipt records `MutationAttempted=true` and `OutcomeUnknownButConverged`; the generic journal receipt uses the matching outcome. Any foreign marker, semantic drift, target Site/parent change, or unavailable probe stops for replan/reapproval.

## Page reference and import proof

`SharedTopologyPageReference` v3 pins the shared-plan digest, global-DAG digest, action-plan digest, execution-group digest, support-cohort digest, partial source-fidelity records, and every required action's target slot, full generic signature, original identifier, expected ownership, URL, and path.

Page import must receive a complete `SharedTopologyExecutionProof`: all source plans, the DAG, action plan, and materialization receipt. Validation replays every contract validator, checks nested action and source-owner receipt digests, matches signed verification checkpoints, and then freshly compares the current target leaf Web with its semantic action digest. Recomputing only an aggregate receipt digest cannot hide a tampered nested action receipt.

Source authorization limits remain visible after successful target storage. The page receipt is `PartiallyAccepted` rather than claiming full source topology fidelity.

## Non-goals

The capability does not create Site Collections, reconstruct denied source metadata, adopt unowned Webs automatically, or encode any project name, Wiki path, page ordinal, or cohort-specific rule. Hashes provide integrity inside the local journal trust boundary; they are not a MAC or external writer authentication mechanism.
