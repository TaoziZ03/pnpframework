# CCD-143 shared target lifecycle remediation

## Verdict

**PASS — shared target lifecycle / native receipt scope complete；storage exactness 修订为 0/30。**

Run `fc772393-1529-40e9-bcb5-398f8be4aa08` 在唯一授权 target
`https://a830edad9050849cupcollect.sharepoint.com/` 完成 run-owned：

`admission → native create → identity-bound readiness → capability → 30 page native create → fresh retained readback → ownership-guarded cleanup → fresh absence`

Source requests/mutations 均为 0。没有把 `PublishingPageContent`、Web Part persistence
或 source-equal ASPX bytes 冒充 ingredient replay PASS。

## Version and lineage

- Source bundle：CCD-109，immutable ref `2dea1bbcc44bfcfe0fe5c3dd32e32bd28b79cc0c`。
- Source planDigest：`8f99ef2da6471dacdfd18bd72977662855776fa15f6a41f40642b02c34194f7f`。
- Actual target planDigest：`a67db2cc2a513df90197a27b42654ea151df18120108ea162c3f00be1ac3568d`。
- Lifecycle digest：`5665ba566bc431c5ef1f5a0178c4a8403744a6633bd6cb9aae35ec95556c2707`。
- Live producer SHA-256：`ae5b8fc33d598352d6b998156d256a3fc5c9b7eca01bdc96632a104ed6000801`；
  exact execution source 已封存在 `ccd-143/run/source/build-lifecycle-expressions.live.mjs`。
- `ccd-143/scripts/build-lifecycle-expressions.mjs` 是 live execution 后修正的 builder，SHA-256
  `d6e4ca3776b538cd907995fe7dc18875f5279ecc47f58e015bed64b28b00f854`，不再被冒充历史 producer。
- Final verification：`ccd-143/run/verification.json`（v3），SHA-256
  `c04674a4c2791068b99538310448df6164e035f5b4c38d1e62d41fee6995cfbd`。

三个原始 target site 路径在 fresh admission 中已有非本 run 对象，因此按照 Board
exact-level conflict 规则重规划为：

- `/sites/DevCenter-ccd35-fc772393-3`
- `/sites/biatmicrosoft-ccd35-fc772393-3`
- `/sites/catalog-ccd35-fc772393-3`

所有 descendant web/list/page 路径和 page operation ID 都绑定到上述 target planDigest；
未覆盖或清理原有对象。

## Native readiness and capability receipts

6 个 child Web 均通过：

- create phase 使用 CSOM `parent.Webs.Add(WebCreationInformation)`；同一
  `ProcessQuery` 保存 `_ObjectIdentity_`、Web ID、exact path、ownership fingerprint、
  operation ID 和 request correlation。
- fresh phase 使用 `Site.OpenWebById(webId)`；每个 Web 单次 authoritative fresh
  read 均返回同一 Web ID/path、ownership 匹配且 `IsProvisioningComplete=true`。
- 不再使用连续 path REST 200 作为 authoritative readiness。

Capability phase 发现并修复了一个真实 adapter 缺口：SharePoint 可对不存在的 folder
返回 HTTP 200 且 `Exists:false`。旧 predicate 只比较 path，会错误报告 ready。最终实现
要求 `HTTP 200 + Exists:true + exact ServerRelativeUrl`，并在 `Exists!==true` 时执行
run-owned folder create。修复后 `/Pages/Archive` 及其 5 个页面均通过 native identity。

## Page/readback coverage

最终证据：

- native page create identity-bound：30/30；
- fresh retained file/list-item identity：30/30；
- fresh binary presence HTTP 200：30/30；
- target list schema receipt：30/30 page mappings（12/12 distinct target lists PASS）；
- target content type receipt：30/30 page mappings（12/12 distinct target lists PASS）；
- fresh runtime HTTP 200，无 access-denied/server-error shell：30/30；
- invalid lifecycle identity：0。

Raw `fresh-readback.json` 的 v1 aggregator 返回
`fail/FRESH_READBACK_COVERAGE_FAIL`，因为它把 source-equal ASPX bytes 作为 shared
lifecycle 必要条件。原始 receipt 没有被改写，其 SHA-256 为
`7bca5b59a60398d26fce135e1c2ce2d30ae08fd1a9fe8d4810cc537a03ce3638`。

