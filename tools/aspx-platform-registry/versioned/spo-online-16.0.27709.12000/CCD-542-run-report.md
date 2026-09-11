# CCD-542 跨 build ASPX authority 等价证明 v1

## Verdict

**PASS（作者实现与离线执行）**。`16.0.27708.12757` 到
`16.0.27709.12000` 的真实 proof 已由新机制独立计算并签发；未使用
build 邻接、手工复制 CCD-516 结论或生成结果偶然相同作为充分证据。
独立 Verification verdict 由 CCD-542 的非作者 child 单独给出，作者
PASS 不替代该 gate。

本任务未访问 tenant、浏览器或身份提供者。SPO.Core 仅从
`/mnt/q/spocore/src` 读取两个精确 Git commit 的 object/tree/blob。

## KB workflow

- query/get：`spo.page-family.expand-coverage.v1`
  (`kb-local/spo/scenarios/page-family/expand-page-family-coverage.md`)；
  `spo.contract.validation-layer.v1`
  (`kb/spo/scenarios/contracts/validation-layer-and-sandbox-fidelity-contract.md`)；
  `spo.workflow.select-validation-layer.v1`
  (`kb/spo/scenarios/workflows/select-smallest-sufficient-validation-layer.md`)。
- 采用 exact build/ref、不可用证据 fail closed、最低充分验证层。
- 新验证事实已直接贡献
  `kb-data/spo/architecture/interfaces/endpoints/aspx-virtual-path-and-publishing-handler-redirection.md`：
  `FilesToRedirect.sts.xms` 是带 `//` 注释和 `<file>` 片段的专用格式，
  不是完整 XML；commit `aad2fece0fe186736511ae4a2a389e8a345d6fe8`
  已 push `dev.titao/main`。

## Version/ref bindings

- source build/ref/commit/tree：`16.0.27708.12757` /
  `build/main/16.0.27708.12757` /
  `1826a78bef6194afb25edc44b9abf61b7798de0a` /
  `a9d047d1f57a35855b767384daca789ca1157cc7`。
- target build/ref/commit/tree：`16.0.27709.12000` /
  `release/16.0.27709.12000` /
  `ab4856999051acfe946fab5632b45ce6427287aa` /
  `239647a562ddd5263dbcc4d8bc72c57e780c10e8`。
- generator commit/tree/version：
  `2428e8a9ff0293c12c463628414fc96a3ff76412` /
  `7e8b1e70e3cf06fbf7c1dc9ad9c15be5928f4ab2` /
  `aspx-registry-generator/v2-equivalence`。
- generator manifest hash：
  `756780cbc560c5a4d8e38a2d5c9e1b7951da6f0f94664723733884a98c215675`。
- Assessment consumer commit/tree：
  `77aea495dd1c994ef7cfbfcfc66b7f19cf6805ef` /
  `8ea05c4a22deb9e4b88471a8dfe9675d1e9ec948`。

## Certificate and closure

- schema：`cross-build-equivalence-certificate/v1`；schema file SHA-256
  `99e163cac9299be825589f877606016cdb30a7630bd118e4ba3bf7e2ae1b4c23`。
- certificate canonical hash：
  `a551b9cdb67c27e259ec8fb89457a5976a712061f117c5e97d8f0a06248fe2c1`；
  file SHA-256
  `8eb4fe1b865e8c3490cd2778118e4fbb54c4b4da09015b24ef4c34c6c79577d4`。
- source closure：145 members，hash
  `72dfacf075472eed996ac7dd70fa5230746a2e0cdef1a3aab0e912fd9dc7b4c7`。
- target closure：145 members，hash
  `3f245e7fa467aa602c9cbd8accbb421a5c0cda596621e3b5053c5c9a10a9876b`。
- comparison：`added=[]`、`deleted=[]`、`changed=[]`、
  `renameOrAliasCandidates=[]`、`caseChanges=[]`、`uncertainties=[]`。
- 每个 member 均绑定 path/pathCaseFold、kind、Git blob、SHA-256、
  byteLength、parserId、semanticHash。closure 覆盖全部 direct
  `otools/deploy/*.xml`、legacy redirect map、两个 virtual path provider
  source、解析到的间接 include/import，以及 generator code/schema/
  canonicalization manifest。
