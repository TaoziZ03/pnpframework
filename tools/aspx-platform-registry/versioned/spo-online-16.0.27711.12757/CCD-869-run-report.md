# CCD-869｜CUPCollect 16.0.27711.12757 exact registry authority

## 结论

实现与作者验证：**PASS**。独立 Verification：**PENDING（CCD-873）**。

本轮从 exact SPO.Core shipping authority 生成了 build
`16.0.27711.12757` 的 `aspx-platform-registry/v1`、platform profile、
Assessment acquisition consumer profile/schema、legacy/current fixtures 与
adverse receipts。`platformBuildMin` 与 `platformBuildMax` 都是
`16.0.27711.12757`；没有发行 `12001..12757` range 或 equivalence
certificate。

在 [CCD-873](/CCD/issues/CCD-873) 独立 PASS 前，本产物不得解除
[CCD-746](/CCD/issues/CCD-746) 的 gate。

## 独立 source authority

- Repository：`SPO.Core`
- Tag/branch：`release/16.0.27711.12757` / `build/main/16.0.27711.12757`
- Commit：`0ff1276087a43640144d8072fa347a886473855f`
- Tree：`6c992a8ae11e186bf5fdaba77c082a7c9a893f99`
- Commit time：`2026-09-12T01:49:39Z`
- 获取：Windows Git + existing GCM exact `ls-remote`，随后 fetch 到本轮隔离
  bare repo；Linux Git 只读 immutable objects。
- 限定路径：`otools/deploy/*.xml`、
  `sts/template/sts/layouts/FilesToRedirect.sts.xms`、
  `sts/stsom/ApplicationRuntime/spvirtualpathprovider.cs`、
  `sts/stsom/ApplicationRuntime/spvirtualfile.cs`。
- 统计：17 shipping manifests、2,289 deploy rows、104 legacy LCID
  redirects、2 virtual handler sources。

禁止 authority 输入：CUPCollect scanner output、Assessment output、tenant
runtime enumeration、observed runtime path 与 historical `12001` registry。

### 与 historical 12001 的受限差异

这是差异证据，不是 cross-build equivalence：

- bounded source `git diff`：9 个 `otools/deploy/*.xml` path 发生变化；
- 实际进入 authority 的 changed manifests：`projectserver.xml`、`spo.xml`、
  `spx.xml`、`sts.xml`；
- deploy rows：2,299 → 2,289，added 0，removed 10；
- registry entries：1,161 → 1,156；
- 11 个 virtual `ReferenceUnavailable` dispositions 保持不变。

## 版本化产物

Root：`tools/aspx-platform-registry/versioned/spo-online-16.0.27711.12757`

| Artifact | Revision / canonical SHA-256 | File SHA-256 |
| --- | --- | --- |
| Authority | `8394ef9d5f1e6409131835ab123a5104afa5cedc17b65580831ebc7eee81e9d5` | `a439bae4eb58f1c3d31ce5b69c17fbc8b3d187c90464899a0d34106b79b3cec5` |
| Registry | `spo-online-16.0.27711.12757-r1` / `342b8ed81dfba8404ef70d7c3f876e950f641e861b0d0eab8f30b9cdbfdba5a2` | `b5744ec7b99d7f5d5e79fd608dd0031d663bbb8a94f4ff369c28dfcc255ec112` |
| Platform profile | `spo-online-16.0.27711.12757-profile-r1` / `c4a4892fa269484f0badd4fa24364161e8cd9cb0b8bdeac3eebd7b49fb51f51e` | `4657bacdf0f1b064b2fb31dc1f42083bbea56ffd832bd96136e4d5d7a27db0cb` |
| Assessment consumer profile | `spo-online-16.0.27711.12757-acquisition-consumer-r2` / `7ac773f404477da51162daef2986947792f01feff73f15da33619bbac0e3f14b` | `b58d2af366c4a1a2390ed6623cfaa277958de85c2ca02487bb0f2e941a5fe077` |
| Registry schema | `3ca69e123d826c4273c86b460a33f6d391455449c745e8b9b2455d7db0c9f4e9` | same |
| Platform profile schema | `a0f47b6eafce87ab2a4cd42251394d3ca2cb223b78f863510c592a4d8e76809b` | same |
| Consumer profile schema | `bdd95a89d951df76abd4e8a2fc2e7d09bc7bc91bdbbc5a0ebddfbe582315ad18` | same |

