# SharePoint Online ASPX registry release 16.0.27711.12757

This directory is an independent exact-build `aspx-platform-registry/v1`
release for SharePoint Online build `16.0.27711.12757`. It was generated from
the immutable SPO.Core shipping authority for this build. CUPCollect output,
Assessment output, tenant-observed paths, and the historical `16.0.27709.12001`
registry were not authority inputs.

## Frozen bindings

- SPO.Core tag: `release/16.0.27711.12757`
- SPO.Core commit/tree: `0ff1276087a43640144d8072fa347a886473855f` / `6c992a8ae11e186bf5fdaba77c082a7c9a893f99`
- Authority canonical/file SHA-256: `8394ef9d5f1e6409131835ab123a5104afa5cedc17b65580831ebc7eee81e9d5` / `a439bae4eb58f1c3d31ce5b69c17fbc8b3d187c90464899a0d34106b79b3cec5`
- Registry revision/canonical hash: `spo-online-16.0.27711.12757-r1` / `342b8ed81dfba8404ef70d7c3f876e950f641e861b0d0eab8f30b9cdbfdba5a2`
- Registry file SHA-256: `b5744ec7b99d7f5d5e79fd608dd0031d663bbb8a94f4ff369c28dfcc255ec112`
- Platform profile revision/canonical hash: `spo-online-16.0.27711.12757-profile-r1` / `c4a4892fa269484f0badd4fa24364161e8cd9cb0b8bdeac3eebd7b49fb51f51e`
- Platform profile file SHA-256: `4657bacdf0f1b064b2fb31dc1f42083bbea56ffd832bd96136e4d5d7a27db0cb`
- Assessment consumer profile revision/canonical hash: `spo-online-16.0.27711.12757-acquisition-consumer-r2` / `7ac773f404477da51162daef2986947792f01feff73f15da33619bbac0e3f14b`
- Assessment consumer profile file SHA-256: `b58d2af366c4a1a2390ed6623cfaa277958de85c2ca02487bb0f2e941a5fe077`
- Current Assessment v2 consumer: `pnp/assessment@a33e21eed513e470cfbbb4af4451cb1b37880d0e`
- Registry entries: `1,156`; `ReferenceUnavailable`: `11`
- Exact build range: `min=max=16.0.27711.12757`
- Independent Verification gate: `CCD-873`

The bounded shipping authority differs from the historical `12001` build: 10
deploy rows were removed and the registry has five fewer canonical entries.
No range or cross-build equivalence certificate is declared.

## Generate

```bash
python3 tools/aspx-platform-registry/generate_registry.py \
  --spocore-repo <isolated-exact-spocore-repository> \
  --git-executable /usr/bin/git \
  --output-root tools/aspx-platform-registry/versioned/spo-online-16.0.27711.12757 \
  --release-spec tools/aspx-platform-registry/versioned/spo-online-16.0.27711.12757/release-spec.json \
  --independent-review-ref CCD-873
```

The isolated repository must resolve `release/16.0.27711.12757` to
`0ff1276087a43640144d8072fa347a886473855f`. Do not substitute a tenant
observation or a neighboring release.

## Validate

```bash
release=tools/aspx-platform-registry/versioned/spo-online-16.0.27711.12757

python3 tools/aspx-platform-registry/validate_registry.py \
  "$release/registry/spo-online-16.0.27711.12757.registry.json" \
  --authority "$release/authority/spo-online-16.0.27711.12757.authority.json" \
  --profile "$release/profile/spo-online-16.0.27711.12757.profile.json" \
  --schema "$release/schema/aspx-platform-registry.schema.json" \
  --profile-schema "$release/schema/aspx-platform-registry-profile.schema.json" \
  --acquisition-profile "$release/profile/spo-online-16.0.27711.12757-acquisition-consumer.profile.json" \
  --acquisition-profile-schema "$release/schema/aspx-acquisition-consumer-profile.schema.json" \
  --fixtures "$release/fixtures/contract-cases-v2.json" \
  --release-spec "$release/release-spec.json"

python3 -m unittest discover \
  -s tools/aspx-platform-registry/tests -p 'test_*.py' -v

(cd "$release" && sha256sum -c SHA256SUMS)
```

Author verification produced `58/58` legacy fixture passes, `23/23` current
Assessment fixture passes, `6/6` release-focused unit passes, and `44/44` full
tool-suite passes. These results do not replace the independent `CCD-873`
verdict or the downstream fresh tenant first/resume owned by `CCD-746`.
