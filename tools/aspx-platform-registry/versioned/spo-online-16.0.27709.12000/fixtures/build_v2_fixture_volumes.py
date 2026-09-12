#!/usr/bin/env python3
"""Build deterministic synthetic Assessment v2 consumer fixtures for CCD-759."""

from __future__ import annotations

import hashlib
import json
import sqlite3
import sys
from pathlib import Path


FIXTURES = Path(__file__).resolve().parent
REGISTRY_ROOT = FIXTURES.parents[2]
sys.path.insert(0, str(REGISTRY_ROOT))

from generate_registry import discovery_hash  # noqa: E402


RUN_ID = "11111111-2222-3333-4444-555555555555"
PRODUCT_REF = "pnp/assessment@0e54ce48c952a077b2c0f258c62b9f19c8d4a466"
SDK_REF = "1f07296b186698c3cc9ca8580f00af36c0f3f4f5"
BUILD = "16.0.27709.12000"
REGISTRY_REVISION = "spo-online-16.0.27709.12000-r1"
REGISTRY_HASH = "050e1b18ea16207b3fdbe6c3b2fed57d6453bcac7e2ec063ce91b27727d99c87"
SCOPE_HASH = "5" * 64
PERMISSION_HASH = "c" * 64
SNAPSHOT = "fixture-snapshot-v2-001"


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n").encode()


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def write_bytes(name: str, value: bytes) -> None:
    (FIXTURES / "volumes" / name).write_bytes(value)


def build_physical_store() -> tuple[bytes, str]:
    source = (FIXTURES / "volumes" / "physical-store-v2.sqlite").read_bytes()
    connection = sqlite3.connect(":memory:")
    try:
        connection.deserialize(source)
        row = connection.execute(
            "SELECT ManifestJson FROM DiscoveryRuns WHERE RunId=?", (RUN_ID,)
        ).fetchone()
        assert row is not None
        manifest = json.loads(row[0])
        manifest.update(
            productRef=PRODUCT_REF,
            sdkRef=SDK_REF,
            fixtureRevision="ccd-759-assessment-v2-r1",
        )
        manifest_json = json.dumps(manifest, separators=(",", ":"))
        manifest_hash = discovery_hash(manifest_json)
        connection.execute(
            "UPDATE DiscoveryRuns SET ManifestJson=?, ManifestHash=? WHERE RunId=?",
            (manifest_json, manifest_hash, RUN_ID),
        )
        connection.commit()
        return connection.serialize(), manifest_hash
    finally:
        connection.close()