Assessment v2 dispatch exact pin：
`pnp/assessment@a33e21eed513e470cfbbb4af4451cb1b37880d0e`，source tree
`07cfec44626f8617e63d34e270a4fc7f3baf761c`。前一 v2 producer
`ccc655b6…` 的 adverse disposition 为 `PRODUCER_REF_UNSUPPORTED`。

Registry 保持 `SetupLayoutApplicationPage` / `VirtualMappedRequest`、setup
artifact / virtual handler、physical acquisition volumes 三层分离；没有把
physical volume 伪装为 registry entry。

## 本轮执行与结果

```text
exact ls-remote tag/branch equality: PASS
isolated exact SPO.Core fetch/object read: PASS
generate_registry.py exact generation: PASS
validate_registry.py legacy fixtures: 58/58 PASS
validate_registry.py Assessment v2 fixtures: 23/23 PASS
test_release_27711_12757.py: 6/6 PASS
unittest discover tools/aspx-platform-registry/tests: 44/44 PASS
py_compile: PASS
git diff --check: PASS
```

Adverse coverage 包括 unknown/mismatched build、相邻 build、widened range、
revision/hash/schema drift、alias collision、missing/terminal volume、unsupported
producer/provider/store/output/surface contract、pagination-sensitive input 与
artifact/store integrity。所有路径保持 fail closed。

## KB workflow

- scenario-first 查询：`classic page ASPX platform setup virtual`、
  `assessment ASPX acquisition shipping manifest`、
  `product tenant authority exact build`、`source control exact ref`；
- 阅读：
  `kb-local/spo/scenarios/endpoints/expand-endpoint-handler-coverage.md`、
  `kb-local/spo/architecture/interfaces/endpoints/aspx-virtual-path-and-publishing-handler-redirection.md`、
  `kb/spo/classic-page/model/sharepoint/ghosting-setup-path.md`、
  `kb/spo/classic-page/model/aspnet/page-parser-virtual-path.md`；
- 应用：冻结 exact source/build；分离 route/file/factory/runtime；把 ghosted
  setup stream 与 remote-observable reference 分开；运行 schema/semantic/
  negative checks。

本次 build/ref/hash 是 version-bound shipping facts，不是可跨 build 推广的
KB 规则；执行证据与已读 KB 无冲突，因此没有 KB 修改或 commit。

## Read versus executed

Read：CCD-746 trigger evidence、Assessment exact commit/tree 和 runtime-byte
hashes、historical `12001` release、KB、exact SPO.Core Git objects。

Executed：read-only exact source resolution/fetch、registry/profile generation、
offline synthetic fixture generation、schema/semantic/adverse/unit/checksum checks、
PnP branch/commit/push。

Not executed：Assessment source edit、CUPCollect page acquisition、tenant auth、
source/target tenant mutation、fresh `product_tenant_authority` first/resume、
32,077 delta 或 target cleanup。

## Remaining uncertainty 与 next action

作者测试不能替代独立准入。[CCD-873](/CCD/issues/CCD-873) 必须从 exact
source authority 独立复算 hashes、验证 11 unavailable 与 fail-closed adverse
矩阵，并给出 PASS / CONDITIONAL / FAIL。只有 PASS 后 CCD-869 才可完成，
随后 [CCD-746](/CCD/issues/CCD-746) 必须使用新的 run-owned Windows-local
路径，从 fresh preflight 开始 first/resume，并对 sealed 32,077 baseline
`715663f706d0a5e5b6635db81dfc9aa2ed6501ca6f89dda071891952a9d89aec`
完成 fresh downstream verification。

最终 PnP review commit/tree 与 branch publication 由 CCD-869 的 Paperclip
work product 记录；避免在 commit 自身包含不可自洽的 self hash。
