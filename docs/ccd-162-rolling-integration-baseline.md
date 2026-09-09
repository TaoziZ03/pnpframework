# CCD-162 rolling ingredient integration baseline

状态：`conditional / offline-integrated`  
系列：`ccd162-rolling/v1`  
权威变更：Board comment `22eebea9-8051-4a96-b245-62a3c051fe2d`

## Contract

CCD-162 维护可前进的滚动 shared ref，不再等待一个全局完整、不可变的 I0。
每次只吸收已有 immutable commit、版本绑定证据和兼容性检查的贡献。一个
tuple、ingredient instance 或 merge path 的冲突，只冻结该冲突及其不安全的
依赖写入；其他 lane 的 discovery、fixture 和 lane-local implementation 继续。

本基线不是 M5、release 或 CUPCollect live verdict。source
`microsoft*.sharepoint.com` 仍只读，唯一 reproduction target 仍是
`https://a830edad9050849cupcollect.sharepoint.com/`。

## Admitted lineage

| 顺序 | 原任务 | 原始 commit | 准入方式 | 当前结论 |
| --- | --- | --- | --- | --- |
| 0 | Linux authority | `2dea1bbcc44bfcfe0fe5c3dd32e32bd28b79cc0c` | 固定 first-parent ancestor | admitted baseline |
| 1 | CCD-165 catalog | `13300f39ab32cc9de25485c8d0894f27210320c6` | 由 CCD-168 lineage 继承 | conditional; known hardening remains |
| 2 | CCD-165 pipeline integration | `fb2fc0e647919444a3cacd4a312fbc7cd4d7fb9c` | 由 CCD-168 parent 继承 | conditional shared dependency |
| 3 | CCD-168 runtime verification v2 | `d473aa2c289c0bc6f595d97bcf4b0ab46dbb4741` | exact parent | admitted offline contract |
| 4 | CCD-166 durable execution binding | `2c1e36f4238b19f74fab1409fb102872a7d9b6dc` | conflict-free cherry-pick；本文件 parent 为 integration commit `e421a755e942ae7ecad0fd67e45f639cd8829615` | admitted offline contract |

CCD-169 是 governance registry/guard artifact，不是 PnP product commit；其 CTO
批准与 digest 作为准入元数据引用，不伪装成产品 lineage commit。

## Not admitted in v1

- CCD-165 `a94ffd6fec393bbc77907d8b7aada82c0268424b`：latest review 仍为
  `FAIL / CHANGES_REQUIRED`，有 typed source predicate、canonical-owner admission
  和 callback envelope alias 三组 P1 boundary 缺口。只暂停该 hardening merge
  path；不阻塞与缺口无关的 provisional lane claim。
- CCD-167：worktree 有已验证的 fixture/validator 文件，但当前 branch HEAD 仍为
  `2dea1bbcc44bfcfe0fe5c3dd32e32bd28b79cc0c`，变更未形成 immutable product
  commit。本轮不从他人 dirty worktree 重建或代签提交；获得 exact commit 后可独立
  评估并追加准入。
- 任一 lane commit：只有 claim identity、source version、approved writable paths、
  empty shared-path intersection 或已裁定 shared change，以及 focused test/receipt
  齐全后才追加。没有 globally complete catalog 前置条件。

## Fresh offline verification

在隔离 worktree `pnpframework-ccd162`、branch
`codex/ccd162-rolling-integration` 上执行：

```text
dotnet test PnP.Framework.Test.csproj \
  --filter "FullyQualifiedName~PublishingPageIngredientExecutionBindingTests|FullyQualifiedName~RuntimeVerificationAssertionContractTests|FullyQualifiedName~PublishingPageIngredientVerificationCatalogTests"

Passed: 18, Failed: 0, Skipped: 0
```

SDK：Windows host `.NET SDK 10.0.400`。构建保留仓库既有 warning，包括
`System.Security.Cryptography.Xml 10.0.0` 的 `NU1903`；未把 warning 描述为本次
新增失败。未登录 tenant、未修改 source/target、未运行 full solution 或 live E2E。

## Rolling admission rule

后续 contribution 以当前 reviewed compatible ref 为 parent 或显式 cherry-pick
输入。每条 ledger 记录 source issue、original commit、integration commit、变更路径、
测试、review revision、conflict/disposition 和 remaining uncertainty。若出现冲突，保留
双方声明与 acquisition method，Ingredient Delivery Manager 提议 owner，PnP Lead 作
最终 instance arbitration，shared-contract/reuse 影响交 Architect 审查。

## Revision 2 — CCD-256 adverse review and shared remediation

本节只追加、不改写上面的首次准入历史。Architect 子任务 CCD-256 对 exact ref
`088a95e23a4c864bffb5a8548ff2bdf9d8cc8107` 给出 `CHANGES_REQUIRED`：
`RuntimeVerificationContractValidator.ValidateReceipt` 只按 v2 assertions 判断 receipt
状态，而 `PublishingPageCompareReconciler.ValidateRuntime` 又只按 legacy required
results 判断同一状态。该 ref 因此不再是 reviewed-compatible ref；只冻结
runtime-v2 mixed-outcome merge path，不停止其他 ingredient instance 或 lane-local 工作。