`verification/v3` 按 audit lens 分离独立结果：lifecycle PASS；storage 双 digest exact **0**；
11 页 `expectedSha256=null`，分类为 `expected-unavailable`；19 页双 digest 均存在但不相等，
分类为 `mismatch-deferred`。缺 expected 或 actual digest 均 fail-closed，永久测试覆盖 equal、
missing expected、missing actual、both missing 与 mismatch 五种路径。30 页仍有完整 fresh
identity、binary、schema/content type 和 runtime receipts；这些 lifecycle receipts 不被错误分类为
evidence-invalid，但也不被声明为 ingredient replay 或 storage exact PASS。

## Producer / expression archive correction

CTO 复算确认 v2 包封存了 live run 后修改的 builder。历史 run log 给出可审计时序：

- `2026-09-09T13:24:07Z` 执行 generator，生成 manifest 与 9 个 expressions；
- `2026-09-09T13:41:16Z` 才加入 lifecycle/ingredient aggregation replacement block。

从 post-run builder 去除该后置 8 行，得到 SHA-256
`ae5b8fc33d598352d6b998156d256a3fc5c9b7eca01bdc96632a104ed6000801`，与 manifest、lifecycle
input 和全部 raw receipts 声明精确一致。因此无需 target rerun。9 个实际 execution expressions
现已纳入封存包，并按 manifest 的 SHA-256 与 bytes 逐一 fail-closed 验证。旧 raw
`fresh-readback.json` 继续保持原 `fail/FRESH_READBACK_COVERAGE_FAIL` 和原 digest，不改写。

离线 replay 还以原 run ID、三个 conflict paths 和 suffix ordinal `3` 执行该恢复 source；
重建 manifest 的 run ID、planDigest、lifecycleDigest、producer ref、expression manifest 全部相等，
9/9 expression 文件逐字节 `cmp` 相等。该 replay 只读取 CCD-109 已封存输入，没有 source/target
HTTP 请求或 mutation。

## Cleanup

- page ownership-guarded delete：30/30 PASS；
- site collection delete：3/3 PASS；
- ownership marker delete及 fresh readback：404；
- post-cleanup 三个 run-owned site fresh readback：3/3 HTTP 404。

Cleanup receipt SHA-256：
`687f478791e1aedc03ff70faab5cb02d55bde228abfec864ce6b5f43e8d19a54`；
post-cleanup receipt SHA-256：
`871628b07b1c7ea15dc5dc851854b9d38c491c69129fe9a958405fc36b249900`。

## KB and environment evidence

KB scenario query：
`classic page target lifecycle native create fresh readback cleanup CSOM OpenWebById subweb readiness`。
选中并读取：

- `kb-local/spo/classic-page/reproduction/create-target-subweb-with-recoverable-native-readiness.md`
- `kb-local/private/personal/classic-page-repro/integrate-and-verify-page-runtime.md`
- `kb-local/spo/classic-page/reproduction/representative-5x5-page-repro.md`

执行结果与上述 scenario 的 fail-closed evidence contract 一致；本次修正没有产生新的产品事实，
无需 KB 更正或新增 commit。

初次 WSL host interop 返回 `UtilConnectUnix:533` / `UtilBindVsockAnyPort:307`；已复用
现有根因 IT ticket CCD-207 的已验证 elevated host-interoperable path，同一 Edge target
session 随即成功。该事件发生在 SharePoint request 前，不是 MFA/auth blocker。

## Artifact digests

完整 receipt file digest 与 byte length 记录在 `ccd-143/run/verification.json`：

- admission `f6eca0fb52542bfa44a7d4151298a777c1e2bd1a8af83f8ba14337aa6fac2258`
- provision `ca665ab41f59d1ccfa765277a55ad345938b191eb621204be91f414644658d65`
- readiness `00c67bcc5b1500554dd85776b756aecf99611b4f9e42244035712ad7c66a31d9`
- capability `93fedf824ea42e21a4f704e80d4396751ba8590e4bf9224747ef6f6fb94997cd`
- native-create `089246f33d9b37e7103e39d823a8fa6407d9bdc6ef95429543fb53fa0e31619a`
- fresh-readback `7bca5b59a60398d26fce135e1c2ce2d30ae08fd1a9fe8d4810cc537a03ce3638`
- cleanup `687f478791e1aedc03ff70faab5cb02d55bde228abfec864ce6b5f43e8d19a54`
- post-cleanup `871628b07b1c7ea15dc5dc851854b9d38c491c69129fe9a958405fc36b249900`

## Downstream handoff

CCD-143 只维持 scope-limited shared lifecycle PASS。正式下游 CCD-108/CCD-113 依赖和八 lane、
E2E/M5 门不因本修正解除。ingredient owners 可消费 `verification/v3`、actual expressions 与 raw
receipts，但必须继续独立处理 storage/ingredient fidelity；不得把 lifecycle PASS 当作 Assessment verdict。
