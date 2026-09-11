# CCD-542 跨 build ASPX authority 等价证明 v1

## Verdict

**CONDITIONAL（CCD-543 FAIL 的 bounded remediation 已完成，等待原非作者路径复验）**。`16.0.27708.12757` 到
`16.0.27709.12000` 的真实 proof 已由新机制独立计算并签发；未使用
build 邻接、手工复制 CCD-516 结论或生成结果偶然相同作为充分证据。
CCD-543 revision `85da3f33-93fa-44ca-b0a4-c36b2a32bcce` 的独立 FAIL
已被消费；本报告记录 F1–F4 修复，但作者执行不替代重新验证 gate。

本任务未访问 tenant、浏览器或身份提供者。SPO.Core 仅从
`/mnt/q/spocore/src` 读取两个精确 Git commit 的 object/tree/blob。
机器可读修复 receipt：`ccd-542-remediation-receipt.json`。

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
  `7f1622c55db6d46fb38cefcc5166f6fda366480d` /
  `e1acf5e7122d94c21ccf4aae03907ac0d5891217` /
  `aspx-registry-generator/v2-equivalence`。
- generator manifest hash：
  `c9fb7e8e4e81bdde26e83f3afd58827ed9f28f2169ffa00c2f8d9b3096c7730f`。
- Assessment consumer commit/tree：
  `3fe4ab726ce41934f04d7f143712daac8cc5dfe2` /
  `f53eb48f462b15664c56d128bf4f4d7d4d06eb0b`。

## Certificate and closure

- schema：`cross-build-equivalence-certificate/v1`；schema file SHA-256
  `e11fccd74d780461b659f8cdd2ad748c0c56cc3e48eab16e860a60e87f7382f7`。
- certificate canonical hash：
  `2ac59d8769f900aa469b46f02105f31727cefe97760cd3a47f37ad327e8c0e50`；
  file SHA-256
  `9736c92b44fd54611dfea17700df17ae0a3877c13921b59763cc33bb15ce641a`。
- source closure：219 members，hash
  `ab61a3a3aca7b00762c69dee2439928322f03a36dc4f0bacf461c738865b428e`。
- target closure：219 members，hash
  `36c90fd75554a8594004abb7e969b34618dca78d9047aabf79e4a0bcc09b399f`。
- comparison：`added=[]`、`deleted=[]`、`changed=[]`、
  `renameOrAliasCandidates=[]`、`caseChanges=[]`、`uncertainties=[]`。
- 每个 member 均绑定 path/pathCaseFold、kind、Git blob、SHA-256、
  byteLength、parserId、semanticHash。closure 通过 generator 与 proof 共用的
  `git grep -- otools/deploy/*.xml` pathspec 枚举器覆盖全部 direct/nested
  输入，包括 `otools/deploy/packages/microsoft.sharepoint.warehouse.template_14.xml`，
  以及 legacy redirect map、两个 virtual path provider
  source、解析到的间接 include/import，以及 generator code/schema/
  canonicalization manifest。
- 原 exact-generation registry/profile/authority 保持不变且不需要证书。
  reuse variant 使用独立 revision
  `spo-online-16.0.27709.12000-equivalence-r1` 与显式
  `admission.mode=cross-build-equivalence-certificate/v1`；仍为 exact
  `min=max=16.0.27709.12000`，没有 build range。
- certificate 绑定实际 `release-spec.json`：file SHA-256
  `ec23663411837fb5fe679dddef290ac3a647f6c5eced95be3a62c4df4eb58a78`，
  canonical hash
  `40685ac88d861b62a2376be18b97fe67be156cfde62d58f4c4f4dd82e9a1a9d5`。
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

- `python3 -m unittest discover ...`：28/28 PASS，`29.074s`。
- certificate Draft 2020-12 schema validation：PASS。
- proof-first real execution：PASS，未执行 full registry extraction；
  `proofSeconds=7.201968`。
- isolated full exact generation：PASS，registry/profile/authority hash 与
  admitted 27709 exact release 完全一致；`fullGenerationSeconds=3.590000`。
- 本 canary 的 `savedSeconds=-3.611968`，即 proof 比当前输入 full
  generation 慢。该负节省被保留为真实 baseline，不宣称性能收益。

### Assessment

- task-owned native export：
  `Q:\src\paperclip-ccd542-f68b8175-v2\assessment`，精确 PnP.Core sibling
  `Q:\src\paperclip-ccd542-f68b8175-v2\pnpcore`；SDK `8.0.424`，
  `TargetFrameworks=net8.0`。
- Core Tests + Process build：PASS，0 errors，`6.49s`；warning 仅为 export 无 Git
  metadata、既有 obsolete/unused-field warnings。
- `AspxRegistryEquivalenceTests` 三个永久 xUnit 方法在 native Q: mirror
  编译并由 VSTest 执行：3/3 PASS，`31ms`；其中 command admission negative
  证明缺 chain 与向 exact registry 注入 certificate 均在 runtime 前拒绝。
- valid/revoked/tampered/source-build/release-spec cases 由同一 native 编译产物的
  validator tests 执行；本轮未访问 tenant、未启动 provider/network/page-chain。

## Benchmark summary

| 阶段 | wall-clock |
| --- | ---: |
| pure equivalence proof | 7.201968s |
| full exact generation | 3.590000s |
| PnP permanent tests | 29.074s |
| Assessment focused test execution | 0.031s |
| independent review | CCD-543 FAIL 已消费；修复后复验 pending |

## Remaining gate

原非作者 Verification 路径需要针对上述 exact commits/artifacts 重新审查
schema、219-member closure completeness、nested mutation、release-spec binding、
Assessment pre-provider fail-closed、旧 release immutability 和 benchmark，给出
新的 PASS/CONDITIONAL/FAIL。复验完成前，CCD-542 不提升为最终准入完成；
GitHub publication 仍独立依赖 CCD-202。
