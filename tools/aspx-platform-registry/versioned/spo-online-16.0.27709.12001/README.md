# SharePoint Online ASPX registry release 16.0.27709.12001

This directory is an independent exact-build `aspx-platform-registry/v1`
release for SharePoint Online build `16.0.27709.12001`. The registry was fully
regenerated from the immutable SPO.Core release authority. CUPCollect scan
output, observed runtime paths and the prior `16.0.27709.12000` registry were
not accepted as authority inputs.

## Frozen bindings

- SPO.Core tag: `release/16.0.27709.12001`
- SPO.Core commit/tree: `45ad38aee279ef47f54dc04dfe8eb9fe4cfe5d01` / `a831fc3cab1d714d9ccfac5306ef19aeb00f0362`
- Authority canonical hash: `8a39d64cffaebdec4ee64c9d42418e0569a525f6a533a46adba9aaf71b312691`
- Authority file SHA-256: `b5df9f58fedea0db72136d55d2eb2c6c977c16780b393f2b57aeb8e9487ae94d`
- Registry revision/hash: `spo-online-16.0.27709.12001-r1` / `3138b6d0b1e1af1e170801939d2c1e00793e61cb17f53c6f7d01e61cf3507830`
- Registry file SHA-256: `203eec0e6b1d69242589c2d430ba52c2bd3fe973c80f6659d6a2176757e5a5dc`
- Platform profile revision/hash: `spo-online-16.0.27709.12001-profile-r1` / `e52cbb539f6bf6248830a2dc07da09137c5e991a34008a1a40483e7f7b822ba4`
- Assessment consumer profile revision/hash: `spo-online-16.0.27709.12001-acquisition-consumer-r2` / `3aff8e059b8f129844fbd713ebf36d37048651427734a6f22917ff01c011a444`
- Current Assessment v2 consumer: `pnp/assessment@a33e21eed513e470cfbbb4af4451cb1b37880d0e`
- Current Assessment v2 tree: `07cfec44626f8617e63d34e270a4fc7f3baf761c`
- Assessment remediation evidence: `CCD-845`; predecessor `ccc655b68fc6be9cdf107e6d8fe7d9f56266182a` is an explicit unsupported producer negative case
- PnP.Core source: `1f07296b186698c3cc9ca8580f00af36c0f3f4f5`
- Entry count: `1161`
- Exact build range: `min=max=16.0.27709.12001`
- Independent Verification history: `CCD-835` reproduced the registry but rejected the predecessor consumer; a fresh successor verdict is required before downstream admission

The bounded authority blobs happen to match the previous build, but no range or
cross-build certificate is declared. The exact source ref, commit time,
authority hash, registry hash, profile hashes and schemas were regenerated and
remain fail-closed for every other build.

## Generate and validate

```bash
python3 tools/aspx-platform-registry/generate_registry.py \
  --spocore-repo <isolated-exact-spocore-repository> \
  --git-executable /usr/bin/git \
  --output-root tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001 \
  --release-spec tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/release-spec.json \
  --independent-review-ref CCD-835

python3 tools/aspx-platform-registry/validate_registry.py \
  tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/registry/spo-online-16.0.27709.12001.registry.json \
  --authority tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/authority/spo-online-16.0.27709.12001.authority.json \
  --profile tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/profile/spo-online-16.0.27709.12001.profile.json \
  --schema tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/schema/aspx-platform-registry.schema.json \
  --profile-schema tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/schema/aspx-platform-registry-profile.schema.json \
  --acquisition-profile tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/profile/spo-online-16.0.27709.12001-acquisition-consumer.profile.json \
  --acquisition-profile-schema tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/schema/aspx-acquisition-consumer-profile.schema.json \
  --fixtures tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/fixtures/contract-cases-v2.json \
  --release-spec tools/aspx-platform-registry/versioned/spo-online-16.0.27709.12001/release-spec.json

python3 -m unittest discover \
  -s tools/aspx-platform-registry/tests -p 'test_*.py' -v
```

This run produced `58/58` legacy fixture passes, `23/23` current Assessment v2
fixture passes, `6/6` release-focused unit passes and `38/38` complete tool
suite passes. The successor Assessment author suite separately produced `70/70`
passes and a bounded five-role fixture, while actual CLI-host terminal startup
remains unverified. See `CCD-834-run-report.md` and
`offline-assessment-compatibility.json` for evidence classes and remaining
uncertainty.
