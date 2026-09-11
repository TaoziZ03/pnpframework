# SharePoint Online ASPX registry release 16.0.27709.12000

This directory is an independent, exact-only ASPX platform registry/profile
release for SharePoint build `16.0.27709.12000`. It is generated from the
immutable SPO.Core release authority, never from Assessment output or a tenant
scan. The prior `16.0.27708.12757` release remains byte-immutable.

## Frozen bindings

- SPO.Core ref: `release/16.0.27709.12000`
- SPO.Core commit: `ab4856999051acfe946fab5632b45ce6427287aa`
- SPO.Core tree: `239647a562ddd5263dbcc4d8bc72c57e780c10e8`
- Registry revision: `spo-online-16.0.27709.12000-r1`
- Registry canonical hash: `050e1b18ea16207b3fdbe6c3b2fed57d6453bcac7e2ec063ce91b27727d99c87`
- Registry file SHA-256: `7431631550f9ff1133497285a353f807cb459374dd6a5c439efbf2184429dbe0`
- Registry schema SHA-256: `041b89abbbbe77a696a8161d5cef0f9027d4051b3bf0befdbca23536479f85ad`
- Profile revision: `spo-online-16.0.27709.12000-profile-r1`
- Profile canonical hash: `7406683883f81c47a53171785775615fa7899b4aa565a454e5c2eedf8c676d2f`
- Profile file SHA-256: `2dc95dd6f472d0effdd9529c5836e8d887ec14fc522c9dc175fd089f55299818`
- Profile schema SHA-256: `f74303e088bdf6c4c34024f0f0ee0f9e17f891a5994ae28c6ba36ccde22688af`
- Authority canonical hash: `6b7392ba6bf81d6a62f01bb396032f5eb6b042f1426e001d453b2ce26fbe02c2`
- Authority file SHA-256: `9e7849a985e4addaad19b6fac35b550a2df34b886405b39022a0ed699169670d`
- Entry count: `1161`
- Exact build range: `min=max=16.0.27709.12000`
- Assessment consumer: `pnp/assessment@3012555317d5a8ee981b9e103206f3f0680333d8`
- Independent Verification: `CCD-517`

The bounded authority paths contain the same blobs as the prior build, but the
new release is not a copied authority: its immutable source commit, source ref,
commit time, exact build binding, revision, authority hash, registry hash and
profile hash are independently regenerated and pinned.

## Cross-build equivalence certificate

CCD-542 adds a separate, revocable proof variant that can reuse the admitted
`16.0.27708.12757` registry payload without widening either build binding. The
original exact-generation registry/profile above remain unchanged and valid
without a certificate. The reuse variant is
`registry/spo-online-16.0.27709.12000-equivalence.registry.json` with explicit
`admission.mode=cross-build-equivalence-certificate/v1`; its independent
revision is `spo-online-16.0.27709.12000-equivalence-r1`. The certificate is
`certificates/27708-to-27709.cross-build-equivalence.json`; its canonical hash
is `2ac59d8769f900aa469b46f02105f31727cefe97760cd3a47f37ad327e8c0e50`.
It binds 219 source and 219 target closure members, including nested pathspec
input `otools/deploy/packages/microsoft.sharepoint.warehouse.template_14.xml`,
the exact SPO.Core commits
and trees, generator commit/tree/version, both schema hashes, all source and
target artifact hashes, the actual `release-spec.json` file/canonical hashes,
and payload hash
`805fa210c5c1157d787b8d15411f1bfb9cae2839e846f6ee9b4a637e6a9e2ac8`.

The proof requires identical path sets plus identical blob, byte, parser and
semantic hashes. Added/deleted inputs, rename/case changes, unresolved imports,
parser drift, generator/schema/canonicalization drift, target mismatch,
certificate tamper, revocation, cycles and overlong chains fail closed. The
empty versioned revocation input is `certificate-revocations.json`.

Run proof-first generation (omit `--no-fallback` in normal operation so any
proof uncertainty explicitly invokes full exact generation):

```bash
python3 tools/aspx-platform-registry/cross_build_equivalence.py \
  --spocore-repo /mnt/q/spocore/src \
  --git-executable /usr/bin/git \
  --source-build 16.0.27708.12757 \
  --source-ref 1826a78bef6194afb25edc44b9abf61b7798de0a \
  --source-tag build/main/16.0.27708.12757 \
  --source-root tools/aspx-platform-registry \
  --target-release-spec tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/release-spec.json \
  --output-root tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000 \
  --certificate-out tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/certificates/27708-to-27709.cross-build-equivalence.json \
  --benchmark-out tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/cross-build-equivalence-benchmark.json \
  --generator-repo . \
  --generator-ref 7f1622c55db6d46fb38cefcc5166f6fda366480d
```

The direct-Git benchmark recorded pure proof `7.201968s` and full generation
`3.590000s`. This first canary is therefore `3.611968s` slower; the
result is retained rather than presented as a saving. It establishes the
observable baseline for later builds where extraction cost may differ.

## Validation

Regenerate from the exact SPO.Core commit:

```bash
python3 tools/aspx-platform-registry/generate_registry.py \
  --spocore-repo Q:/spocore/src \
  --git-executable '/mnt/c/Program Files/Git/cmd/git.exe' \
  --output-root tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000 \
  --release-spec tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/release-spec.json \
  --independent-review-ref CCD-517
```

Validate schemas, canonicalization, aliases/collisions, missing volumes,
revision/hash drift, unknown build, incompatible build and the pinned consumer
fixtures:

```bash
python3 tools/aspx-platform-registry/validate_registry.py \
  tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/registry/spo-online-16.0.27709.12000.registry.json \
  --authority tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/authority/spo-online-16.0.27709.12000.authority.json \
  --profile tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/profile/spo-online-16.0.27709.12000.profile.json \
  --schema tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/schema/aspx-platform-registry.schema.json \
  --profile-schema tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/schema/aspx-platform-registry-profile.schema.json \
  --fixtures tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/fixtures/contract-cases.json \
  --receipt-out tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/fixtures/f1-f4-negative-receipts.json \
  --release-spec tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12000/release-spec.json

python3 -m unittest discover \
  -s tools/aspx-platform-registry/tests -p 'test_*.py' -v
```

The v2 offline CLI receipt binds the exact Assessment and PnP Core commits to
the executed `microsoft365-assessment.dll`, `PnP.Scanning.Core.dll`,
`PnP.Core.dll`, apphost and runtime-config SHA-256 values. The isolated
`--no-restore` build used SDK `8.0.425`; the apphost selected .NET and ASP.NET
Core runtime `8.0.31`. The exact runtime accepted `16.0.27709.12000` and
advanced to an intentionally missing local-certificate gate. Both
`16.0.27708.12757` and `unknown` failed with
`registry_or_platform_binding`; no provider, network, page chain or output
volume started in any case. See `offline-assessment-compatibility.json`.
