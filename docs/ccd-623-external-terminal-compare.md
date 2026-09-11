# Shared Compare v3: external terminal evidence

## Decision and authority

Use an additive `pnp-page-compare-report/v3` profile for a **single-ingredient
terminal observation**, with an internal, typed intake. Do not relabel CCD
lifecycle/browser bytes as native PnP import/runtime receipts. v1/v2 retain
their existing entry points, schema constants, native admission, serialization
shape and digest algorithm.

This profile solves the historical negative closure, not successful import
admission. Its only acceptance verdicts are `fail` and `conditional`. It cannot
award maturity, certify a whole page/batch, revalidate absent plan bytes, or
replace a lane's domain comparer. Shared architecture review is required because
the change touches Compare/schema and reused shared validation helpers.

The exact consumer remains the implementation at
`8de62c9d4268f9017f6f51a4b5a9447b5eb2e6ec`, tree
`4a498a34a0f3d7b95957638937c7706e8490d08a`. A new Compare producer does not change
the producer of historical tenant operations or independent browser observations.

## Frozen interfaces and files

Internal intake versions:

- `pnp-page-compare-external-terminal-evidence/v1`
- `pnp-page-compare-external-terminal-admission/v1`
- Entry: `PublishingPageCompareReconciler.ReconcileExternalTerminal(request,
  admission, artifactStore)`.
- Consumer validation: `ValidateExternalTerminalReport(report, request,
  admission, artifactStore)` reopens the same independent pins and rederives
  the complete result; recomputing a report hash is not acceptance.
- Request seam: internal `PublishingPageCompareRequest.ExternalEvidence`.
- Result: the existing `PublishingPageCompareReport`, with nullable
  `CompareExternalEvidenceSummary` omitted from v1/v2 serialization.

Shared/frozen implementation paths, all relative to the repository:

- `src/lib/PnP.Framework/Migration/Pages/Publishing/Comparison/PublishingPageCompareContracts.cs`
- `src/lib/PnP.Framework/Migration/Pages/Publishing/Comparison/PublishingPageCompareReconciler.cs`
- `src/lib/PnP.Framework/Migration/Pages/Publishing/Comparison/ExternalTerminalCompareEvidence.cs`
- `src/lib/PnP.Framework/Migration/Pages/Publishing/Comparison/PublishingPageExternalCompareReconciler.cs`
- `src/lib/PnP.Framework/Migration/Pages/Assessment/Maturity/External/Batch1SealedPlanEvidenceAdapter.cs`
- `src/lib/PnP.Framework/Migration/Pages/Assessment/Maturity/External/SharedTargetLifecycleEvidenceAdapter.cs`
- `docs/schemas/pnp-page-compare-report-v3.schema.json`

Permanent shared tests and raw fixture bytes:

- `src/lib/PnP.Framework.Test/Migration/Pages/Publishing/Comparison/ExternalTerminalCompareConformanceTests.cs`
- `src/lib/PnP.Framework.Test/Migration/Pages/Publishing/Comparison/ExternalTerminalCompareFixtures.resx`

Lane owners must not edit these files or invent a private receipt schema. This
change does not register a lane, change an owner predicate, add an enum member,
or modify any lane implementation/fixture, target writer or lifecycle producer.

## Why a versioned terminal profile

The consumer archive is SHA-256
`13f88da7b01a5a1f3a2a3016ae37920f99d4cee18ad535520571ee19bd1d1171`.
All 16 manifest members were checked before intake. It contains original
external receipts, but no sealed plan/input bytes or full domain comparison
annex. It also contains an explicit unavailable native-cleanup observation.

A translation to native PnP v5 import receipts would assert an importer execution
and step sequence that this packet cannot prove. A native runtime-receipt label
would likewise misstate the independent browser receipt's producer/schema.
Merely widening a v1 schema constant would silently change existing consumers'
meaning. v3 names the original formats and limits its authority explicitly.

