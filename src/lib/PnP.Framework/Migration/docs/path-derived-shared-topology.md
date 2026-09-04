# Path-derived shared topology

> Status: Draft
> Implementation status: Generic exact-path planning, global action DAG, CSOM execution, and page references implemented
> Scope: Target child-Web containers when source ancestor-Web fidelity is authorization-limited

## Why this capability exists

A source page can retain trustworthy Site ID, leaf Web ID, Site Collection path, and leaf Web path even when reading the complete ancestor-Web closure returns a literal HTTP `401` or `403`. The denied source metadata and the target path are different facts. Source fidelity must remain visibly authorization-limited, but that limitation does not by itself make an exact target child-Web path unsafe to plan.

`PathDerivedSourceTopologyEvidence` therefore retains only facts already known from the page capture boundary plus digest-valid `LiteralHttpAuthorizationEvidence`. It does not invent ancestor Web IDs, titles, templates, configurations, language, permissions, or feature state. Every target creation value comes from a separate reviewed `PathDerivedTargetWebProvisioningPolicy`.

## Independent ingredients and shared actions

Each decoded relative path segment is one `TargetWebContainerIngredientPlan`. For a leaf below two child Webs, the plan contains two independently identified containers and an explicit parent edge. This lets Lists, content types, pages, and later migration domains reference the exact owner container instead of treating the whole path as one opaque page-local operation.

The same container can be needed by many pages. Page plans do not copy or execute that shared producer. They carry a `SharedTopologyPageReference` with the required global-action chain and support-cohort signature. A host compiles all reviewed shared plans into one `SharedTopologyGlobalActionDag` and executes each equivalent producer once.

Four stable identities keep deduplication and conflict detection separate:

| Identity | Meaning |
| --- | --- |
| Target slot key | The canonical target server-relative Web path. |
| Action signature digest | Stable target-only creation values, source provenance, parent action, ownership boundary, and collision decision. It excludes mutable target observations. |
| Global action key | Hash of target slot plus action signature. |
| Support-cohort signature | Hash of the sorted global actions required by one shared plan. |

Equivalent slot/signature pairs deduplicate. The same slot with different signatures is a blocking `SharedTopologyTargetSlotSignatureConflict`; ordering, page number, or whichever worker arrives first never chooses a winner.

## Target admission and ownership

The default is conservative:

- a missing exact path selects `CreateMissing`;
- exact matching original-identifier and action-signature markers select `ReuseOwned`;
- an exact deterministic interrupted-create fingerprint with empty markers selects `RecoverInterruptedCreate`;
- an unmarked existing Web is a collision;
- an unmarked existing Web can select `ReuseExplicitApprovedHost` only when policy binds its exact target Web ID;
- literal target HTTP `401` or `403` becomes `AuthorizationBlocked` only with digest-valid wire evidence;
- `408`, `409`, `423`, `429`, and `5xx` are retry-required observations, not ownership decisions.

Migration-owned Webs use the existing reserved properties `pnp_reserved_web_original_identifier` and `pnp_reserved_web_migration_digest`. The mapping digest is the stable action signature. Conflicting values are never overwritten. An explicitly approved external host stays external: execution does not write either migration-ownership marker, and its receipt records `ExternalApprovedHost`.

## Execution, races, and resume

The reviewed target analysis is approval evidence, not mutation authority. `PathDerivedTopologyMigrationService` performs these steps for every global action in parent-first order:

1. freshly inspect the exact direct-parent slot;
2. compare the fresh state with the reviewed action and accept only safe forward transitions;
3. write a mutation intent before create or recovery;
4. perform the action, or record exact reusable state as already satisfied;
5. freshly inspect the same slot again;
6. seal runtime Site/Web/parent IDs, ownership, markers, action signature, and readback result in the per-action receipt.

`CreateMissing` may safely advance to `RecoverInterruptedCreate` or `ReuseOwned`. This covers a crash after Web creation and a concurrent worker that completed the same global action. `RecoverInterruptedCreate` may advance to `ReuseOwned`. External reuse must remain the same approved external Web ID. Missing-after-recovery, foreign markers, parent drift, target Site drift, a different signature, or authorization/retry failure requires replan and reapproval; execution does not broaden the action.

The shared receipt is the source of runtime target IDs for page-local List and dependency work. Page import validates the receipt digest, required global-action chain, leaf Site/Web identity, current ownership markers, and current target connection before page mutation. A source `403` remains in the page reference and import receipt as an acceptance limitation. Target storage can pass while acceptance is `PartiallyAccepted`; this does not claim full source topology fidelity.

## Scope and non-goals

The implementation is path- and domain-generic. It contains no project name, Wiki path, page ordinal, or cohort-specific rule. The current executor provisions child Webs inside an existing target Site Collection. Tenant-level Site Collection creation, source metadata reconstruction, feature inference, and automatic adoption of unowned Webs remain outside this capability.