- target registry/profile/authority 仍为 exact
  `min=max=16.0.27709.12000` 的独立 revision；没有 build range。
- source/target registry payload hash 均为
  `805fa210c5c1157d787b8d15411f1bfb9cae2839e846f6ee9b4a637e6a9e2ac8`，
  entry count 均为 1,161。

## Old release immutability

proof 前后旧 `16.0.27708.12757` 文件保持：

- authority SHA-256：
  `0cfc45a75ff33c51f0c2b7b1f4effdd922bf71967cb5836b876ea8d6869b7801`；
- registry SHA-256：
  `402de4a697e3c236c2a7ebe46ff90764ec5b6b91442654b3ba13b3ed6556c7a9`；
- profile SHA-256：
  `f453c76729595cf43179ee247314dc66e0aaa9232c7d1f4aed4e12bb32ceec78`。

## Fail-closed and fallback behavior

- proof command 枚举并解析完整 closure；只有成员集合、blob/byte、parser
  和 semantic hashes 全等才调用 payload reuse writer。
- one-byte、added/deleted、rename/case、generator/schema/canonicalization、
  parser behavior、unresolved include/source 均有永久 Python negative test。
- `run_fallback` 永久测试验证 proof 不成立时显式调用同一个
  `generate_registry.py` 做 full exact generation，并写
  `full_exact_generation_fallback` receipt。
- Assessment `AspxRegistryEquivalenceValidator` 重新计算 certificate、closure、
  generator manifest、registry/authority/profile canonical hash 与文件 SHA；
  校验 target build、source admission、schema hashes、revocation、cycle 和
  chain length（上限 8）。unknown/partial/tampered 均返回
  `registry_or_platform_binding`，位于 authentication/provider/network/
  page-chain 之前。

## Execution evidence

### PnP

- `python3 -m unittest discover ...`：25/25 PASS，`30.291903s`。
- certificate Draft 2020-12 schema validation：PASS。
- proof-first real execution：PASS，未执行 full registry extraction；
  `proofSeconds=3.763462`。
- isolated full exact generation：PASS，registry/profile/authority hash 与
  admitted 27709 release 完全一致；`fullGenerationSeconds=2.677372`。
- 本 canary 的 `savedSeconds=-1.086090`，即 proof 比当前小输入 full
  generation 慢。该负节省被保留为真实 baseline，不宣称性能收益。

### Assessment

- task-owned native export：
  `Q:\src\paperclip-ccd542-f68b8175-v2\assessment`，精确 PnP.Core sibling
  `Q:\src\paperclip-ccd542-f68b8175-v2\pnpcore`；SDK `8.0.425`，
  `TargetFrameworks=net8.0`。
- Process build：PASS，0 errors，约 `6.51s`；warning 仅为 export 无 Git
  metadata、既有 obsolete/unused-field warnings。
- 两个永久 `AspxRegistryEquivalenceTests` 方法已编译并通过 package-free
  reflection runner 实际执行：2/2 PASS，`0.329691s`。本机 VSTest adapter
  自动 discovery 返回 no-test，因此保留 package-free runner 与真实 CLI
  smoke 作为本轮执行证据；测试源码仍是标准 xUnit `[Fact]`。
- real valid certificate CLI：通过 registry/certificate gate，随后按预期在
  故意不存在的本地 certificate-store path 失败；未进入 provider/network/
  page-chain，未产生输出。
- real revoked certificate CLI：exit 1，精确包含
  `registry_or_platform_binding:certificate[0]:revoked`；未认证、未产生输出。

## Benchmark summary

| 阶段 | wall-clock |
| --- | ---: |
| pure equivalence proof | 3.763462s |
| full exact generation | 2.677372s |
| PnP permanent tests | 30.291903s |
| Assessment focused test execution | 0.329691s |
| independent review | pending independent Verification child |

## Remaining gate

非作者 Verification 需要在独立 child 上审查 schema、closure completeness、
negative semantics、Assessment pre-provider fail-closed、旧 release
immutability 和 benchmark，并给出 PASS/CONDITIONAL/FAIL。独立 verdict 完成
前，CCD-542 不应提升为最终准入完成。
