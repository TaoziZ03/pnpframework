# External sealed-plan and lifecycle evidence for unified maturity

Decision owner: Architect / CCD-559. Integration admission requires the separate
non-author review; this implementation document is not an implementation PASS.

Base: `3748a6d8c3ad919437f2310a68b81e3029e7b072`,
tree `95bba6fbe5adba397517226ebd1ef4a5b7bf5a44`.
Branch: `codex/ccd559-external-maturity-evidence`.

The initial implementation at `35be03c36222d23884e3f33414a37138be708473`
(tree `1db9631635b11d6498200a650cb71ffcd52c2330`) received
`CHANGES_REQUIRED` in CCD-565. Its dependency-primitive correction is isolated
on `codex/ccd559-lifecycle-closure`, based on that exact commit. The completed
review remains adverse for its original input; the correction requires a new
bounded non-author review, not a relabeled approval.

The next input, `49f147ae9fb4ef260b862b8a607a9870ac240f95`
(tree `51773f5f91b1d5fec219666805218c3ca2df181b`), also received
`CHANGES_REQUIRED` in CCD-571: the absent-list poll could hide a foreign or
missing terminal sample identity, or a denied prefix, behind its final body.
That bounded correction is isolated on `codex/ccd559-list-poll-closure` from
the exact reviewed input. Both completed reviews remain valid for their own
inputs; this correction requires another non-author verdict.

## Decision and compatibility

Use a generic internal artifact envelope and a separately admitted plan-binding
annex, with a schema-specific reader for the existing CCD-109 plan and CCD-143
single-claim lifecycle. Do not translate either into Publishing plan/import
bytes or invent GUID operation IDs.

- Assessment schema stays `pnp-ingredient-maturity-assessment/v1`.
- Evaluator becomes `pnp-ingredient-maturity-evaluator/v3`; common validator
  becomes `pnp-ingredient-maturity-common-validator` / `v3`.
- Add internal `pnp-ingredient-external-evidence/v1`,
  `pnp-ingredient-external-evidence-admission/v1`,
  `pnp-ingredient-external-plan-binding/v1`, and
  `pnp-ingredient-external-receipt-set/v1`.
- Existing Publishing overloads and all public graph, action, plan, digest,
  serializer, receipt, journal, registry and Compare contracts are unchanged.
  Existing contributors compile. Stored evaluator-v1/v2 assessments must be
  regenerated, not relabeled, before admission to v3.
- No lane implementation, target writer, tenant, source tenant, browser or
  Sandbox is modified by this change.

## Why the annex is necessary

The actual `ccd.batch1-repro-plan/v1` contains source page operations, target
mapping and producer lineage. It contains no canonical ingredient graph,
per-ingredient disposition or dependency-release policy. Those facts cannot be
inferred from a page-level `target.repro` operation.

The original input was independently reopened from the CCD-109 package
(concatenated archive SHA-256
`f2d7dd798fd991eb86709a9d788a56c50f13bbd33d3fa1639f54294cdc825203`).
Its producer `ccd-109/scripts/select.mjs` seals
`JSON.stringify(stable(planBody))`: object keys are recursively sorted,
arrays retain order, and the root `planDigest` is omitted.

| Binding | Exact value |
| --- | --- |
| Raw `plan.json` SHA-256 | `6546a2e5c1de5e9c350c9ebca284810858ae3c9a0e6264592f5892d0acd21787` |
| Original admitted `planDigest` | `8f99ef2da6471dacdfd18bd72977662855776fa15f6a41f40642b02c34194f7f` |
| Original plan producer | `2dea1bbcc44bfcfe0fe5c3dd32e32bd28b79cc0c` |
| Historical CCD-255 implementation | `5a9f634da422a92b2493967e32dc74edacac9777` |
| Historical lifecycle input SHA-256 | `31536d8b14eaeb03f41e08e5c603c7704513c91d601838d7ddf47727ef8fa814` |

