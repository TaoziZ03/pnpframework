# Setup and virtual ASPX platform registry

This directory contains the independent platform authority required by the
CCD-394 `aspx-surface-applicability/v3` contract. It is an input to Assessment
live acquisition and to PnP/Repro/Ingredient/Compare consumers. It is not
generated from Assessment results, CUPCollect paths, or a tenant scan.

## Frozen authority

- SPO.Core commit: `cee0ed61136e17c742c1cf98c9dea9446f10c564`
- SPO.Core tag: `release/16.0.27606.12000`
- Supported platform family: `SharePointOnline-16`
- Supported build range: exactly `16.0.27606.12000`
- Shipping authority: `otools/deploy/*.xml` `File` destinations below
  `Web Server Extensions\16\TEMPLATE\LAYOUTS` that end in `.aspx`
- Legacy LCID alias authority:
  `sts/template/sts/layouts/FilesToRedirect.sts.xms`
- Virtual mapping implementation evidence:
  `sts/stsom/ApplicationRuntime/spvirtualpathprovider.cs`

The exact-build range is deliberate. A newer or older SharePoint Online build
must use a separately generated and reviewed registry revision. Consumers fail
closed to `Unknown` when the build is missing or outside the range.

## Outputs

- `authority/spo-online-16.0.27606.12000.authority.json` records every matched
  shipping-manifest row, its source manifest/blob, the redirect-map blob, the
  extraction algorithm, and the forbidden scanner inputs.
- `registry/spo-online-16.0.27606.12000.registry.json` is the finite consumer
  volume. Each entry has a stable reference ID, canonical `/_layouts/15/` path,
  versionless alias, optional reviewed LCID alias pattern, applicability rule,
  build binding, availability, disposition, and separate identity axes.
- `schema/aspx-platform-registry.schema.json` is the portable schema.
- `fixtures/contract-cases.json` covers positive lookup plus R1-R5, I1-I5 and
  V1-V7 registry-dependent compatibility cases.

No registry entry creates a `FileUniqueId`. Setup-layout application pages are
request references backed by setup artifacts, not content-database `SPFile`
records. A Forms/Views observation may later become `LinkedPhysicalGhosted` or
`LinkedPhysicalCustomized` only after a unique physical-inventory join.

If `FilesToRedirect.sts.xms` names a legacy LCID route whose mapped setup file
is absent from the same frozen shipping manifests, the generator retains a
`VirtualMappedRequest` entry with `ReferenceUnavailable`. This preserves the
virtual handler identity without claiming that the target artifact exists.

## Normalization and fail-closed behavior

Explicit paths use ordinal-ignore-case matching. Versionless
`/_layouts/<path>` aliases normalize to `/_layouts/15/<path>`. The generator
rejects query strings, fragments, backslashes, encoded separators, dot segments
and any explicit alias collision. The consumer can expand the finite
`/_layouts/{lcid}/<file>` patterns only for names present in the frozen
`FilesToRedirect.sts.xms` authority.

These conditions return `Unknown`: unknown/out-of-range build, missing registry
volume, revision/hash drift, alias collision, absent runtime path, unknown wire
enum, or volume/scope/snapshot/build binding mismatch. A physical v2 result can
never become reference-inclusive complete without a compatible reference
volume.

## Regeneration

From a Windows-backed SPO.Core enlistment:

```bash
python3 tools/aspx-platform-registry/generate_registry.py \
  --spocore-repo Q:/spocore/src \
  --git-executable '/mnt/c/Program Files/Git/cmd/git.exe' \
  --independent-review-ref CCD-XXX
```

The output is deterministic for the same ref, tag, review ref and generator
version. The authority and registry hashes are SHA-256 over canonical JSON with
their own top-level hash field omitted.

Validate and run focused tests:

```bash
python3 tools/aspx-platform-registry/validate_registry.py \
  tools/aspx-platform-registry/registry/spo-online-16.0.27606.12000.registry.json \
  --authority tools/aspx-platform-registry/authority/spo-online-16.0.27606.12000.authority.json

python3 -m unittest discover \
  -s tools/aspx-platform-registry/tests \
  -p 'test_*.py' -v
```

## Consumer contract

The registry embeds the CCD-411 companion-volume decision:

- physical: `aspx-discovery-output/v2`
- reference: `aspx-reference-output/v1`
- reference checkpoint: `aspx-reference-sqlite/v1`
- aggregate: `aspx-acquisition-verdict/v1`
- mixed physical/reference shape, if ever required: direct v3

The embedded `consumerCompatibility.dispositions` mapping explicitly defines
Assessment, PnP graph, Repro and Compare behavior for
`ReferenceOnlyAvailable`, `ReferenceUnavailable`, `LinkedPhysicalGhosted`,
`LinkedPhysicalCustomized`, `VirtualHandler`, `NonAspx` and `Unknown`.