def reference_documents() -> tuple[dict[str, object], dict[str, object], dict[str, object]]:
    endpoint = (
        "https://example.invalid/sites/fixture/_api/web/lists(guid'00000000-0000-0000-0000-000000000112')/"
        "Forms?$select=Id,ServerRelativeUrl,FormType"
    )
    observation = {
        "acquisitionMethod": "SharePoint REST List.Forms system-list disposition",
        "canonicalRequestPath": "/sites/fixture/_catalogs/users/forms",
        "contentOrigin": "system-or-virtual-unknown",
        "disposition": "ReferenceUnavailable",
        "evidenceRefs": ["fixture:http-400", "fixture:system-list-forms"],
        "linkedFileUniqueId": None,
        "linkedPhysicalCanonicalInventoryKey": None,
        "matchedAlias": None,
        "permissionContext": "synthetic read-only fixture",
        "platformBuildRef": BUILD,
        "rawLocator": "/sites/fixture/_catalogs/users/Forms",
        "reasonCode": "system_list_forms_http_400_reference_unknown",
        "recordKind": "AspxReferenceObservation",
        "referenceId": None,
        "referenceObservationId": "observation-system-list-forms",
        "registryHash": REGISTRY_HASH,
        "registryRevision": REGISTRY_REVISION,
        "sourceKind": "ListFormReference",
        "sourceObjectId": "00000000-0000-0000-0000-000000000112:forms-authority",
    }
    pagination = {
        "actualEndpoint": endpoint,
        "actualEndpointHash": sha256(endpoint.encode()),
        "actualFilter": "",
        "actualMethod": "GET",
        "actualSelect": "Id,ServerRelativeUrl,FormType",
        "attemptCount": 1,
        "attemptLimit": 1,
        "authorityRevision": "ccd-759-fixture-authority-v1",
        "collectionScopeKey": "forms:system-users",
        "correlationId": None,
        "errorCode": "-2147467261, System.ArgumentNullException",
        "httpStatusCode": 400,
        "nextTokenHash": None,
        "pageOrdinal": 0,
        "receiptVersion": "aspx-pagination-page-receipt/v2",
        "receivedAtUtc": "2026-09-12T00:00:01Z",
        "requestId": "00000000-0000-0000-0000-000000000400",
        "requestTokenHash": None,
        "responseDigest": sha256(b'{"error":"fixture"}'),
        "responseItemCount": 0,
        "semanticDetectorResult": "error-envelope",
        "terminalFlag": True,
    }
    denominator = {
        "absenceProofKind": None,
        "absenceProofRef": None,
        "acquisitionRunId": RUN_ID,
        "actualEndpoint": endpoint,
        "actualFilter": "",
        "actualMethod": "GET",
        "actualSelect": "Id,ServerRelativeUrl,FormType",
        "aggregateEffect": "incomplete",
        "applicability": "SystemOrVirtualOnly",
        "applicabilityApprovalRef": None,
        "applicabilityPlatformBinding": BUILD,
        "applicabilityReviewRef": "CCD-745@0e54ce48c952a077b2c0f258c62b9f19c8d4a466",
        "applicabilityRuleHash": "d31fe01c6c0b8da3f97783495bf7cd347983291efb8681c93234927eaef86887",
        "applicabilityRuleId": "sharepoint-user-information-list-forms-http-400",
        "applicabilityRuleVersion": "v1",
        "artifactRunId": RUN_ID,
        "asOfUtc": "2026-09-12T00:00:01Z",
        "authorityHash": SCOPE_HASH,
        "authorityKind": "SharePointRestCollectionAuthority",
        "authorityLocator": endpoint,
        "authorityRevision": "ccd-759-fixture-authority-v1",
        "classificationEffect": "system-or-virtual-applicability-explicit;http-400-failed;historical-virtual-unknown",
        "continuationRemaining": False,
        "evidenceRefs": ["fixture:http-400", "fixture:ReferenceUnavailable"],
        "expectedCount": None,
        "expectedCountState": "Unknown",
        "observedCount": 0,
        "paginationChainHash": sha256(json_bytes(pagination)),
        "paginationOutstandingTokenCount": 0,
        "parentScopeKey": "list:system-users",
        "permissionContext": "synthetic read-only fixture",
        "platformBuildRef": BUILD,
        "productRef": PRODUCT_REF,
        "providerVersion": "sharepoint-live-aspx-provider/v2",
        "registryHash": REGISTRY_HASH,
        "registryRevision": REGISTRY_REVISION,
        "requiredAdapter": "AllListFormsAuthority",
        "runtimeCounterexampleState": "NoneObserved",
        "scopeAuthorityHash": SCOPE_HASH,
        "scopeKey": "forms:system-users",
        "sdkRef": SDK_REF,
        "snapshotFence": SNAPSHOT,
        "surfaceContractVersion": "aspx-surface-applicability-denominator/v4",
        "surfaceId": "forms:system-users",
        "terminalOutcome": "Failed",
        "visibilityBoundary": "synthetic fixture",
    }
    return observation, denominator, pagination