The raw file hash and its format-specific plan seal are intentionally different.
`ExternalEvidenceJson.BatchPlanDigest` adapts that representation using the
existing `MigrationContractSerializer` and `MigrationDigest`; it does not change
PnP canonicalization or reuse `PublishingPageDigest` on a different type.
Duplicate JSON properties and unsupported numeric representations fail closed.

## Trust and required evidence

The admission consumer independently supplies `context.ExternalAdmission):
raw plan hash, admitted plan digest, original plan producer commit, admitted
annex hash, and (for M4) receipt-set hash, UTC execution window and independently
observed target Site/Web/List/File/item/ETag tuple. It must not copy these pins
out of the submitted evidence. A digest proves byte integrity, not authenticity.

The canonical annex contains:

- full claim/lane/ingredient/kind/subtype/role/predicate identity;
- source context binding and separately typed page URL/path, List/item/File/ETag;
- reopenable source snapshot artifact matching `SourceSnapshotDigest`;
- original plan schema, raw hash, admitted digest and original producer commit;
- original external plan operation ID and original target mapping;
- admitted actual target origin/site/web/list/page paths and target profile;
- exact lifecycle input artifact, recomputed target-mapping and lifecycle
  digests, run/row identity, original native operation/action strings, ownership
  marker and exact lifecycle producer reference;
- the assessed implementation producer, distinct from the historical plan
  producer;
- admission time before the first execution phase;
- existing `CanonicalPageIngredientGraph`, `PageIngredientAction` collection,
  `MigrationActionSignature` and bounded runtime-observation manifest.

Domain owners supply and validate the graph and domain evidence. Shared intake
checks their binding and calls `PageIngredientPlanEvaluator`; it does not
normalize Wiki fields, layouts, scripts, files, Web Parts or runtime projections.
The action signature uses its existing v1 algorithm: original native action ID,
source evidence digest, original admitted plan digest as selection binding,
actual target identity, and the canonical scoped graph/action closure as semantic
digest. This is explicitly a supplemental admission signature, not a claim that
the historical CCD-143 producer emitted a PnP signature.

The receipt-set is only a canonical artifact manifest over original receipts.
It is not another execution journal or a collection of caller-authored PASS flags.
Every artifact is reopened with `MigrationArtifact.ReadAllBytes`, which checks
actual bytes and length (including non-seekable streams). Unknown versions,
missing stores/artifacts, duplicate receipts and corrupt bytes fail closed.

## M3 / M4 behavior

M3 requires all independent bindings, exact external plan seal, the assessed
node/action and PnP dependency/policy validation. Publishing and external
representations are a strict one-of.

M4 additionally binds every root and page operation/action without coercion,
source version, implementation commit, producer digest, mapping, ownership,
target tuple and evidence timeline. It validates native mutation/preflight,
fresh storage, bounded readback attempts, runtime observations, identity-fenced
delete and a later independent post-cleanup absence receipt.

A legal `Drop`, `Delegate` or `Defer` can be plan evidence, but cannot use a
native-write receipt as operational proof. The existing
`PageIngredientExecutionFrontier.IsExecutable` also rejects an unsafe dependent
write. An inaccessible instance yields failed evidence for that instance; it
does not abort independent claims or create a session/IT blocker.

The CCD-143/v2 reader preserves these original phase/state labels:

| Phase | Original before | Original after |
| --- | --- | --- |
| admission | absent | admitted |
| provision | admitted | provisioned |
| native-web-readiness | native-web-created | native-web-ready |
| capability-readiness | native-web-ready | capability-ready |
| native-create | capability-ready | created |
| fresh-readback | created | retained |
| cleanup | retained-or-partial | cleaned |
| post-cleanup | cleaned | absent |

The producer's provision phase includes native Web creation but retains
`stateAfter=provisioned`. Intake verifies its creation/identity evidence and the
following same-Web readiness evidence; it does not rewrite that label.
`retained-or-partial` is also the producer's literal cleanup input label.
All phases remain ordered inside the independently supplied time fence.

### Native dependency primitives are not summary verdicts

`ValidateTargetDependencies` is shared by all six M4 gate paths. It checks the
required Site/Web and capability List before accepting dependent page evidence:

- Native Web creation requires the exact parent/path-derived original operation
  ID, an absent-path `preStatus=404` fence, a provision-phase UTC timestamp,
  CSOM HTTP 200, explicitly null `errorInfo`, request correlation and the
  accepted object/Web/ownership identity. Create acceptance does **not** require
  provisioning to be complete; the subsequent fresh native query proves that.
- The native Web poll reopens its original samples, requires complete bounded
  attempts and ordered phase-bound times, and recomputes identity, ownership
  and readiness from each observation. The terminal summary must equal the
  first successful sample. One successful `OpenWebById` sample is sufficient;
  the file/list/cleanup three-observation rule is not applied to Web readiness.
  Captured non-ready attempts or the producer's exact request-exception shape
  (`AbortError`, `TimeoutError`, `TypeError`) may precede it. Unknown or mixed
  exception/HTTP evidence fails closed. HTTP 401/403,
  non-null CSOM `errorInfo`, foreign identities, contradictory flags and
  observations after a first success fail closed; a later PASS cannot erase
  them. The failure affects this assessed instance, not independent claims.
- The required capability List has exactly one receipt at the bound root path,
  in the bound Web, with the independently supplied List ID and `readStatus=200`.
  An absent-list branch also needs its original successful create and bounded
  List readiness/identity evidence. The original existing-list branch does not
  persist raw samples or request IDs, so the reader does not invent them.
  Template, field/schema, ContentType and layout semantics remain lane-owned.

For an absent List, `ValidateCapabilityListPoll` supplies the List-specific
sample validator to the existing `ValidatePoll` helper. The helper retains
attempt coverage, phase/time bounds, correlation and three-observation HTTP
stability; its File/item/cleanup callers have no List-specific callback.

- The poll must be the original `kind=list-provision`, with bounded elapsed
  time. Every sample must preserve `objectId` and `readyStreak`, which the
  original producer writes. A missing identity field is not a captured null.
  Only its six-field native sample shape is admitted; mixed exception/error
  evidence and unknown fields are not silently discarded.
- Every observed non-null List ID must equal the independently admitted target
  List ID, even on an earlier unsuccessful observation. Explicit null may
  describe a non-ready attempt; it cannot enter a successful stability streak.
  HTTP 401/403 anywhere in the poll is terminal adverse evidence for this
  instance and cannot be erased by later successes.
- The captured streak must reset to zero on a non-ready attempt or increase
  by exactly one on an HTTP 200 observation of the bound identity. The first
  streak of three must be terminal. The producer did not retain every earlier
  response body/root path, so the reader does not invent those bodies or
  infer readiness from HTTP 200 alone: a captured non-ready observation may
  have the right ID but incomplete path evidence.
- Terminal `lastBody.Id`, the same final sample's `objectId`, the library
  receipt and the independent target List ID must agree. The terminal body's
  root path must match the bound target List path in the bound Web.

These are evidence-binding checks, not a List provisioning implementation or
a lane's template/ContentType outcome validator. The original existing-list
branch and native Web first-success protocol are unchanged. Unknown or
contradictory evidence rejects all six M4 gates for this dependent instance,
retains continuous M3 when M0-M3 remain valid, and leaves independent instances
unaffected. No rejected instance receives M5 or an exact migration outcome.

The protocol was checked against the original producer
`ccd-143/scripts/build-lifecycle-expressions.mjs` at
`a0f34fe93473152818ef30842bcb82c2216135d3`, SHA-256
`441ce088a3e2d4f5860412d8ce4d99e4c83f0ddc9e3890e8a843cff56fde19ca`.
This is the producer pinned in the unmodified historical fixture, not this
adapter's implementation commit. No envelope/schema/public API changes or
additional evaluator version are introduced while the v3 candidate is still
awaiting shared admission. Context-bound validation must replay evidence with
the corrected implementation; an old summary cannot substitute for that replay.

## Runtime authority is not duplicated

This intake supports only the original lifecycle producer's required
`PageReachability` and `ErrorShellAbsence` observations. A sealed two-requirement
PnP manifest is checked with `RuntimeVerificationContractValidator`; an
in-memory PnP observation receipt retains the original readback artifact digest.
HTTP denial, semantic denial/error flags, foreign final URL, missing or stale
runtime observations fail. Caller `RuntimeRequired=false`, `CleanupPassed`,
`RetryPassed` and `AdmissionPassed` cannot waive these checks.

This is NOT authored-DOM, screenshot, visual equality, page-family runtime
acceptance or native import acceptance. Those claims remain with their existing
domain validators and the CCD-271 native/runtime authority. No native aggregate,
historical Pending value, acceptance status or Compare report is changed.
Additional runtime requirement kinds fail closed on this adapter.

At this review input, CCD-271 candidate
`b72b4ae3e29e84de4c4bd2892c3f85fc792c70d8` still has the CCD-444
`CHANGES_REQUIRED` verdict and is not in the frozen candidate. This change does
not cherry-pick that unapproved implementation or clone its HTML/image/identity
verifiers. A later native acceptance integration belongs to CCD-271 and the
shared integration owner, not a new lane-private runtime authority.

## Contributor adoption template

The eight owners can use the same additive seam without editing shared files.
For CCD-147, existing `PageLayoutMaturityEvidence.Plan` and `Operational` already
have the correct shared wrapper types; no duplicate lane contract is needed.
The lane changes only its two validator calls to the context-bound overloads
and supplies the `External` artifacts through its authorized lane paths.

```csharp
// The composition/admission consumer obtains these independently of submission.
context.ExternalAdmission = independentlyAdmittedPins;