| Evidence role | Original schema | Meaning in v3 |
| --- | --- | --- |
| Manifest | `ccd.shared-target-lifecycle-manifest/v1` | Exact execution/claim/source/producer references; not a reopened plan |
| Fresh readback | `ccd.shared-target-lifecycle-receipt/v2` | Bound File/ListItem/List/ETag observation; not lane value equality |
| Browser review | `ccd.browser-runtime-review/v1` | Independently pinned browser observation and required gate outcome |
| Cleanup unavailable | `ccd.lifecycle-cleanup-transport-observation/v1` | Native operation receipt is absent; never a substitute native receipt |
| Post-cleanup | `ccd.shared-target-lifecycle-receipt/v2` | Bound page/site/marker absence; not proof of cleanup execution steps |

The current profile requires these five roles. A complete native-cleanup success
or another external format needs a separately reviewed shared evolution; do not
fit it into this unavailable-receipt profile or downgrade the old native gates.

## Independent admission and fail-closed binding

The trusted consumer supplies `ExternalTerminalCompareAdmission` independently
of the bytes being checked. It must not build its expected pins from the same
untrusted receipts merely to make them match. The existing CCD-559 identity types
are reused; this is not a second public plan, graph, lifecycle or maturity contract.

Required bindings include:

- Claim ID, ingredient ID, `PageIngredientKind`, subtype, semantic role, primary
  lane and source predicate. Source role/predicate and implementation tree are
  explicit consumer pins, not fields invented inside a CCD lifecycle receipt.
- Exact source URL/path, List ID, item ID, File UniqueId and ETag; exact target
  origin/site/web/list/page hierarchy, target profile, File/ListItem/List/ETag.
- Original plan schema and admitted `planDigest`; lifecycle run, lifecycle and
  mapping digests; string native operation/action IDs; producer path/digest;
  implementation commit/branch/tree; row and consumer issue.
- Separate browser review issue/run, expected required surface flags and body
  threshold; independently bounded UTC window and readback polling limits.
- A SHA-256 pin for every original artifact. `MigrationArtifact.ReadAllBytes`
  reopens bytes and verifies length/digest; `ExternalEvidenceJson` rejects
  duplicate properties and malformed/unavailable artifacts.

Missing, corrupt, stale, future, wrong-schema, wrong-plan, wrong-source,
wrong-target, foreign operation/action/producer and weakened browser-policy
inputs fail closed for this single instance. An isolated denial is not an IT or
batch-wide authentication blocker. The caller continues independent instances.

The lifecycle binding extraction is reused from
`SharedTargetLifecycleEvidenceAdapter.ValidateReceiptBinding`; bounded readback
is reused from `ValidateBoundedPoll`. Existing maturity callers still execute all
their original phase, dependency primitive, ownership, runtime and cleanup checks.
Exposing these internal helpers and path predicates does not admit incomplete
lifecycles to M4.

Browser receipt validation checks claim/source-derived execution context,
operation, target tuple, requested/observed/final URLs, fresh tab/cache settings,
review time after fresh storage readback, original observation seal, independent
gate policy and observed gate result. A declared `BROWSER_RUNTIME_PASS` cannot
override the required missing surface observations. Unknown/mismatched gate
codes fail closed. HTTP `200` is not runtime fidelity.

## Result and non-authority

For the original packet the report must retain:

- `runtime.status = failed`, `BROWSER_RUNTIME_FAIL`, and the three failed gates:
  `ASPNET_FORM_MISSING`, `CLASSIC_SHAREPOINT_SURFACE_MISSING`,
  `WIKI_RUNTIME_SIGNAL_MISSING`; observed `modern-sharepoint-shell`.
- `externalEvidence.nativeCleanupReceiptStatus = unavailable`, null native
  cleanup receipt digest and reason `NATIVE_CLEANUP_RECEIPT_UNAVAILABLE`.
- `FRESH_POST_CLEANUP_ABSENCE_PASS` as a separate observation, not cleanup success.
- `acceptance.verdict = fail` regardless of storage identity success. A synthetic
  valid browser-positive control is still only `conditional`, never PASS.

The packet's persisted layout facts are preserved in the referenced original
readback. The shared adapter does not normalize BaseTemplate, Content Type,
PublishingPageLayout, WikiField, or any other domain value. Consequently
`storage.status = not-run` refers to **domain comparison**, while
`externalEvidence.storageIdentityStatus = verified` records the validated fresh
identity. The existing page.layout domain verdict is not reversed or re-awarded.

