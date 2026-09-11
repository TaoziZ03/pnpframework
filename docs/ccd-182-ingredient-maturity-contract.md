# Unified Ingredient maturity contract

Contract: `pnp-ingredient-maturity-assessment/v1`

Evaluator: `pnp-ingredient-maturity-evaluator/v3`
Scope: internal migration tooling. This revision does not add M0-M5 to the public PnP API.

## Decision

`IngredientMaturityEvaluator` is the only component that assigns `AttainedMaturity`. A lane implements `IIngredientMaturityContributor` and returns typed gate receipts created by `IngredientMaturityEvidenceValidator`; a contributor cannot declare its own maturity. The evaluator requires every gate in every preceding level, rejects unknown or duplicate receipts, and computes the highest continuous passing level.

Technical outcome remains independent. `IngredientTechnicalOutcome` retains the existing `PageMigrationOutcome` plus `Pass`, `Conditional`, `Fail`, or `Unverified`. Therefore `M5 + Conditional` is valid when the implementation and evidence are complete but the product result is deterministically policy-limited or unsupported.

The assessment binds:

- `claimId`, work-item type, lane, ingredient/assertion ID, `PageIngredientKind` when persisted, subtype, semantic role, and source predicate;
- source page/list-item identity, source version, raw artifact digest, and snapshot digest;
- target profile and canonical target identity;
- producer ID/version, full implementation commit, and optional binary digest;
- every required gate receipt, validator identity/version, and evidence references;
- target maturity, computed attained maturity, confidence, first missing promotion gate, independent technical outcome, evaluator version, and canonical assessment digest.

`behavior.interaction` uses work-item type `runtime-verification`, has no persisted `PageIngredientKind`, and identifies an assertion bound to canonical ingredient/action IDs in its lane evidence. Its M0 call supplies `IngredientRuntimeAssertionEvidence`: a nonblank, matching source predicate, the existing canonical `PageIngredientNode`, and the existing `PageIngredientAction`. The action must address that node and list the assertion ID exactly once in `VerificationAssertions`; the dependency must match the claim's source identity/version/artifact digest. This is a read-only dependency, not ownership of the persisted node. All other lanes use `canonical-ingredient` and must resolve exactly one primary owner through `PublishingPageIngredientPrimaryOwnerRegistry`.

## Consumer validation and trust boundary

`ValidateAssessment(assessment)` revalidates the intrinsic summary after deserialization:

- exact schema/evaluator versions and canonical digest;
- complete, ordered M0-M5 levels and the single frozen gate catalog (no missing, extra, duplicate, reordered or misclassified gates);
- recognized work-item/lane/kind/status/outcome enums, required identity/source/target/producer/predicate/reason fields;
- supported common-validator identity/version, canonical references and passed/failed/missing gate metadata;
- independently recomputed continuous `Levels[].Passed`, `AttainedMaturity`, `Confidence` and `MissingPromotionGate`.

A canonical digest is **not a signature or an authenticity boundary**. A coherent replacement of an entire summary, including a valid different technical outcome or invented gate evidence, cannot be disproved from that same summary alone. Admission consumers must use:

```csharp
IngredientMaturityEvaluator.ValidateAssessment(
    independentlyReadAssessment,
    independentlyObtainedContext,
    contributorsThatRevalidateOriginalEvidence);
```

This overload reruns the contributor/common-evaluator path against the independent context and compares the entire resulting canonical assessment. Never construct that context from the submitted assessment or implement a contributor by echoing its submitted gates. The overload binds `TechnicalOutcome` without inventing domain outcome rules. It also rejects a coherently reawarded missing gate. `Evaluate` returns a detached snapshot rather than retaining aliases to the caller's mutable bindings.

## M1 observation binding

Use `ValidateM1(context, liveEvidence)`. The consumer supplies `ObservationWindowStartUtc` and `ObservationWindowEndUtc` from its collection/readback run; neither comes from wall-clock inference during validation or from the submitted summary.

Each `IngredientValueObservation` carries the exact claim ID, ingredient ID, existing source binding (page/item, version, artifact digest, snapshot digest), existing target binding (profile, canonical identity), canonical value path, value digest, UTC observation time, live origin and evidence reference. The reference must be present in the corresponding source/target evidence-reference set. Source observations must lie between the start fence and `ReadbackStartedAtUtc`; target observations must lie between that readback start and the end fence.

