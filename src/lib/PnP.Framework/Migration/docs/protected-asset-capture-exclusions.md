# Protected document capture exclusions

> Status: Implemented
> Scope: Explicit source capture policy, List planning, execution, and fresh readback

## Boundary

Protected-document payload omission is opt-in. `PageCaptureOptions.ProtectedAssets`
is null by default, which preserves the historical capture-all behavior. A caller
that has reviewed this boundary can supply
`ProtectedAssetCapturePolicy.MetadataOnly(policyId)`.

The capture reader projects the existing Information Protection item metadata
before invoking the binary reader. The policy produces a digest-sealed
`ProtectedAssetCaptureDecision`:

- `SafeToCapture` invokes the ordinary binary reader and retains its existing
  ordinary-file, IRM-envelope, archive, and logical-content classification;
- `MetadataOnly` never invokes the document binary reader and does not capture
  attachments owned by that document-backed item.

An explicit policy is always fail closed. `SafeToCapture` requires the source
List to report `IrmEnabled=false` and complete item-level negative evidence:
the label is observed and empty, user-defined protection and encrypted-content
are observed and false, and both the decrypt-skip reason and RMS template ID
are observed and zero/empty. Any positive protection signal, including an empty
label plus `HasUserDefinedProtection=1`, or any missing evidence therefore never
reaches the binary reader. There is no production fail-open mode. With a null
policy the reader keeps its historical field projection and capture-all shape;
extended negative evidence is retained only for an explicit policy.

## Planning and execution

A metadata-only decision becomes one `ListProtectedDocumentExclusionPlan` bound
to the source item, mapped target path, policy ID, and decision digest. The
existing page ingredient graph keeps its current schema. Its List item, Document, optional
Information Protection policy, and attachment actions use the existing `Drop`
disposition. The owning List and independent sibling ingredients remain in the
existing dependency-aware execution frontier.

If a captured lookup value references a dropped item, the graph adds one
required value-level item edge. Decisions are sealed per exact edge by consumer
List/item/field and provider List/item. The planning policy must explicitly
choose `ClearValue` (remove only that provider value and release only that edge)
or `DropDependentItem`; an omitted decision becomes `NeedsPolicyDecision` and
defers only the dependent branch. Required single- and multi-value lookup fields
cannot use `ClearValue` because source schema still requires a value.

Dropping a dependent item feeds a fixed-point closure: its lookup consumers are
evaluated in turn, so A -> B -> C propagates when both reviewed edges select
`DropDependentItem`. `ClearValue` stops propagation. A dropped folder also
structurally drops its nearest captured child folders/files, then continues
through their dependants. These folder-path edges are intrinsic and do not need
a user lookup policy. Independent siblings remain executable throughout.

The exclusion is a document-backed-item exclusion, not a claim that SharePoint
can create document metadata without a file. No protected-payload replay or
cross-tenant label reproduction is offered.

## Verification

Fresh readback checks that both the migration-owned item identity and exact
mapped document path remain absent. The receipt classifies the path probe as:

- `Absent` for a successful negative lookup or literal HTTP 404;
- `Present` when the excluded item or file exists;
- `AuthorizationBlocked` only for literal HTTP 401/403;
- `RetryableFailure` for HTTP 408/409/423/429/5xx or retryable transport failure;
- `Failed` for other errors.

Only `Absent` passes verification. Retryable and authorization results are not
misreported as target presence.

## Compatibility

The capture policy, decision, plan exclusions, and receipt probes are optional
properties omitted from canonical JSON when unused. Existing export/migration
schema v2 packages and receipt v4 remain valid, and the ingredient graph/frontier
contracts are not duplicated or version-bumped. Potential lookup/folder item
edges are projected only when a v2 snapshot contains an optional metadata-only
capture decision, so ordinary legacy v2 reader shape and graph digests are
unchanged.
