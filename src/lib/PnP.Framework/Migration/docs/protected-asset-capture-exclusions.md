# Protected document capture exclusions

> Status: Implemented
> Scope: Explicit source capture policy, List planning, execution, and fresh readback

## Boundary

Protected-document payload omission is opt-in. `PageCaptureOptions.ProtectedAssets`
is null by default, which preserves the historical capture-all behavior. A caller
that has reviewed this boundary can supply
`ProtectedAssetCapturePolicy.MetadataOnly(policyId, failClosedOnUnknown)`.

The capture reader projects the existing Information Protection item metadata
before invoking the binary reader. The policy produces a digest-sealed
`ProtectedAssetCaptureDecision`:

- `CaptureBinary` invokes the ordinary binary reader and retains its existing
  ordinary-file, IRM-envelope, archive, and logical-content classification;
- `MetadataOnly` never invokes the document binary reader and does not capture
  attachments owned by that document-backed item.

`failClosedOnUnknown` applies only after the caller explicitly selects the
policy. It does not change the library default.

## Planning and execution

A metadata-only decision becomes one `ListProtectedDocumentExclusionPlan` bound
to the source item, mapped target path, policy ID, and decision digest. The
existing page ingredient graph is unchanged. Its List item, Document, optional
Information Protection policy, and attachment actions use the existing `Drop`
disposition. The owning List and independent sibling ingredients remain in the
existing dependency-aware execution frontier.

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
contracts are not duplicated or version-bumped.
