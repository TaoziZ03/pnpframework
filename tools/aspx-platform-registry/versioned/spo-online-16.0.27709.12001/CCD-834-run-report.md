# CCD-834｜CUPCollect 16.0.27709.12001 registry remediation 运行报告

## 结论

实现与本轮作者验证为 **PASS**；产品准入仍为 **待独立 Verification**。

已从独立 SPO.Core shipping authority 为 exact build
`16.0.27709.12001` 全量生成 `aspx-platform-registry/v1`、平台 profile、
Assessment current-v2 consumer profile、schemas、legacy/current fixtures 与
adverse receipts。`platformBuildMin` 与 `platformBuildMax` 均为
`16.0.27709.12001`；没有建立 `12000..12001` 范围，也没有发行跨 build
equivalence certificate。

## 输入证据与边界

- 下游：[CCD-746](/CCD/issues/CCD-746)，Assessment commit/tree
  `ccc655b68fc6be9cdf107e6d8fe7d9f56266182a` /
  `ffdd8db6de296516aea8ebbc61f3b12ff6f6c965`。
- PnP.Core commit/tree：`1f07296b186698c3cc9ca8580f00af36c0f3f4f5` /
  `0534cd65f8e942671886ae0f6820580f81031572`。
- CUPCollect fresh build observation：
  `Q:\src\paperclip-ccd746-32cea23e\artifacts\ccd-746\live-32cea23e\input\platform-preflight-series.json`，
  SHA-256 `96e119d993bb92b762f65fe6eb57f5af4e1b82370b3c95ad8988bfb35eadaf55`。
- 原 fail-closed receipt：同目录 `worker-failure.json`，SHA-256
  `35c35532b912457a577bbf6d6ca9a21ff0bf099b97985dc00f43e4da8dc9192e`。
- 32,077 baseline SHA-256：
  `715663f706d0a5e5b6635db81dfc9aa2ed6501ca6f89dda071891952a9d89aec`。
- 禁止 authority 输入：CUPCollect scanner output、Assessment output、tenant
  runtime paths 与历史 `12000` registry payload。
- tenant：未执行 page acquisition、未写 target、未改 source/target tenant。

## 独立 authority

WSL Git 对 SPO.Core ADO remote 返回
`fatal: could not read Username for 'https://onedrive.visualstudio.com': terminal prompts disabled`。
按 canonical IT 根票 [CCD-500](/CCD/issues/CCD-500) 与 KB scenario
`fetch-an-exact-spocore-ref-through-windows-gcm.md`，使用 Windows Git
`2.55.0.windows.3` + 现有 GCM session 做非交互只读解析：

```text
45ad38aee279ef47f54dc04dfe8eb9fe4cfe5d01 refs/heads/build/main/16.0.27709.12001
45ad38aee279ef47f54dc04dfe8eb9fe4cfe5d01 refs/tags/release/16.0.27709.12001
```

exact tag 被取到本轮隔离 bare repo；没有切换或修改
`Q:\spocore\src` worktree/ref。authority commit/tree/time：

- commit：`45ad38aee279ef47f54dc04dfe8eb9fe4cfe5d01`
- tree：`a831fc3cab1d714d9ccfac5306ef19aeb00f0362`
- commit time：`2026-09-12T02:14:11Z`
- bounded authority：17 个 shipping manifests、2,299 deploy rows、104 个
  legacy LCID redirects、2 个 virtual handler sources。

与 `12000` 的 bounded authority path blob diff 为 0。这只说明两个 exact
authority 的受限输入 bytes 相同；本实现仍从 `12001` 自己的 immutable
commit 全量重算，不据此推断 build range 或 freshness。

## 生成物与 hashes