`planEvidenceStatus = admitted-digest-reference-only` and null source/package
bindings mean those original bytes were not supplied/revalidated here.
`sourceVersionComparison.status = not-checked` is not fresh source collection.
There is no `attainedMaturity` or execution/readiness admission in this report.

The v3 report digest uses the existing PnP canonical serializer and SHA-256,
with only `reportDigestSha256` set to null in its original root position. It
seals the whole report, including dates, original schema/digest references,
unavailable cleanup and negative gates. This differs explicitly from the frozen
v1/v2 semantic digest. v1/v2 refuse an attached v3 external summary.
The external gate names are not emitted as native `RuntimeRequirementIds`.
Resealed PASS, runtime, cleanup, domain, schema, plan, source and timestamp
mutations are rejected by the context-bound report validator.

## Implementation template

This is a shared internal producer template, not code to copy into eight public
lane contracts. Reuse original artifacts and the independently reviewed claim
record. Only artifact locators/pins and source/target context vary by instance.

```csharp
// Inside the PnP assembly: pins come from the trusted consumer's admission.
var request = new PublishingPageCompareRequest
{
    GeneratedAtUtc = reportTimeUtc,
    Producer = exactCompareProducer,
    ExternalEvidence = new ExternalTerminalCompareEvidence
    {
        Manifest = manifestArtifact,
        FreshReadback = readbackArtifact,
        BrowserReview = browserArtifact,
        CleanupUnavailable = unavailableCleanupObservation,
        PostCleanup = absenceArtifact
    }
};
var report = PublishingPageCompareReconciler.ReconcileExternalTerminal(
    request, independentAdmission, originalArtifactStore);
// Never assign a lane-authored acceptance/maturity or relabel a raw receipt.
```

`ExternalTerminalCompareTestFixture` is the executable complete construction
example. Its original five byte arrays are immutable; counterfactual reseals are
test-only mutations and not new live evidence. The optional MSTest run parameter
`ExternalReportGeneratedAtUtc` permits an operator-bound report generation time;
normal UTs use a fixed clock and require no network, credentials or current time.

## Compatibility, review and integration

The shared base is approved CCD-559
`0667d1390b3cb51ab22f506b8c7caf33bcff0a63`. Review the final candidate's exact
commit/tree, not a dirty development label. Existing native contributors remain
on v1/v2 and need no changes. Consumers must negotiate v3 explicitly; a v1-only
validator must reject v3, not silently down-convert it. A multi-ingredient
aggregator must retain this report's single-ingredient scope and cannot replace
other lanes' page/batch evidence with it.

The Architect authors this shared change and does not self-approve it. A
separate non-author shared-contract review must inspect the new profile and
the extracted helper seams. PnP Lead owns integration admission and wiring;
the original page.layout owner resumes its terminal producer after admission.
Independent Verification still owns the product verdict and M5, and CTO final
cross-system/readiness review remains separate from this bounded code review.

Author development verification: 348 shared tests passed (73 new Compare plus
275 existing Compare/maturity), 0 failed/skipped. The generated report also
passed Ajv 8.20.0 with format validation, 14 negative schema controls and an
independent SHA-256 recomputation. Final exact-commit build/test hashes and the
review task are recorded in the issue's downloadable handoff, not asserted by
this source document before that run exists.

## KB and environment evidence

Scenario-first query/read selected:

- `kb-local/spo/classic-page/reproduction/implement-cross-family-page-content-reproduction.md`
- `kb/private/team/ccm/page-compare/page-remediation-worker-runbook.md`

Atomic follow-up query/read selected
`kb-local/spo/classic-page/reproduction/native-import-receipts-require-execution-steps.md`.
The negative evidence agrees with the requirement that schema validity is not
native execution provenance and storage success is not runtime success.

CCD-628 restored the exact historical consumer commit from verified prerequisite
bundles without switching/resetting worktrees. The branch remains
`OFFLINE_ONLY_NOT_PUBLISHED`; object availability is sufficient for bounded
compatibility inspection, not publication. No source/CUPCollect/Sandbox/ULS
action is needed or authorized by this implementation task.
