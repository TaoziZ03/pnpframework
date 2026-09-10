# Unified Ingredient maturity contract

Contract: `pnp-ingredient-maturity-assessment/v1`

Evaluator: `pnp-ingredient-maturity-evaluator/v1`
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

`behavior.interaction` uses work-item type `runtime-verification`, has no persisted `PageIngredientKind`, and identifies an assertion bound to canonical ingredient/action IDs in its lane evidence. All other lanes use `canonical-ingredient` and must resolve exactly one primary owner through `PublishingPageIngredientPrimaryOwnerRegistry`.

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
        receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM1(/* authenticated live evidence */));
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

## Compatibility

- Existing graph, package, plan, digest, action, journal, mutation, import/runtime receipt, and Compare wire contracts are reused and unchanged.
- The new types are `internal`; no existing public overload or enum changes.
- Assessment digest uses the existing canonical serializer and SHA-256 implementation. There is no second digest algorithm.
- Source/site/page-family differences remain lane evidence and fixture concerns. They do not enter the evaluator.
- The evaluator consumes existing `PageMigrationOutcome`; it does not replace product outcome or Compare acceptance.

### Additive persisted JSLink identity payload

[CCD-306](ccd-306-jslink-m0-contract.md) defines the opt-in
`pnp-publishing-page-jslink-reference-evidence/v1` payload over the existing v8
ingredient extension seam. It retains the active `reference.jslink` claim ID
and semantic role, binds the exact persisted host/order/source ETag and script
digest, and resolves through the default owner registry. The frozen maturity
interfaces and `ValidateM0` are unchanged. See that supplement for source
requirements, the lane-local handler template and compatibility tests.
