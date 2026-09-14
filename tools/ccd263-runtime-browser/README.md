# CCD-272 browser runtime evidence adapter

This adapter turns a bounded Edge/CDP observation into the frozen
`pnp-external-page-runtime-evidence/v1` envelope. It consumes, but does not
redefine, the reviewed CCD-271 contracts:

- `pnp-native-page-runtime-binding/v1`
- `pnp-migration-runtime-verification-receipt/v1`
- `pnp-external-page-runtime-evidence/v1`

The adapter consumes both the sealed native binding and its reopenable typed
`AdmittedReproExecutionPlan`. It validates that admission with
`AdmittedReproExecutionPlanValidator`, then creates positive receipts only
through `RuntimeVerificationReceiptFactory.Create`. The request repeats the
expected operation, source-version, admission, import-receipt, and target
identity so missing, stale, or foreign lineage fails closed. The shared
`ClassicWikiRuntimeEvidencePolicy` validates the binding before either result
branch. `NativePageRuntimeBindingValidator.SealExternalEvidence` creates the
content seal, and `ValidateExternalEvidenceAndComputeDigest` reopens all
artifacts both before and after publication.

Publication is create-only. The output must not already exist, alias the
request/binding/admitted-plan input, or be located in the content-addressed
artifact store. Tests hash every read-only input and every CAS object before
the adapter call and require the same set and bytes afterward.

This is the owner-specific CCD-272 adapter, not a general runtime authority.
It additionally pins the reviewed source File/version digest, snapshot,
runtime operation, target File/item/version, CUPCollect host, and origin
evidence-package digest from the claim. A self-consistent foreign binding is
therefore rejected even when a caller reseals it and changes its local
`expected` copy.

The output is strictly one of:

- `runtime-receipt` for a fresh, no-store, exact-URL browser observation whose
  claimed results agree with the shared semantic policy; or
- `terminal-observation` for a bounded per-instance access denial, transport
  failure, or semantic-detector failure.

The adapter never updates a native import receipt and never emits root-level
`runtimeVerificationStatus` or `acceptanceStatus`. Unknown request members are
rejected, so callers cannot smuggle those authority fields into the envelope.

## Build against the reviewed shared contract

The isolated CCD-272 branch predates the CCD-271 files. During review, point
`PnpFrameworkProject` at the exact reviewed CCD-271 worktree/commit. After that
contract is integrated into the shared branch, the default relative project
reference works without an override.

The conformance baseline used for this delivery is CCD-271 commit
`93451dc5188cdf8e495102456d4195fdbb62c6c9`. Key shared-input SHA-256 values:

- `NativePageRuntimeContract.cs`: `63fab887e1b81e5c2c76b2e9eced532e87fec1c3e0f7d8daa401f5312b3a5508`
- `ExternalPageRuntimeEvidence.cs`: `2f0025514c9c44feb5c790ca4c50e5093fae85a5797989a7341e01a9e4688843`
- `NativePageRuntimeBindingValidator.cs`: `30e752eb78a8598dc425938a6d8ac4206a84f9026053fc025c987fd4b227ab1a`
- `ClassicWikiRuntimeEvidencePolicy.cs`: `7412b157a3e420aa5005db7aae2a149c7ad582bfa5b74a137bf57d553779d7a6`

```powershell
$pnp = Resolve-Path ..\ccd271-runtime-native-contract\src\lib\PnP.Framework\PnP.Framework.csproj
dotnet build .\tools\ccd263-runtime-browser\ccd263-runtime-browser.csproj `
  -c Release `
  -p:PnpFrameworkProject=$pnp `
  -p:SourceRevisionId=<full-adapter-commit>
```

## Emit

The request schema is
[`schemas/browser-runtime-evidence-request-v1.schema.json`](schemas/browser-runtime-evidence-request-v1.schema.json).
All paths, including `admittedPlanPath`, are resolved relative to the request
file. The artifact store uses the normal `DirectoryMigrationArtifactStore`
digest layout. The request schema defines the adapter-owned orchestration
envelope; nested binding/admission/runtime types remain owned and strictly
validated by the referenced shared CLR contracts.

```powershell
dotnet .\tools\ccd263-runtime-browser\bin\Release\net9.0\ccd263-runtime-browser.dll `
  emit .\run\ccd272.browser-runtime-request.json
```

No tenant mutation or network I/O is performed. Browser acquisition is expected
to happen in the authorized CUPCollect synthetic run before this deterministic
sealing step. This tool contacts neither source nor target sites.

## Offline conformance

The test executable has no test-framework package dependency. It constructs
synthetic CUPCollect artifacts, loads every permanent fixture under
`fixtures/`, invokes the real shared sealer/validator, and expects either an
exact emission or a named fail-closed rejection.

```powershell
$pnp = Resolve-Path ..\ccd271-runtime-native-contract\src\lib\PnP.Framework\PnP.Framework.csproj
dotnet run --project .\tools\ccd263-runtime-browser\tests\ccd263-runtime-browser.Tests.csproj `
  -c Release `
  -p:PnpFrameworkProject=$pnp `
  -p:TargetFrameworks=net9.0
```

The 40 fixtures cover exact binding, typed-admission/source lineage,
stale/foreign or structurally missing identity, URL redirects,
cache/service-worker reuse, non-fresh contexts, altered HTML/DOM/screenshot
bytes, semantic detector failure, bounded 401, 403, semantic HTTP-200 denial,
and transport terminals, request-ID unavailability, unsupported policy/profile,
forbidden authority fields, retry bounds, and output aliases. Every fixture has
frozen recipe, origin, and generated-input digests in
`fixtures/fixture-provenance-manifest.json`; every successful publication is
reopened through the shared validator.
