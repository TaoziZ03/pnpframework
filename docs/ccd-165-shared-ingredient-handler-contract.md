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
- exact or prefix-based ingredient ID ownership.

`PublishingPageIngredientHandlerCatalog` freezes handlers in `(orderGroup, handlerId)` ordinal order. It rejects duplicate handler IDs and overlapping ownership both within one descriptor and across handlers. There is no mutable global registration API and no filesystem-order discovery.

Use `PublishingPageIngredientEvidenceEnvelope.Create` to create a typed envelope. It uses the shared canonical serializer, sorts evidence references, and seals handler, lane, schema, ingredient key, canonical payload, and references into `evidenceDigest`. Package validation deserializes the declared typed payload, canonicalizes it again, validates the handler schema, and fails closed for unknown handlers, schemas, noncanonical payloads, and digest tampering.

## Version behavior

- Packages without extension evidence remain `pnp-publishing-page-export/v3` and `pnp-publishing-page-migration-package/v3`.
- Extension-bearing packages use export/migration v4 and `pnp-publishing-page-ingredient-projection/v8`.
- Extension capture graphs use `pnp-page-ingredient-graph/v2` and stable string `kindId` values. Known built-ins retain the existing `PageIngredientKind` enum view; custom kinds omit the legacy enum value.
- Legacy graph v1 serialization omits both `kindId` and `ingredientEvidence`.
- Projection dispatch for v2 through v7 stays on the historical built-in path and is protected by six canonical golden digests.
- Existing `PublishingPagePackageValidator` entry points retain their signatures and use the frozen default catalog. New overloads accept an explicit catalog.

## Lane integration rules

1. Create all envelopes with the shared factory.
2. Put envelopes in `catalog.OrderEvidence(...)` order before snapshot sealing.
3. Set the export/migration schema with `PublishingPagePackageContract.ExportSchemaFor(true)` and `MigrationSchemaFor(true)`.
4. Project with `PublishingPageIngredientGraphProjector.Project(snapshot, catalog)`.
5. Add nodes and edges only through `PublishingPageIngredientGraphProjectionContext`; a handler can create only IDs owned by its descriptor.
6. Do not edit the catalog, envelope, graph identity, serializer, digest, or package validators in lane branches. Route shared-contract changes back through the PnP Framework Lead.

## Verification

Focused tests are in `PublishingPageIngredientExtensionContractTests` and cover deterministic order, catalog immutability, duplicate/overlap rejection, typed round-trip, unknown handler/schema rejection, digest tamper rejection after outer snapshot resealing, graph v2 kind identity, duplicate node/disconnected edge rejection, v2-v7 golden projection dispatch, and legacy serialization omission.