// Graph/normalization/domain checks and M0-M2 stay in the lane.
var planEvidence = new IngredientPlanEvidence
{
    IngredientId = context.Identity.IngredientId,
    ExpectedSourceSnapshotDigest = context.Source.SourceSnapshotDigest,
    ExpectedPlanDigest = independentlyAdmittedPins.PlanDigest,
    External = new IngredientExternalPlanEvidence
    {
        PlanArtifact = originalExternalPlanArtifact,
        BindingArtifact = independentlyAdmittedAnnexArtifact,
        ArtifactStore = artifactStore
    }
};
var operationalEvidence = new IngredientOperationalEvidence
{
    IngredientId = context.Identity.IngredientId,
    AdmittedPlanDigest = independentlyAdmittedPins.PlanDigest,
    External = new IngredientExternalOperationalEvidence
    {
        PlanEvidence = planEvidence.External,
        ReceiptSetArtifact = independentlyAdmittedReceiptSetArtifact
    }
};
receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM3(context, planEvidence));
receipts.AddRange(IngredientMaturityEvidenceValidator.ValidateM4(context, operationalEvidence));
// Return the usual IngredientMaturityContribution. Do not set attained maturity.
```

The canonical annex and receipt-set are serialized with
`MigrationContractSerializer.SerializeCanonical` and stored through the existing
`IMigrationArtifactStore`. A host must admit their hashes through its real
review/control-plane path. The test fixture's `ResealBinding` / `ResealReceipts`
helpers are adversarial test utilities, not production admission examples.

Historical CCD-255 artifacts remain useful compatibility evidence. They cannot
be relabeled as current-commit M4: the producer commit is checked across input,
annex, context and every receipt. The historical run also had no newly admitted
annex. CCD-147 must obtain real annex admission and a fresh same-commit run after
shared integration review. Its existing `VERIFIED_AT_M2` claim is not changed
by this patch.

## Frozen ownership and tests

Architect/Integration-owned existing changes:

- `Migration/Pages/Assessment/Maturity/IngredientMaturityContracts.cs`
- `Migration/Pages/Assessment/Maturity/IngredientMaturityEvidence.cs`
- `Migration/Pages/Assessment/Maturity/IngredientMaturityEvidenceValidator.cs`
- `docs/ccd-182-ingredient-maturity-contract.md`

Architect/Integration-owned additions:

- `Migration/Pages/Assessment/Maturity/IngredientMaturityExternalEvidenceValidator.cs`
- `Migration/Pages/Assessment/Maturity/External/IngredientExternalEvidenceContracts.cs`
- `Migration/Pages/Assessment/Maturity/External/ExternalEvidenceJson.cs`
- `Migration/Pages/Assessment/Maturity/External/Batch1SealedPlanEvidenceAdapter.cs`
- `Migration/Pages/Assessment/Maturity/External/SharedTargetLifecycleEvidenceAdapter.cs`
- `PnP.Framework.Test/Migration/Pages/Assessment/IngredientExternalEvidenceConformanceTests.cs`
- `PnP.Framework.Test/Migration/Pages/Assessment/IngredientExternalEvidenceTestFixture.cs`
- `PnP.Framework.Test/Migration/Pages/Assessment/IngredientExternalEvidenceFixtures.resx`
- this document.

Library/test paths above are relative to `src/lib/PnP.Framework/` and
`src/lib/`, respectively. No test csproj change is needed: the standard SDK
embeds the new resx. There is no lane/shared-file collision.

The resource embeds the unmodified plan, lifecycle input and eight original
receipts from CCD-255 attachment `ce0b0647-ac20-44f5-954d-630f728af504`;
archive SHA-256
`488dc7007f2b906d6c08c77e323935030718576fa11492b1f4273d0aa2022c11`.
The graph/annex and M0-M2 observations in the fixture are explicitly synthetic.
Tests do not make tenant requests, depend on login, wall-clock time or network,
or claim independent live verification.

Positive controls prove original byte/digest identity, string operation
preservation, artifact reopening, M4 protocol intake, continuity and conditional
outcome independence. Negative controls cover schema, digest, source, claim,
target, producer, operation/action, policy/dependency, raw corruption, absent
admission/cleanup, stale readback, denial and fail-soft instance isolation.
Four execution-frontier/page-admission counterexamples were run RED before the
hardening and retained as permanent rejection tests.

CCD-565's twelve dependency-primitive counterexamples were subsequently run
RED against the original library source and are permanent resealed rejection
cases in `LifecycleDependencyPrimitivesCannotBeReplacedBySummaryPass`. Each
also proves M3 continuity and an independent instance's unaffected M4 control.
Additional controls cover poll metadata, first-success semantics, accepted but
incomplete creation, retained non-ready attempts, contradictory earlier
observations, duplicate List coverage and the absent-list creation branch.
All original 97 common and 90 external cases remain in the focused cohort.

CCD-571's three List counterexamples are permanent resealed controls in
`CapabilityListSamplesCannotBeReplacedByFinalBodyOrLaterSuccess`; they require
all six M4 gates to reject, continuous M3, and an unaffected independent M4
instance. The earlier synthetic absent-list baseline now includes the actual
producer's `kind`, `elapsedMs`, per-sample `objectId` and `readyStreak` fields.
This does not alter the ten historical resource members or remove any of the
237 pre-correction test cases. Additional controls cover earlier identities,
captured null versus missing, HTTP 401/403, terminal body/path, streak ordering,
the first terminal streak, explicit non-ready prefixes, existing-list history
and File/cleanup independence from List-only fields.
The three permanent assertions were first run RED against the unchanged
reviewed library source. The corrected development cohort has 268 passing
cases: the existing 237 plus three CTO rejections, 21 protocol/identity
rejections, five retained-non-ready controls and two compatibility controls.
Development overlay versions are not immutable build provenance.

Final exact-commit build/test receipts, hashes, downloadable patch and the
separate non-author review path are recorded on CCD-559. Tests and this document
do not establish CTO readiness, independent Verification, live M3/M4/M5 or
integration admission.
