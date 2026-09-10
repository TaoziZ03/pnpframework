# Persisted JSLink shared M0 identity extension

Baseline: `aff892f119a20dd764af6b005087e7af5dad3a1c` (CCD-182).
Scope: shared identity, owner predicate, graph projection and conformance tests only.

## Decision and versions

The frozen maturity interface remains `pnp-ingredient-maturity-assessment/v1`
and `pnp-ingredient-maturity-evaluator/v1`. No maturity type, gate, receipt,
evaluator, technical outcome or public enum changes. JSLink is represented
losslessly through the existing typed ingredient extension seam:

| Boundary | Contract |
| --- | --- |
| Package | Existing Ingredient-extension export/migration schema v4 |
| Graph | Existing canonical graph v2, Publishing projection v8 |
| Evidence payload | `pnp-publishing-page-jslink-reference-evidence/v1` |
| Primary-owner registry | Existing v1, additive `reference.jslink` and REST host-observation entries |
| Source predicate | `reference.persisted-webpart-jslink/v1` |
| Primary lane | Exactly `resource.script` |

`PublishingPageJsLinkReferenceEvidence` and the projection helper are internal.
The additive public source fields are optional `SourcePageFence.ETag` and
`ClassicWebPartSnapshot.PropertyEvidenceFormat/PropertyEvidenceJson/PropertyEvidenceArtifact`.
The latter reuses `ArtifactReference`; no second public artifact contract is
created. Null fields are omitted, preserving legacy source-fence and Web Part serialization.
An ETag is opaque evidence: it is never synthesized from `VersionLabel`.
When supplied, it must bind the fence to the source file; the owner-bound
`SourceVersionIdentity` is `etag=` followed by the exact original ETag.

The claim's descriptive `semanticRole` is retained verbatim. The registry's
internal `$source-bound` role selector is not a permissive ownership wildcard:
its JSLink predicate independently reads the single sealed typed envelope and
requires exact equality of the node's role, identity, source/version and digest.
Other registry entries keep literal-role matching. No page title, customer
site, host GUID or claim-specific role string is encoded in the registry.

## Lossless binding and source predicate

The payload requires:

- Original canonical ingredient ID, `reference.jslink` subtype and nonempty
  semantic role. The accepted ID shape is
  `ccd.ingredient.resource.script/v1:{page-guid:D}:{host-guid:D}:JSLink:{raw-locator}`.
  No `reference:` prefix, subtype rewrite, locator normalization or role rewrite
  is applied to that ID.
- Exact source file UniqueId, ListItem ID, server-relative page path and ETag,
  checked against `PublishingPageCaptureBundle.Source` and `SourceFence`.
  The existing source identity also retains site/web GUIDs.
- Exact host Web Part GUID, acquisition format, artifact SHA-256 and captured
  `ZoneIndex`. The frozen active claim's `hostWebPartOrder` is derived from
  `WebPart.ZoneIndex`, not from the order of the normalized evidence array.
  The host must occur exactly once and its zone index must match.
- `ReferenceForm = JSLink`, the complete persisted property value and zero-based
  `PersistedReferenceOrder`. Pipe separators, companion references and the
  original property text remain in evidence. The selected token must match the
  raw locator at that exact position. Repeated identical tokens cannot be
  disambiguated by this claim-ID shape and fail closed; do not silently merge them.
- An existing `PageReferenceSnapshot` carrying the script kind, raw/normalized
  locators, host consumer, captured status, content SHA-256/length and optional
  inline artifact bytes. `MigrationArtifactContractValidator` checks the
  existing artifact contract; M0 needs the digest, not customer script bytes.

The host evidence format is explicit:

- `rest-expanded-webpart-json`: the recorded normalized-property artifact has
  `id`, `properties`, `title`, `type` and `zoneIndex`. Preserve the exact JSON
  artifact bytes and original digest; do not reserialize them with a different
  canonicalization or add a transport LF. `ClassicWebPartPropertyEvidenceValidator`
  uses the existing artifact validator, checks host ID/zone and rejects duplicate
  JSON property names. It reads only the direct `properties.JSLink` binding.
  `ExportXml`, `ExportSha256` and unavailable CLR type remain unavailable; the
  host graph explicitly labels this as REST properties, not a native export.
