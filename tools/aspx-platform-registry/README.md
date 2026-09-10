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
- Registry revision: `spo-online-16.0.27606.12000-r2`
- Registry canonical hash:
  `1f38b4ade77695f546afaf69dbb3f8148c4a3714849c6f7dbeae46de4001b680`
- Profile revision: `spo-online-16.0.27606.12000-profile-r1`
- Profile canonical hash:
  `62283f038be997244cda11552e51a4974a62815653dcb9e025356c642de4e79f`
- Registry schema SHA-256:
  `85a2f0664c90d7fe5dfe013bcb938438d6f5e688888fada57bfef28ec2f9cfd2`

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
  bindings. `fixtures/f1-f4-negative-receipts.json` records deterministic
  results for the 34 positive/negative contract cases.

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