修订以原 ref 为直接 first parent：

| 来源 | original/integration commit | 路径 | conflict/path analysis | disposition |
| --- | --- | --- | --- | --- |
| CCD-256 P1 remediation | `267ec53518c9e9f34a03c6b94e5378f75133c10d` | shared runtime validator、Publishing Compare reconciler、两组永久 tests | 与既有 owner resolver、serializer/digest、action journal/receipt、registry/guard 无路径交集；复用唯一 runtime receipt/status contract；无 cherry-pick conflict | candidate admitted for exact-ref review |

统一规则由 `RuntimeVerificationContractValidator.ValidateAggregateStatus` 实现：完整覆盖的
required legacy results 与 v2 assertion results 共同导出一个 aggregate status；任一
required evidence failure 都得到 `Failed`，optional legacy failure 不改变 aggregate。
Compare 删除本地第二套 status 逻辑，只消费该共享规则。plan/target/time/state/artifact
binding 仍由既有验证路径执行；缺失 required result 继续 fail closed。

本轮使用 Windows host version-matched `.NET SDK 10.0.400` 与既有 restore：

```text
Permanent combined cohort: 22 passed, 0 failed, 0 skipped
CCD-256 original reviewer probes: 6 passed, 0 failed, 0 skipped
```

永久测试覆盖 required/assertion 四格、v2-only、optional requirement、missing required
result、既有 absent-receipt pending 与 v1 compatibility。首次全局 `dotnet.exe` 解析到
SDK 9.0.318 并产生 `NETSDK1045`；按既有 IT 路径改用宿主已安装的 version-matched
mise dotnet-root，未安装依赖、未修改 TargetFramework 或全局配置。

剩余不确定性：包含本节的最终 exact Git ref 仍需 Architect 新一轮 exact-SHA review；
Independent Verification、live CUPCollect、M5、release 与 customer acceptance 均未声明。
CCD-165 hardening 与 CCD-167 immutable-contribution 条件保持 deferred，不因本修订被
改写为 PASS。

## Revision 3 — v1 no-required aggregate compatibility remediation

本节只追加，不改写 CCD-256 修复及其后续 adverse history。Architect 复审任务
CCD-288 对 exact ref `64009526d1683aeb2156cfcec1f328f45d3dc1ec` 确认原 P1
mixed-outcome 缺陷已修复，同时发现 P2 v1 reader compatibility regression：v1 manifest
没有 required runtime evidence 时，既有 Compare reader 可接受的 `Passed` receipt 被新的
aggregate validator 拒绝。该 review 只暂停受影响的 v1 receipt compatibility/admission path，
不停止独立 ingredient claims、discovery、fixtures 或 lane-local implementation。

修复以 adverse ref 为直接 first parent：

| 来源 | product commit | 路径 | conflict/path analysis | disposition |
| --- | --- | --- | --- | --- |
| CCD-288 P2 remediation | `de8091b89f1cc254de5e9b6c3e86f40664363bbb` | shared runtime validator、Publishing Compare permanent tests | 只修改唯一 aggregate rule 和其 public-Compare tests；未新增 Compare status 算法，未触碰 serializer/digest、action journal/receipt、owner resolver、registry/guard、target client 或 lane paths；无 cherry-pick conflict | candidate admitted for exact-ref re-review |

`RuntimeVerificationContractValidator.ValidateAggregateStatus` 继续作为唯一 aggregate rule。
有 required legacy result 或 v2 assertion 时，aggregate 仍严格由全部 required evidence 导出
`Passed` / `Failed`；没有 required evidence 时，v1 保留既有 `Passed` 或 `NotRequired`
两种 reader representation，二者都投影为 Compare runtime `not-required`。该兼容分支明确
绑定 `ManifestSchemaV1`，不会放宽 v2 assertion aggregation，也不会重写历史 receipt/digest。

永久 public-Compare test 新增 optional-only pass、optional-only fail、optional-only
`NotRequired`、empty `Passed`、empty `NotRequired` 五个边界。Windows host `.NET SDK
10.0.400` / `net10.0` 本轮结果：

```text
Runtime validator + Publishing Compare focused cohort: 17 passed, 0 failed, 0 skipped
Permanent combined cohort + CCD-256 probes + CCD-288 v1 probes: 34 passed, 0 failed, 0 skipped
```

现有 package/advisory、obsolete API、resource 与 analyzer warnings 保留为 warning；未改写为
本修复 PASS 或新增产品失败。最终含本 append-only ledger 的 exact shared ref 记录在
CCD-162 issue document `rolling-integration-baseline` 的新 revision，并再次交 Architect
exact-ref review。Independent Verification、live CUPCollect、M5、release 与 customer
acceptance 仍未声明；本轮没有 source/target tenant 访问或 mutation。
