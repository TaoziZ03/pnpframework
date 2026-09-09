# CCD-153 native producer

This executable is the clean-ref entry point for an admitted publishing-page
import and strict page compare. It does not construct an import receipt or
compare report as a sidecar.

`import` deserializes a sealed `PublishingPageMigrationPackage` and
`AdmittedReproExecutionPlan`, connects only to the CUPCollect target, and calls
`PublishingPageMigrationImporter.ImportAdmitted`. The native execution journal,
receipt steps, operation IDs, source-version binding, plan digest, binary hash,
and full implementation ref are written together. Target authentication is
accepted only through the named process environment variable and is never
serialized or printed.

`reconcile` deserializes the native import receipt and the runtime receipt,
recomputes every supplied actual-ingredient artifact through
`IMigrationArtifactStore.OpenRead`, and calls
`PublishingPageCompareReconciler.Reconcile`. That call also invokes
`RuntimeVerificationReceiptValidator`, which reopens the bound HTML, DOM, and
screenshot bytes and validates requirements-manifest, implementation-ref,
browser-context, timeline, HTTP, and no-store evidence.

Build from an immutable commit and bind the binary to that exact ref:

```powershell
dotnet build tools/ccd153-native-producer/ccd153-native-producer.csproj `
  -c Release -p:SourceRevisionId=<full-commit-sha>
```

Run import only after read-only source recheck and target admission have sealed
the package and admitted plan:

```powershell
$env:CCD153_TARGET_COOKIE_HEADER = <provider-managed-cookie-header>
dotnet .\tools\ccd153-native-producer\bin\Release\net10.0\ccd153-native-producer.dll `
  import .\run\ccd35-06.import-request.json
Remove-Item Env:CCD153_TARGET_COOKIE_HEADER
```

Never write the cookie value to the request, ledger, receipt, console, or
artifact bundle. `microsoft*.sharepoint.com` remains read-only; this producer
admits only `a830edad9050849cupcollect.sharepoint.com` as a target authority.

The request schemas are under `schemas/`. Missing, stale, foreign, corrupt,
partial, duplicate, and orphaned native execution lineage fails closed.
