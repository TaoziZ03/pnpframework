# Setup and virtual ASPX platform registry

This directory contains the independent platform authority required by the
CCD-394 `aspx-surface-applicability/v3` contract. It is an input to Assessment
live acquisition and to PnP/Repro/Ingredient/Compare consumers. It is not
generated from Assessment results, CUPCollect paths, or a tenant scan.

## Frozen authority and external profile

- SPO.Core commit: `cee0ed61136e17c742c1cf98c9dea9446f10c564`
- SPO.Core tag: `release/16.0.27606.12000`
- Supported platform family: `SharePointOnline-16`
- Supported build: exactly `16.0.27606.12000`
- Registry revision: `spo-online-16.0.27606.12000-r4`
- Registry canonical hash:
  `c3727376779b7dcd52743edd0ea0a3c773a56c7bf913d4772ae1a4f787144146`
- Profile revision: `spo-online-16.0.27606.12000-profile-r3`
- Profile canonical hash:
  `b1502cf265032930b895b04ee55a8ce98c7e8e5d447f65790f9c9f6f850142a8`
- Registry schema SHA-256:
  `a0f3d3e4659797e987263a8e678cd607dd44fee96812b21c66d4a42724824c1d`

The shipping authority is the `otools/deploy/*.xml` `File` destinations below
`Web Server Extensions\16\TEMPLATE\LAYOUTS` that end in `.aspx`. The bounded
legacy LCID alias authority is
`sts/template/sts/layouts/FilesToRedirect.sts.xms`. The virtual implementation
evidence is `sts/stsom/ApplicationRuntime/spvirtualpathprovider.cs` and
`spvirtualfile.cs` at the same frozen ref.

The registry's own canonical hash is an integrity check, not a compatibility
grant. A consumer must load
`profile/spo-online-16.0.27606.12000.profile.json` as an externally trusted
profile and require its exact revision/hash. The profile pins the authority,
registry, schema hash, exact platform build, CCD-394/CCD-411 decisions, wire
enums, version boundary and fail-closed map. A self-consistently rehashed wider
build range or changed contract remains invalid.

## Outputs

- `authority/spo-online-16.0.27606.12000.authority.json` records every matched
  shipping-manifest row, its source manifest/blob, the redirect-map blob, the
  extraction algorithm and the forbidden scanner inputs.
- `registry/spo-online-16.0.27606.12000.registry.json` is the finite consumer
  volume. Each entry has a stable reference ID, canonical `/_layouts/15/` path,
  aliases, applicability rule, availability, disposition and separate identity
  axes.
- `profile/spo-online-16.0.27606.12000.profile.json` is the external immutable
  compatibility pin.
- `schema/aspx-platform-registry.schema.json` and
  `schema/aspx-platform-registry-profile.schema.json` are closed Draft 2020-12
  schemas for the registry and profile.
- `fixtures/contract-cases.json` carries a real reader-shaped registry envelope
  plus `aspx-acquisition-verdict/v1`, physical, reference and SQLite/store
  bindings. `fixtures/volumes/` contains the actual physical/reference JSON
  bytes and two readable SQLite databases; `fixtures/sql/` records the SQL used
  to construct those stores. `fixtures/f1-f4-negative-receipts.json` records
  deterministic results for all 45 positive/negative contract cases.

The compatibility fixtures are bound to Assessment consumer source ref
`3012555317d5a8ee981b9e103206f3f0680333d8`: `AspxDiscoveryOutputV2`,
`AspxReferenceOutputV1`, `DiscoveryStore.InitializeSchema` and
`AspxReferenceStore.Initialize`. This consumer-side provenance never feeds the
SPO.Core authority extraction or registry entry set, so the registry remains
independent from Assessment output and scanner-observed paths.

No registry entry creates a `FileUniqueId`. Setup-layout application pages are
request references backed by setup artifacts, not content-database `SPFile`
records. A Forms/Views observation may become `LinkedPhysicalGhosted` or
`LinkedPhysicalCustomized` only after a unique physical-inventory join.

If `FilesToRedirect.sts.xms` names a legacy LCID route whose mapped setup file
is absent from the frozen shipping manifests, the generator retains a
`VirtualMappedRequest` entry with `ReferenceUnavailable`. This preserves the
virtual handler identity without claiming that the target artifact exists.

## Closed consumer contract

The five reference `sourceKind` wire strings are fixed:

- `ListFormReference`
- `ListViewReference`
- `WebWelcomePageReference`
- `PlatformRegistryReference`
- `RuntimeRequestReference`

The seven dispositions are fixed:

- `ReferenceOnlyAvailable`
- `ReferenceUnavailable`
- `LinkedPhysicalGhosted`
- `LinkedPhysicalCustomized`
- `VirtualHandler`
- `NonAspx`
- `Unknown`

The CCD-411 three-volume boundary is represented by seven exact version names:

- physical: `aspx-discovery-output/v2`, `aspx-discovery/v2`,
  `aspx-discovery-sqlite/v2`
- reference: `aspx-reference-output/v1`, `aspx-reference/v1`,
  `aspx-reference-sqlite/v1`
- aggregate: `aspx-acquisition-verdict/v1`

