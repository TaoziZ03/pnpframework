# CCD-165 shared ingredient handler contract

## Integration identity

- Base branch: `codex/gate10-family-integration`
- Base commit: `2dea1bbcc44bfcfe0fe5c3dd32e32bd28b79cc0c`
- Delivery branch: `codex/ccd165-shared-ingredient-catalog`
- The exact delivery commit is recorded on Paperclip issue `CCD-165` after the commit is created. A commit cannot contain its own SHA-1/SHA-256 identity.

## Public extension seam

Lane implementations derive from `PublishingPageIngredientHandler<TEvidence>` and provide an immutable `PageIngredientHandlerDescriptor`. The descriptor binds:

- stable `handlerId` and `laneId`;
- supported page families;
- accepted evidence schema versions;
- introduction projection version and deterministic order group;
- exact or prefix-based ingredient ID namespace guards. These guards prevent duplicate handler output; they do not decide primary ownership.

Primary ownership is decided by the frozen `PublishingPageIngredientPrimaryOwnerRegistry`. Its 36 persisted/derived-node entries cover all 22 `PageIngredientKind` values from the CCD-160 `pnp-page-ingredient-primary-owner-registry/v1` contract. Each graph-v2 node must carry `subtype`, `semanticRole`, `sourcePredicateId`, exact source page/list-item identity, source version identity, and `primaryOwnerLane`. Registry construction rejects unbound predicate IDs, and projection rejects both zero-match and multiple-match predicate results. Handler order is not an ownership tiebreaker.

`PublishingPageIngredientHandlerCatalog` freezes handlers in `(orderGroup, handlerId)` ordinal order. It rejects duplicate handler IDs and overlapping ownership both within one descriptor and across handlers. There is no mutable global registration API and no filesystem-order discovery.

Use `PublishingPageIngredientEvidenceEnvelope.Create` to create a typed envelope. It uses the shared canonical serializer, sorts evidence references, and seals handler, lane, schema, ingredient key, canonical payload, and references into `evidenceDigest`. Package validation deserializes the declared typed payload, canonicalizes it again, validates the handler schema, and fails closed for unknown handlers, schemas, noncanonical payloads, and digest tampering.

## Version behavior

- Packages without extension evidence remain `pnp-publishing-page-export/v3` and `pnp-publishing-page-migration-package/v3`.
- Extension-bearing packages use export/migration v4 and `pnp-publishing-page-ingredient-projection/v8`.
- Extension capture graphs use `pnp-page-ingredient-graph/v2` and stable string `kindId` values. Known built-ins retain the existing `PageIngredientKind` enum view; custom kinds omit the legacy enum value.
- Path-derived planning keeps the distinct `pnp-publishing-page-ingredient-projection/path-derived-topology-v2` discriminator even though its graph also uses schema v2. Its clone preserves the complete extension node identity and ownership envelope (`kindId`, `subtype`, `semanticRole`, `sourcePredicateId`, source identity/version, `primaryOwnerLane`, evidence digest/references) without rewriting the captured snapshot or its digest.
- Legacy graph v1 serialization omits both `kindId` and `ingredientEvidence`.
- Projection dispatch for v2 through v7 stays on the historical built-in path and is protected by six canonical golden digests.
- Existing `PublishingPagePackageValidator` entry points retain their signatures and use the frozen default catalog. Explicit-catalog validation uses the three-argument overload so existing calls such as `ValidateExport(package, null)` remain source compatible and unambiguous.

The catalog flows through the normal product chain: family exporters expose `ExportWithIngredientEvidence` and select v4/v8 automatically; family planners and importers accept a catalog constructor dependency while preserving parameterless constructors; graph, action and assessment dispatch run before missing-handler fallback; `PublishingPagePackageFileStore` exposes `Save/Load*WithIngredientHandlers`; report validation uses the same catalog.

## Lane integration rules

1. Create all envelopes with the shared factory.
2. Put envelopes in `catalog.OrderEvidence(...)` order before snapshot sealing.
3. Set the export/migration schema with `PublishingPagePackageContract.ExportSchemaFor(true)` and `MigrationSchemaFor(true)`.
4. Project with `PublishingPageIngredientGraphProjector.Project(snapshot, catalog)`.
5. Add nodes and edges only through `PublishingPageIngredientGraphProjectionContext`; a handler can create only IDs inside its namespace guard and only when the primary-owner registry resolves the complete tuple to that handler lane.
6. Contribute actions and assessments through the collision-checking handler contexts; direct mutation of shared dictionaries is not exposed.
7. Do not edit the catalog, registry, envelope, graph identity, serializer, digest, or package validators in lane branches. Route shared-contract changes back through the PnP Framework Lead.

## Verification

Focused tests are in `PublishingPageIngredientExtensionContractTests` and cover deterministic order, catalog immutability, duplicate/overlap rejection, all-22-kind registry coverage, unbound/zero/multiple owner rejection, typed export and migration round-trip, standard file-store save/load, action and assessment dispatch, tamper rejection after outer digest resealing, source-compatible null artifact-store calls, graph v2 identity, legacy path-derived, extension-only and combined path-derived/extension projection, wrong-discriminator rejection, snapshot/evidence digest preservation, duplicate node/disconnected edge rejection, v2-v7 golden projection dispatch, and legacy serialization omission.

This-run verification used the installed .NET SDK `10.0.400`: the CCD-165 focused contract suite passed `17/17`; the combined focused extension plus `PathDerivedSharedTopologyTests` regression passed `46/46`. The prior clean `fb2fc0e6` verification of `EnterpriseWikiMigrationTests` plus `PublishingProfilesTests` passed `136/136`. Repository warnings were pre-existing package/advisory and analyzer warnings; no test or compilation error remained.