- `native-webpart-export-xml`: the predicate recomputes `ExportSha256` and
  reuses `ClassicListWebPartBindingParser.Parse`. It additionally requires one
  direct persisted `JSLink` property, so the parser's `XmlDefinition.JSLink`
  fallback cannot change the reference form. This path requires a genuine
  PnP-parseable list-bound Web Part export; never synthesize XML to enter it.

For the frozen active claim, the direct JSLink value is only
`~Site/SiteAssets/bloghome.js`. `sp.ui.blogs.js` belongs to the separate
`XmlDefinition.JSLink` observation; it is **not** appended to the direct value.
The complete host artifact retains that companion observation without assigning
it to this canonical instance. Other reference forms need their own source
predicate, not a relabelled direct-JSLink claim.

The existing `PageReferenceSnapshotReader.TryResolveUri` validates locator
normalization. This canary accepts HTTP(S) resources on the source authority;
no URL probing, token acquisition or target write is performed. Script runtime
support, content extraction, policy and comparison remain domain responsibilities.

The graph adds the exact reference node and a required, read-only
`DependsOn -> webpart:{host-guid}` edge. The container remains owned by
`webpart.instance`, including when its authority is REST property observation.
The reference payload does not grant ownership or
materialization of the host, List, View or runtime projection.
The existing native replay policy still rejects an absent export; this change
does not make a REST observation replayable or release a required dependency.

Do not also insert the envelope's reference into `snapshot.Dependencies`: that
would project a second generic node for the same persisted binding. A matching
host/locator generic dependency fails closed until the adapter reconciles it.
Independent body or other-host observations remain separate and are not removed.

Captured evidence with literal authorization-denial evidence is rejected by
this positive M0 path. Apply the existing per-instance fail-soft policy outside
it: keep unavailable/degraded evidence, skip only unsafe dependent writes and
continue independent work. A skipped inaccessible script is not a successful
copy, equality result or M5 claim.

## Lane integration template

The owner creates a lane-local handler, registered in the authorized composition
root with the **default** `PublishingPageIngredientPrimaryOwnerRegistry`. The
shared catalog validates the payload schema; the lane needs no private registry.

```csharp
internal sealed class ResourceScriptIdentityHandler
    : PublishingPageIngredientHandler<PublishingPageJsLinkReferenceEvidence>
{
    public override PageIngredientHandlerDescriptor Descriptor { get; } = new(
        "pnp.resource-script.identity/v1",
        new PageIngredientLaneDescriptor("resource.script", new[] { "classic-wiki", "publishing" }),
        new[] { PublishingPageJsLinkReferenceEvidence.SchemaVersion },
        PublishingPageIngredientGraphProjector.IngredientExtensionProjectionVersion,
        700,
        new[] { new PageIngredientIdOwnership(PageIngredientIdOwnershipKind.Prefix,
            PublishingPageJsLinkReferenceEvidence.IngredientIdPrefix) });

    protected override void ProjectGraph(
        PublishingPageIngredientGraphProjectionContext context,
        PublishingPageIngredientEvidenceEnvelope envelope,
        PublishingPageJsLinkReferenceEvidence evidence)
        => context.AddPersistedJsLinkReference(envelope, evidence);

    // Domain actions and evidence remain lane-owned. No default action or
    // positive technical outcome is supplied by the identity helper.
}
```

Use `PublishingPageIngredientEvidenceEnvelope.Create(handler, schema,
evidence.IngredientId, evidence, refs)` and `catalog.OrderEvidence(...)`.
Then project with `PublishingPageIngredientGraphProjector.Project(snapshot,
catalog)`. The maturity contributor passes the projected node, same snapshot,
default registry and original claim identity to the unchanged `ValidateM0`.
It passes the script content digest as `SourceArtifactDigest` and recomputes
`SourceSnapshotDigest` with `PublishingPageDigest.ComputeSnapshotDigest`.
Use the same catalog at package validation, projection and planning.