Every canonical value path requires exactly one authenticated-source and one fresh-target observation. Duplicate, missing, foreign, stale, future, unknown-origin, historical or synthetic observations fail M1. Lanes own the mapping from domain locators into value paths. Different source and target value digests are retained, not declared equal or used to manufacture a shared outcome rule: a reviewed transform can legitimately remain conditional.

## Required gates

| Level | Required gates | Validation reuse |
| --- | --- | --- |
| M0 | canonical identity; unique primary owner; version-bound source binding | `PublishingPageIngredientPrimaryOwnerRegistry` and canonical graph node tuple |
| M1 | authenticated source collection; CUPCollect fresh readback; per-value observations | explicit live-origin evidence; historical/synthetic substitution fails |
| M2 | raw artifact bytes/length/store integrity; normalized semantic integrity | `MigrationArtifactContractValidator`, `MigrationContractSerializer`, `MigrationDigest` |
| M3 | source snapshot/plan binding; one legal action/disposition; dependency/policy closure | `PublishingPageDigest.ComputePlanDigest`, `PageIngredientPlanEvaluator` |
| M4 | admitted exact plan; operation/action signature; mutation/journal/verification receipts; ownership/provenance; fresh storage; runtime/cleanup/retry | `MigrationActionSignature`, existing mutation/import/state receipts, `RuntimeVerificationContractValidator` |
| M5 | hermetic RED to GREEN UT; exact code/build/binary; same-commit CUPCollect E2E; deterministic Compare; Architect, CTO, independent Verification; Draft PR/PR-ready commit | `PublishingPageCompareReconciler.ComputeReportDigest` plus delivery/review evidence |

PnP-domain gates and delivery-process gates are distinct in every serialized gate result. M0-M4 are domain gates. M5 Compare is a domain gate; the remaining M5 gates are delivery-process gates.

## Lane integration template

Each owner adds a lane-local contributor without editing the shared contract, gate catalog, validator, or evaluator:

```csharp
internal sealed class ContentTextMaturityContributor : IIngredientMaturityContributor
{
    public string ContributorId => "pnp.content-text-maturity/v1";
    public string Lane => "content.text";

    public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
    {
        var receipts = new List<IngredientMaturityGateReceipt>();
        receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM0(/* typed node + owner registry */));
        receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(context, /* bound authenticated live evidence */));
        receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM2(/* artifact + semantic evidence */));
        receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM3(/* existing PnP plan */));
        receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM4(/* existing receipts */));
        receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM5(/* tests/build/E2E/reviews */));
        return new IngredientMaturityContribution
        {
            ContributorId = ContributorId,
            ClaimId = context.Identity.ClaimId,
            Lane = Lane,
            IngredientId = context.Identity.IngredientId,
            GateReceipts = receipts
        };
    }
}
```

The recognized lane IDs are frozen in v1:

| Lane | Work item | M0 ownership rule |
| --- | --- | --- |
| `content.text` | canonical ingredient | Content/body Field tuple resolves to this lane |
| `webpart.instance` | canonical ingredient | persisted classic Web Part tuple resolves to this lane |
| `resource.image` | canonical ingredient | typed image/page-referenced file tuple resolves to this lane |
| `page.layout` | canonical ingredient | layout/runtime/page content-type binding tuple resolves to this lane |
| `resource.script` | canonical ingredient | typed script/ScriptLink/JSLink/editor binding tuple resolves to this lane |
| `embed.iframe` | canonical ingredient | typed embed Reference tuple resolves to this lane |
| `dynamic.region` | canonical ingredient | provider-derived runtime region tuple resolves to this lane; provider nodes remain dependencies |
| `behavior.interaction` | runtime-verification assertion | no persisted Kind or materializer ownership |

The catalog permits one contributor per lane and deterministic lane ordering. Lane registration happens in the integration composition root, not by changing the evaluator. Unknown lanes fail closed.

## Frozen shared paths and contributor conformance

The shared boundary remains these eight paths; lanes do not edit them:

- `docs/ccd-182-ingredient-maturity-contract.md`
- `src/lib/PnP.Framework.Test/Migration/Pages/Assessment/IngredientMaturityEvaluatorTests.cs`
- `src/lib/PnP.Framework/Migration/Pages/Assessment/Maturity/IngredientMaturityContracts.cs`
- `src/lib/PnP.Framework/Migration/Pages/Assessment/Maturity/IngredientMaturityContributorCatalog.cs`
- `src/lib/PnP.Framework/Migration/Pages/Assessment/Maturity/IngredientMaturityEvaluator.cs`
- `src/lib/PnP.Framework/Migration/Pages/Assessment/Maturity/IngredientMaturityEvidence.cs`
- `src/lib/PnP.Framework/Migration/Pages/Assessment/Maturity/IngredientMaturityEvidenceValidator.cs`
- `src/lib/PnP.Framework/Migration/Pages/Assessment/Maturity/IngredientMaturityGateCatalog.cs`

Contributors generate receipts through the current common-validator wrappers (`pnp-ingredient-maturity-common-validator`, `v3`), which reuse the PnP domain validators. Unsupported/self-awarded validator IDs or versions fail closed. A lane does not emit an assessment, register new gates, change continuity, or normalize another lane's values.

## External plans and lifecycle receipts (evaluator v3)

The additive, internal `pnp-ingredient-external-evidence/v1` seam is specified in
[`ccd-559-external-maturity-evidence.md`](ccd-559-external-maturity-evidence.md).
An original external plan is not a `PublishingPageMigrationPlan`, and an original
string operation ID is not converted to a synthetic GUID. An independently
admitted annex supplies the typed PnP ingredient graph/action/policy closure that
the external page-only plan does not contain. The original bytes and original
plan digest remain distinct and immutable.

External consumers keep `IngredientPlanEvidence` / `IngredientOperationalEvidence`,
populate their new `External` property, and call the context-bound
`ValidateM3(context, evidence)` / `ValidateM4(context, evidence)` overloads.
`context.ExternalAdmission` comes from the admission consumer, not from the
submitted annex or receipt manifest. The older one-argument overloads still
support Publishing inputs but deliberately fail for external inputs.

The new `Maturity/External/*`, `IngredientMaturityExternalEvidenceValidator.cs`,
shared external conformance tests/resource/test fixture, and the CCD-559 document
are also frozen Architect/Integration-owned paths. Lane owners do not edit them.
Existing M0-M2, M5, continuity and outcome behavior is unchanged.

Permanent shared controls include:

- `AssessmentRejectsRedigestedSemanticTamper` and `AssessmentRejectsRedigestedContinuityBypass`;
- `BoundAssessmentRejectsCoherentRedigestedSubstitution` and `BoundAssessmentRejectsCoherentlyReawardedMissingGate`;
- `EveryContinuousLevelRoundTripsWithRecomputedPromotionGate`;
- `M1RejectsForeignUnpairedOrStaleObservations`, `M1LegacyUnboundOverloadCannotAwardMaturity` and `M1RecordsDifferentValueDigestsWithoutInventingAnOutcomeRule`;
- `RuntimeM0RequiresPredicateAndCanonicalActionDependency`;
- `ContributorUnknownNullDuplicateAndUnsupportedValidatorReceiptsFailClosed`.

These are hermetic contract controls, not evidence of a lane's live M5. PnP Lead non-author review, CTO cross-system review and Independent Verification remain separate gates.

## Compatibility

- Existing graph, package, plan, digest, action, journal, mutation, import/runtime receipt, and Compare wire contracts are reused and unchanged.
- The new types are `internal`; no existing public overload or enum changes.
- Assessment digest uses the existing canonical serializer and SHA-256 implementation. There is no second digest algorithm.
- Source/site/page-family differences remain lane evidence and fixture concerns. They do not enter the evaluator.
- The evaluator consumes existing `PageMigrationOutcome`; it does not replace product outcome or Compare acceptance.
- The assessment JSON schema remains v1; external evidence semantics are explicitly versioned as evaluator v3/common-validator v3. Old evaluator-v1/v2 assessments must be regenerated from their original bound evidence, not relabeled. Publishing contributor source calls remain compatible; saved older summaries are not silently upgraded.
- Existing contributor and M0 call signatures remain source-compatible; runtime-verification M0 additionally requires the optional typed assertion evidence to pass. The old single-argument `ValidateM1(evidence)` overload remains callable but returns failed receipts because it has no independent claim/time binding. Callers must adopt the context-bound overload to attain M1.
- No graph/action/receipt/Compare/owner-registry or tenant change is part of this remediation. Integration admission belongs to a non-author reviewer on the implementation issue's native review stage; implementation tests do not self-approve admission.