The v2 physical output/store shape is closed. Adding a reference row, table,
source kind or enum under a v2 physical contract is invalid; such a mixed
format requires an explicit v3 contract. The reference volume and acquisition
envelope bind run ID, producer refs, scope authority hash, snapshot fence,
platform build, artifact hashes, registry revision/hash and exact store
versions. Missing volumes/envelope or any hash/ref/fence/build/version drift
fails closed to `Unknown`.

The reader additionally requires actual artifact handles. It computes SHA-256
and byte length from both output files, parses their exact output version and
run ID, opens both SQLite stores read-only, requires `PRAGMA integrity_check`
and `foreign_key_check`, computes a semantic
`sqlite-schema-manifest/v2` hash, verifies exact-version typed rows, and verifies
the stored run manifest JSON and manifest hash. The v2 schema projection uses
`table_xinfo`, includes hidden/generated-column state, preserves a quote-aware
normalized `sqlite_schema.sql` definition, and records the explicit partial
index `WHERE` predicate. It is a finite fail-closed projection for the pinned
Assessment producer DDL, not a claim of general SQL semantic equivalence.

The JSON gate validates the complete `AspxDiscoveryOutputV2` and
`AspxReferenceOutputV1` root/row/enum shapes. The store gate rejects physical
rows with reference-only source kinds, validates serialized reference rows,
denominator rows and pagination rows, and requires the reference output
`manifestHash` and coverage verdict to match the same `ReferenceRuns` row.
Invalid JSON root types return `Unknown`; they do not escape as parser or
attribute exceptions.

A self-consistent envelope with invented hashes, stale lengths, an injected
reference table in physical v2, a changed partial-index predicate, a generated
reference column, typed row pollution, a missing reference-v1 table, or a
rewritten/drifted SQLite manifest therefore returns `Unknown`. The frozen store
schema hashes are:

- physical `aspx-discovery-sqlite/v2`:
  `899aa84b3c1786e1e4754d23b943b4609d23dd4b19d34819c1b7e80ea1f4e504`
- reference `aspx-reference-sqlite/v1`:
  `73eabd7a2001f7dbaaafa48db4f0c6489b57305a630b53aa2194d1c8cb9af5ec`

The committed fixture artifacts are intentionally small synthetic data, not a
tenant capture. Their SHA-256/length pairs are bound in
`fixtures/contract-cases.json` and repeated in the v3 receipt. The 54-case
suite includes the CCD-423/CCD-449 changed-predicate, generated-column,
physical/reference row pollution, output/store manifest drift and JSON-array
root counterexamples.

## Assessment adapter conformance

The exact Assessment consumer source is
`3012555317d5a8ee981b9e103206f3f0680333d8`. Its public product shape
`AspxAcquisitionVerdictV1` is not serialized directly as this tool's richer
reader test envelope. `fixtures/contract-cases.json` therefore identifies two
separate shapes and freezes the adapter mapping:

- `AcquisitionRunId` -> reader `runId`;
- volume `Sha256` -> aggregate plus per-volume `artifactHash`;
- volume `Length` -> per-volume `artifactLength`;
- `PlatformBuildRef` -> `platformBuild`;
- `SealedAtUtc` -> `asOfUtc`.

The adapter must also supply the companion store bindings from the same run.
The validator then reads the actual output/store artifacts; metadata mapping by
itself never proves compatibility. The fixture provenance names the exact C#
types and `Initialize*` schema sources, while the 1,211 authority entries remain
derived only from the frozen SPO.Core ref.

Explicit paths use ordinal-ignore-case matching. Versionless
`/_layouts/<path>` aliases normalize to `/_layouts/15/<path>`. The generator
rejects query strings, fragments, backslashes, encoded separators, dot segments
and explicit alias collisions. The reader expands the finite
`/_layouts/{lcid}/<file>` patterns only for names present in the frozen redirect
authority.

## Regeneration and deterministic time

The generator reads the authority commit timestamp as Unix epoch seconds and
renders canonical UTC `YYYY-MM-DDTHH:MM:SSZ`. Windows Git and Linux Git therefore
produce byte-identical authority, registry, profile and bound fixtures for the
same source/ref. Any frozen hash drift requires a new registry/profile revision
and independent review; the generator rejects silent drift.

From a Windows-backed SPO.Core enlistment:

```bash
python3 tools/aspx-platform-registry/generate_registry.py \
  --spocore-repo Q:/spocore/src \
  --git-executable '/mnt/c/Program Files/Git/cmd/git.exe' \
  --independent-review-ref CCD-423
```

Validate the authority, registry, schemas, profile, reader fixtures and receipt:

```bash
python3 tools/aspx-platform-registry/validate_registry.py \
  tools/aspx-platform-registry/registry/spo-online-16.0.27606.12000.registry.json \
  --authority tools/aspx-platform-registry/authority/spo-online-16.0.27606.12000.authority.json \
  --profile tools/aspx-platform-registry/profile/spo-online-16.0.27606.12000.profile.json \
  --schema tools/aspx-platform-registry/schema/aspx-platform-registry.schema.json \
  --profile-schema tools/aspx-platform-registry/schema/aspx-platform-registry-profile.schema.json \
  --fixtures tools/aspx-platform-registry/fixtures/contract-cases.json \
  --receipt-out tools/aspx-platform-registry/fixtures/f1-f4-negative-receipts.json

python3 -m unittest discover \
  -s tools/aspx-platform-registry/tests \
  -p 'test_*.py' -v
```
