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