def build_reference_store(
    observation: dict[str, object],
    denominator: dict[str, object],
    pagination: dict[str, object],
) -> tuple[bytes, str]:
    manifest = {
        "contractVersion": "aspx-reference/v2",
        "schemaVersion": "aspx-reference-sqlite/v2",
        "productRef": PRODUCT_REF,
        "sdkRef": SDK_REF,
        "scopeAuthorityHash": SCOPE_HASH,
        "permissionBoundaryHash": PERMISSION_HASH,
        "registryRevision": REGISTRY_REVISION,
        "registryHash": REGISTRY_HASH,
        "platformBuildRef": BUILD,
        "snapshotFence": SNAPSHOT,
        "providerVersion": "sharepoint-live-aspx-provider/v2",
        "artifactRunId": RUN_ID,
    }
    manifest_json = json.dumps(manifest, separators=(",", ":"))
    manifest_hash = discovery_hash(manifest_json)
    connection = sqlite3.connect(":memory:")
    try:
        connection.executescript(
            """
            PRAGMA journal_mode=DELETE;
            PRAGMA page_size=4096;
            CREATE TABLE ReferenceRuns (
              RunId TEXT PRIMARY KEY, ManifestJson TEXT NOT NULL, ManifestHash TEXT NOT NULL,
              OutputVersion TEXT NOT NULL, CoverageVerdict TEXT NOT NULL, UpdatedUtc TEXT NOT NULL);
            CREATE TABLE ReferenceObservations (
              RunId TEXT NOT NULL, ObservationId TEXT NOT NULL, Json TEXT NOT NULL,
              PRIMARY KEY (RunId, ObservationId));
            CREATE TABLE ReferenceDenominator (
              RunId TEXT NOT NULL, SurfaceId TEXT NOT NULL, Json TEXT NOT NULL,
              PRIMARY KEY (RunId, SurfaceId));
            CREATE TABLE ReferencePaginationReceipts (
              RunId TEXT NOT NULL, ScopeKey TEXT NOT NULL, PageOrdinal INTEGER NOT NULL, Json TEXT NOT NULL,
              PRIMARY KEY (RunId, ScopeKey, PageOrdinal));
            CREATE TABLE ReferenceGaps (
              RunId TEXT NOT NULL, GapCode TEXT NOT NULL, PRIMARY KEY (RunId, GapCode));
            """
        )
        compact = lambda value: json.dumps(value, separators=(",", ":"))
        connection.execute(
            "INSERT INTO ReferenceRuns VALUES (?, ?, ?, ?, ?, ?)",
            (RUN_ID, manifest_json, manifest_hash, "aspx-reference-output/v2", "Incomplete", "2026-09-12T00:00:02Z"),
        )
        connection.execute(
            "INSERT INTO ReferenceObservations VALUES (?, ?, ?)",
            (RUN_ID, observation["referenceObservationId"], compact(observation)),
        )
        connection.execute(
            "INSERT INTO ReferenceDenominator VALUES (?, ?, ?)",
            (RUN_ID, denominator["surfaceId"], compact(denominator)),
        )
        connection.execute(
            "INSERT INTO ReferencePaginationReceipts VALUES (?, ?, ?, ?)",
            (RUN_ID, pagination["collectionScopeKey"], pagination["pageOrdinal"], compact(pagination)),
        )
        connection.execute(
            "INSERT INTO ReferenceGaps VALUES (?, ?)",
            (RUN_ID, "forms:system-users:expected_count_unknown"),
        )
        connection.commit()
        return connection.serialize(), manifest_hash
    finally:
        connection.close()


def descriptor(path: str, value: bytes, schema_hash: str | None = None) -> dict[str, object]:
    result: dict[str, object] = {"path": path, "sha256": sha256(value), "length": len(value)}
    if schema_hash is not None:
        result["schemaManifestHash"] = schema_hash
    return result