The production lane handler, M1-M5 contributor, materializer, fresh readback and
Compare implementation are intentionally **not** included in this shared change.
The handler template is not an implementation pass or an independent verdict.

## Compatibility and frozen ownership

- Old snapshots with no extension evidence still take projection v7. Existing
  generic script references keep `reference.script`, `typed-script-reference`,
  `reference.kind-script` and their legacy node IDs.
- Existing v8 users with other evidence schemas retain their registered handlers
  and literal owner tuples. Unknown handler/schema, duplicate key and overlapping
  handler ownership remain fail-closed.
- Old consumers cannot validate the new evidence schema and must reject it;
  they must not discard the ETag/envelope and recalculate a legacy digest.
  Upgrade producer and consumer together for this opt-in payload. Do not modify
  an already sealed package, plan or receipt in place; regenerate using the
  existing digest/plan admission contracts after integration.
- No change to target clients, generic actions, receipts, execution journals,
  Compare, retry logic, PnP Core SDK dependencies or eight lane implementation
  roots. PnP's existing parser, reference resolver, artifact validator,
  serializer, digest, catalog, graph context and M0 validator are reused.
- Newly frozen shared files: `PublishingPageJsLinkReferenceEvidence.cs` and
  `PublishingPageJsLinkReferenceProjector.cs` under
  `Migration/Pages/Publishing/Ingredients`. Registry, graph-context, handler
  catalog, source fence, Web Part property-evidence validator/snapshot/projector,
  reference-resolver visibility and shared tests are also
  shared-owner paths. Lane owners copy only the template into their approved roots.

## Conformance and evidence limits

`PublishingPageJsLinkM0Tests` uses the frozen active claim's exact ID, subtype,
role, page/host IDs, item ID, ETag, script length and digest. The source authority
is `source.example.invalid`; the REST host property artifact is a minimal
generated fixture that preserves direct-vs-embedded binding and unavailable CLR
identity. A separate test covers the genuine-native-XML input contract.
It includes no customer script bytes, credentials, network calls, current-time
dependency, target mutations or self-authored live maturity verdict.

Positive coverage proves one default owner and all three unchanged M0 gates.
Negative coverage includes redigested wrong subtype, foreign/missing/duplicate
host, host and reference order, stale source version, foreign page/item/fence,
missing/invalid payload or host digest, changed host artifact/property, alternate
reference form, locator mismatch, duplicate generic projection, denied capture
and caller-corrupted claim/node pairs. It also proves ZoneIndex is independent
of evidence-array order. Existing maturity and extension-catalog
tests must also pass.

The narrow filter is:

```text
FullyQualifiedName~PublishingPageJsLinkM0Tests|FullyQualifiedName~IngredientMaturityEvaluatorTests|FullyQualifiedName~PublishingPageIngredientExtensionContractTests
```

Build/test with the repository's .NET 10 SDK. On a Windows-local checkout:

```text
dotnet msbuild src/lib/PnP.Framework.Test/PnP.Framework.Test.csproj -restore -t:Build -p:TargetFramework=net10.0 -p:TargetFrameworks=net10.0
dotnet vstest src/lib/PnP.Framework.Test/bin/Debug/net10.0/PnP.Framework.Test.dll /TestCaseFilter:<filter-above>
```

For a WSL UNC checkout, use CCD-251's verified managed SDK and copy the complete
test output to a run-owned Windows-local directory before `dotnet vstest`;
verify both product and test DLL hashes. The SDK/staging route is an environment
detail, not a product source change.

Completion of CCD-306 releases only its shared-contract blocker on CCD-157.
PnP Lead integration, the lane implementation, CTO review and independent
Verification remain separate; no live claim maturity is assigned here.