| Artifact | Revision / canonical hash | File SHA-256 |
| --- | --- | --- |
| Authority | `8a39d64cffaebdec4ee64c9d42418e0569a525f6a533a46adba9aaf71b312691` | `b5df9f58fedea0db72136d55d2eb2c6c977c16780b393f2b57aeb8e9487ae94d` |
| Registry | `spo-online-16.0.27709.12001-r1` / `3138b6d0b1e1af1e170801939d2c1e00793e61cb17f53c6f7d01e61cf3507830` | `203eec0e6b1d69242589c2d430ba52c2bd3fe973c80f6659d6a2176757e5a5dc` |
| Platform profile | `spo-online-16.0.27709.12001-profile-r1` / `e52cbb539f6bf6248830a2dc07da09137c5e991a34008a1a40483e7f7b822ba4` | `e7b6532622afd529de93fe585e27faa5c6c7bec32af165a4e42fd7f5350b594a` |
| Assessment consumer profile | `spo-online-16.0.27709.12001-acquisition-consumer-r1` / `de30575284989ded91ff5ea4eb9bf57010fa89968b02c762cacb45d6e435a58d` | `9642c31c0b79cd0d2b9fc4ac6fe156cca5c1c19fffdd681eb76150c08ebe73d1` |
| Registry schema | `6a22625b33961ea20069b541b09c459d21db23db1548da4cce4a5538f9f4c32d` | same |
| Platform profile schema | `5e33167c65e451a1f6f40ae9e1825d32bf105188ad1d146f5c1e05b265b080e3` | same |
| Consumer profile schema | `84bf71701a5cfe8b679ca93bad581d5ff35c81921ccf63120f031e2fcb8cc32c` | same |

Registry 有 1,161 entries。setup application pages 与 virtual mapped requests
保持不同 `surfaceKind`/`handlerOrArtifactType`；physical acquisition volume
不被伪装为 registry entry。alias normalization/collision、missing volume、
revision/hash drift、unknown/incompatible build、runtime-path counterexample 与
current-v2 terminal/pagination adverse paths继续 fail closed。

## 本轮执行

```text
generate_registry.py exact generation: PASS
validate_registry.py legacy fixtures: 58/58 PASS
validate_registry.py Assessment v2 fixtures: 22/22 PASS
test_release_27709_12001.py: 6/6 PASS
unittest discover tools/aspx-platform-registry/tests: 38/38 PASS
```

Current Assessment source读取：

- `AspxAcquisitionCommandHandler.ExecuteAsync` 在任何 auth/provider/network
  work 前 deserialize `AspxPlatformRegistryV1` 并调用
  `registry.Validate(options.PlatformBuild)`。
- `AspxPlatformRegistryV1.Validate` 保持 exact build 与 alias collision
  fail-closed。
- consumer profile 的 `assessment-v2.productRef` exact pin 到
  `pnp/assessment@ccc655b68fc6be9cdf107e6d8fe7d9f56266182a`。

用 CCD-746 exact Release bytes 做了两次 bounded offline runtime 尝试（UNC 与
Windows-local input），均持续超过 60 秒且没有 stdout/stderr/terminal receipt，也没有
official output volume；进程已终止。这两次不计 PASS，也不证明 network stage。
详见 `offline-assessment-compatibility.json`。

## KB

- scenario 查询：`classic page ASPX platform setup virtual`、
  `assessment ASPX acquisition shipping manifest`、
  `product tenant authority exact build`、`source control exact ref`。
- 阅读：
  `aspx-virtual-path-and-publishing-handler-redirection.md`、
  `page-parser-virtual-path.md`、`ghosting-setup-path.md`、
  `aspx-single-container-denominator-needs-enumeration-evidence.md`、
  `escalate-test-tenant-issues-to-sandbox.md`。
- Windows GCM exact-ref scenario 已存在于 `dev.titao/main` commit
  `ab71978aa0e6a598347ef1a320fd9f8745c3bb40`；本轮没有重复 KB 修改。

## 准入与下一步

- 独立 Verification：[CCD-835](/CCD/issues/CCD-835)。Verifier 必须在自己的
  issue 给出 PASS/CONDITIONAL/FAIL；adverse verdict 也是 done。
- 在 CCD-835 PASS 前，CCD-834 不得解锁 CCD-746。
- PASS 后，CCD-746 owner 从新的 official registry path 重新执行 fresh
  preflight → `product_tenant_authority` first → exact run-ID resume →
  terminal/volume/store integrity → 32,077 exact delta。不得复用本次
  `12000` failure path 作为 fresh success。