def main() -> None:
    physical_output = (FIXTURES / "volumes" / "physical-output-v2.json").read_bytes()
    physical_store, physical_manifest_hash = build_physical_store()
    write_bytes("physical-store-assessment-v2.sqlite", physical_store)

    observation, denominator, pagination = reference_documents()
    reference_store, reference_manifest_hash = build_reference_store(observation, denominator, pagination)
    write_bytes("reference-store-v2.sqlite", reference_store)
    reference_output_document = {
        "acquisitionRunId": RUN_ID,
        "coverageVerdict": "Incomplete",
        "denominator": [denominator],
        "gapCodes": ["forms:system-users:expected_count_unknown"],
        "manifestHash": reference_manifest_hash,
        "outputVersion": "aspx-reference-output/v2",
        "paginationReceipts": [pagination],
        "references": [observation],
    }
    reference_output = json_bytes(reference_output_document)
    write_bytes("reference-output-v2.json", reference_output)

    aggregate_document = {
        "acquisitionRunId": RUN_ID,
        "aggregateVerdict": "Unknown",
        "gapCodes": ["forms:system-users:expected_count_unknown"],
        "outputVersion": "aspx-acquisition-verdict/v2",
        "physicalVolume": {
            "length": len(physical_output),
            "outputVersion": "aspx-discovery-output/v2",
            "productRef": PRODUCT_REF,
            "runId": RUN_ID,
            "scopeAuthorityHash": SCOPE_HASH,
            "sha256": sha256(physical_output),
            "snapshotFence": SNAPSHOT,
        },
        "platformBuildRef": BUILD,
        "productRef": PRODUCT_REF,
        "referenceVolume": {
            "length": len(reference_output),
            "outputVersion": "aspx-reference-output/v2",
            "productRef": PRODUCT_REF,
            "runId": RUN_ID,
            "scopeAuthorityHash": SCOPE_HASH,
            "sha256": sha256(reference_output),
            "snapshotFence": SNAPSHOT,
        },
        "registryHash": REGISTRY_HASH,
        "registryRevision": REGISTRY_REVISION,
        "sdkRef": SDK_REF,
        "sealedAtUtc": "2026-09-12T00:00:03Z",
        "surfaceContractVersion": "aspx-surface-applicability-denominator/v4",
    }
    aggregate_output = json_bytes(aggregate_document)
    write_bytes("aggregate-output-v2.json", aggregate_output)

    role_values = {
        "aggregate-output": ("aspx-acquisition-verdict/v2", "aggregate-output-v2.json", aggregate_output),
        "physical-database": ("aspx-discovery-sqlite/v2", "physical-store-assessment-v2.sqlite", physical_store),
        "physical-output": ("aspx-discovery-output/v2", "physical-output-v2.json", physical_output),
        "reference-database": ("aspx-reference-sqlite/v2", "reference-store-v2.sqlite", reference_store),
        "reference-output": ("aspx-reference-output/v2", "reference-output-v2.json", reference_output),
    }
    terminal_document = {
        "aggregateVerdict": "Unknown",
        "artifactRunId": RUN_ID,
        "completedAtUtc": "2026-09-12T00:00:04Z",
        "completionState": "Succeeded",
        "errorCode": None,
        "errorDigest": None,
        "executable": {"fixture": "pnp/assessment@0e54ce48"},
        "exitCode": 0,
        "productRef": PRODUCT_REF,
        "receiptVersion": "aspx-acquisition-terminal-receipt/v1",
        "sdkRef": SDK_REF,
        "snapshotFence": SNAPSHOT,
        "volumes": [
            {
                "fileName": file_name,
                "length": len(value),
                "outputVersion": version,
                "role": role,
                "sha256": sha256(value),
            }
            for role, (version, file_name, value) in sorted(role_values.items())
        ],
    }
    terminal = json_bytes(terminal_document)
    write_bytes("terminal-receipt-v1.json", terminal)

    artifact_bindings = {
        "aggregateOutput": descriptor("volumes/aggregate-output-v2.json", aggregate_output),
        "physicalOutput": descriptor("volumes/physical-output-v2.json", physical_output),
        "physicalStore": descriptor(
            "volumes/physical-store-assessment-v2.sqlite",
            physical_store,
            "899aa84b3c1786e1e4754d23b943b4609d23dd4b19d34819c1b7e80ea1f4e504",
        ),
        "referenceOutput": descriptor("volumes/reference-output-v2.json", reference_output),
        "referenceStore": descriptor(
            "volumes/reference-store-v2.sqlite",
            reference_store,
            "73eabd7a2001f7dbaaafa48db4f0c6489b57305a630b53aa2194d1c8cb9af5ec",
        ),
        "terminalReceipt": descriptor("volumes/terminal-receipt-v1.json", terminal),
    }
    reader_template = {
        "acquisitionEnvelope": {
            "aggregateVerdict": "Unknown",
            "asOfUtc": "2026-09-12T00:00:03Z",
            "outputVersion": "aspx-acquisition-verdict/v2",
            "physicalArtifactHash": sha256(physical_output),
            "physicalVolume": {
                "artifactHash": sha256(physical_output),
                "artifactLength": len(physical_output),
                "contractVersion": "aspx-discovery/v2",
                "outputVersion": "aspx-discovery-output/v2",
                "platformBuild": BUILD,
                "productRef": PRODUCT_REF,
                "runId": RUN_ID,
                "scopeAuthorityHash": SCOPE_HASH,
                "snapshotFence": SNAPSHOT,
                "store": {
                    "manifestHash": physical_manifest_hash,
                    "schemaVersion": "aspx-discovery-sqlite/v2",
                },
            },
            "platformBuild": BUILD,
            "productRef": PRODUCT_REF,
            "referenceArtifactHash": sha256(reference_output),
            "referenceVolume": {
                "artifactHash": sha256(reference_output),
                "artifactLength": len(reference_output),
                "contractVersion": "aspx-reference/v2",
                "dispositions": [
                    "ReferenceOnlyAvailable",
                    "ReferenceUnavailable",
                    "LinkedPhysicalGhosted",
                    "LinkedPhysicalCustomized",
                    "VirtualHandler",
                    "NonAspx",
                    "Unknown",
                ],
                "outputVersion": "aspx-reference-output/v2",
                "platformBuild": BUILD,
                "productRef": PRODUCT_REF,
                "recordKind": "AspxReferenceObservation",
                "registryHash": REGISTRY_HASH,
                "registryRevision": REGISTRY_REVISION,
                "runId": RUN_ID,
                "scopeAuthorityHash": SCOPE_HASH,
                "snapshotFence": SNAPSHOT,
                "sourceKinds": [
                    "ListFormReference",
                    "ListViewReference",
                    "WebWelcomePageReference",
                    "PlatformRegistryReference",
                    "RuntimeRequestReference",
                ],
                "store": {
                    "manifestHash": reference_manifest_hash,
                    "schemaVersion": "aspx-reference-sqlite/v2",
                },
            },
            "registryHash": REGISTRY_HASH,
            "registryRevision": REGISTRY_REVISION,
            "runId": RUN_ID,
            "scopeAuthorityHash": SCOPE_HASH,
            "snapshotFence": SNAPSHOT,
        },
        "platformBuild": BUILD,
        "registryEnvelope": {
            "authorityArtifactHash": "6b7392ba6bf81d6a62f01bb396032f5eb6b042f1426e001d453b2ce26fbe02c2",
            "authoritySourceRef": "ab4856999051acfe946fab5632b45ce6427287aa",
            "platformBuild": BUILD,
            "platformFamily": "SharePointOnline-16",
            "profileHash": "7406683883f81c47a53171785775615fa7899b4aa565a454e5c2eedf8c676d2f",
            "profileRevision": "spo-online-16.0.27709.12000-profile-r1",
            "profileSchemaVersion": "aspx-platform-registry-profile/v2",
            "registryHash": REGISTRY_HASH,
            "registryRevision": REGISTRY_REVISION,
            "registrySchemaHash": "041b89abbbbe77a696a8161d5cef0f9027d4051b3bf0befdbca23536479f85ad",
            "registrySchemaVersion": "aspx-platform-registry/v1",
        },
        "runtimePath": "/_layouts/15/accessdenied.aspx",
    }
    negative = [
        ("V2-AGGREGATE-V1-AS-V2", "aggregateOutputV1", "UNSUPPORTED_OUTPUT_VERSION", {}),
        ("V2-AGGREGATE-TERMINAL-COMMON-DRIFT", "aggregateTerminalVerdictDrift", "AGGREGATE_CONTENT_MISMATCH", {"operations": [{"op": "replace", "path": "/acquisitionEnvelope/aggregateVerdict", "value": "Incomplete"}], "rebindTerminalVolumes": ["aggregate-output"]}),
        ("V2-AGGREGATE-SEMANTIC-FALSE-COMPLETE", "aggregateTerminalFalseComplete", "AGGREGATE_CONTENT_MISMATCH", {"operations": [{"op": "replace", "path": "/acquisitionEnvelope/aggregateVerdict", "value": "CompleteAuthorizedSurface"}], "rebindTerminalVolumes": ["aggregate-output"]}),
        ("V2-AGGREGATE-OUTSTANDING-PAGINATION-FALSE-COMPLETE", "aggregateOutstandingPaginationFalseComplete", "AGGREGATE_CONTENT_MISMATCH", {"operations": [{"op": "replace", "path": "/acquisitionEnvelope/aggregateVerdict", "value": "CompleteAuthorizedSurface"}], "rebindActualArtifact": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
        ("V2-AGGREGATE-VERDICT-UNSUPPORTED", "aggregateTerminalUnsupportedVerdict", "AGGREGATE_CONTENT_MISMATCH", {"operations": [{"op": "replace", "path": "/acquisitionEnvelope/aggregateVerdict", "value": "EqualitySuccess"}], "rebindTerminalVolumes": ["aggregate-output"]}),
        ("V2-PRODUCER-V1-AS-V2", None, "PRODUCER_REF_UNSUPPORTED", {"operations": [{"op": "replace", "path": "/acquisitionEnvelope/productRef", "value": "pnp/assessment@3012555317d5a8ee981b9e103206f3f0680333d8"}]}),
        ("V2-STORE-V1-AS-V2", None, "UNSUPPORTED_STORE_VERSION", {"operations": [{"op": "replace", "path": "/acquisitionEnvelope/referenceVolume/store/schemaVersion", "value": "aspx-reference-sqlite/v1"}]}),
        ("V2-PROVIDER-V1-AS-V2", "referenceProviderV1", "PROVIDER_VERSION_UNSUPPORTED", {"rebindActualArtifact": ["reference"], "rebindStoreManifestHash": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
        ("V2-SURFACE-V3-AS-V4", "referenceSurfaceV3", "SURFACE_CONTRACT_VERSION_UNSUPPORTED", {"rebindActualArtifact": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
        ("V2-PAGINATION-V1-AS-V2", "paginationReceiptV1", "PAGINATION_RECEIPT_VERSION_UNSUPPORTED", {"rebindActualArtifact": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
        ("V2-TERMINAL-ROLE-VERSION", "terminalRoleVersionSwap", "TERMINAL_ROLE_VERSION_MISMATCH", {}),
        ("V2-TERMINAL-MISSING", "terminalMissingVolume", "TERMINAL_VOLUME_MISSING", {}),
        ("V2-TERMINAL-DUPLICATE", "terminalDuplicateVolume", "TERMINAL_VOLUME_DUPLICATE", {}),
        ("V2-TERMINAL-HASH", "terminalHashDrift", "TERMINAL_VOLUME_HASH_MISMATCH", {}),
        ("V2-TERMINAL-LENGTH", "terminalLengthDrift", "TERMINAL_VOLUME_LENGTH_MISMATCH", {}),
        ("V2-RAW-SKIPTOKEN", "paginationRawSkiptoken", "PAGINATION_SENSITIVE_INPUT_REJECTED", {"rebindActualArtifact": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
        ("V2-RAW-CONTINUATION", "paginationRawContinuationToken", "PAGINATION_SENSITIVE_INPUT_REJECTED", {"rebindActualArtifact": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
        ("V2-AUTHORIZATION", "paginationAuthorization", "PAGINATION_SENSITIVE_INPUT_REJECTED", {"rebindActualArtifact": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
        ("V2-COOKIE", "paginationCookie", "PAGINATION_SENSITIVE_INPUT_REJECTED", {"rebindActualArtifact": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
        ("V2-SET-COOKIE", "paginationSetCookie", "PAGINATION_SENSITIVE_INPUT_REJECTED", {"rebindActualArtifact": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
        ("V2-SYSTEM-LIST-NOT-APPLICABLE", "systemListNotApplicable", "SYSTEM_LIST_FORMS_SEMANTICS_DRIFT", {"rebindActualArtifact": ["reference"], "rebindAggregateVolumes": True, "rebindTerminalVolumes": True}),
    ]
    fixtures = {
        "artifactBindings": artifact_bindings,
        "fixtureProvenance": {
            "assessmentConsumerSourceRef": "0e54ce48c952a077b2c0f258c62b9f19c8d4a466",
            "assessmentConsumerTree": "46553801a1bdab1f0feda1b638514315004d5eb5",
            "baselineReview": "CCD-745+CCD-755+CCD-756",
            "consumerWireShape": "aspx-acquisition-verdict/v2",
            "readerShape": "aspx-platform-registry-reader-envelope/v2",
            "fixtureKind": "synthetic-no-tenant-payload",
        },
        "fixtureSchemaVersion": "aspx-platform-registry-fixtures/v6",
        "negative": [
            {
                "evidenceMutation": {"kind": mutation} if mutation else None,
                "expectedReasonCode": reason,
                "expectedVerdict": "Unknown",
                "id": identifier,
                "kind": "readerRequest",
                **extra,
            }
            for identifier, mutation, reason, extra in negative
        ],
        "positive": [
            {
                "expectedReasonCode": "REGISTRY_MATCH",
                "expectedVerdict": "ReferenceOnlyAvailable",
                "id": "V2-Q1",
                "kind": "readerRequest",
            }
        ],
        "readerTemplate": reader_template,
    }
    for case in fixtures["negative"]:
        if case["evidenceMutation"] is None:
            del case["evidenceMutation"]
    (FIXTURES / "contract-cases-v2.json").write_bytes(json_bytes(fixtures))


if __name__ == "__main__":
    main()
