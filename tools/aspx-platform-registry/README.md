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
- Registry revision: `spo-online-16.0.27606.12000-r3`
- Registry canonical hash:
  `61180a9ff5aa3b61ce614bd8faecb2713e40780ccdf55d5ddde174d5a54dfd6d`
- Profile revision: `spo-online-16.0.27606.12000-profile-r2`
- Profile canonical hash:
  `1e39ac4999034c9a44b98bb6a8f08364dc1a7113255623740e74fa21ab76a8ae`
- Registry schema SHA-256:
  `f599f5816d8fc4413409a37300a415a8c3add5a2415485afc6702f53a5e5b175`

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
`sqlite-schema-manifest/v1` hash, and verifies the stored run manifest JSON and
manifest hash. A self-consistent envelope with invented hashes, stale lengths,
an injected reference table in physical v2, a missing reference-v1 table, or a
rewritten SQLite manifest therefore returns `Unknown`. The frozen store schema
hashes are:

- physical `aspx-discovery-sqlite/v2`:
  `d346ebbf2a8cbd25c0babae543e2fc6a4dd234a171df83973c6624b1eb65fda2`
- reference `aspx-reference-sqlite/v1`:
  `7470d1e964f202978c9628a0425b3fe1860fd1384d6a2cead35cbd3a0b17e57d`

The committed fixture artifacts are intentionally small synthetic data, not a
tenant capture. Their SHA-256/length pairs are bound in
`fixtures/contract-cases.json` and repeated in the v2 receipt.

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
