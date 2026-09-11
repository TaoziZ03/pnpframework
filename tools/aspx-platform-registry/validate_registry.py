#!/usr/bin/env python3
"""Strict validator and reader-shaped fail-closed evaluator for registry v1."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import sqlite3
from contextlib import closing
from pathlib import Path
from typing import Any

from generate_registry import (
    ARTIFACT_VERIFICATION,
    AUTHORITY_REF,
    AUTHORITY_SCHEMA_VERSION,
    AUTHORITY_TAG,
    COMPATIBILITY_DECISION_REF,
    CONSUMER_PRODUCT_ID,
    CONSUMER_PRODUCT_REF,
    CONSUMER_SOURCE_REF,
    CONTRACT_REVIEW_REF,
    DISPOSITIONS,
    EXPECTED_AUTHORITY_ARTIFACT_HASH,
    EXPECTED_CONSUMER_COMPATIBILITY_HASH,
    EXPECTED_PROFILE_HASH,
    EXPECTED_REGISTRY_HASH,
    EXPECTED_REGISTRY_SCHEMA_HASH,
    FAILURE_SEMANTICS,
    PLATFORM_BUILD,
    PLATFORM_FAMILY,
    PROFILE_REVISION,
    PROFILE_SCHEMA_VERSION,
    REFERENCE_RECORD_KIND,
    REGISTRY_REVISION,
    SCHEMA_VERSION,
    SOURCE_KINDS,
    VOLUME_COMPATIBILITY,
    canonical_json_bytes,
    configure_release,
    discovery_hash,
    normalize_registry_key,
    object_hash,
    rule_hash,
    sqlite_schema_manifest,
    validate_profile_schema_resource,
)


KNOWN_DISPOSITIONS = set(DISPOSITIONS)
KNOWN_SOURCE_KINDS = set(SOURCE_KINDS)
NON_PHYSICAL_DISPOSITIONS = {
    "ReferenceOnlyAvailable",
    "ReferenceUnavailable",
    "VirtualHandler",
}
LINKED_PHYSICAL_DISPOSITIONS = {
    "LinkedPhysicalGhosted",
    "LinkedPhysicalCustomized",
}
CONSUMER_MAPPING_KEYS = {"assessment", "pnpGraph", "repro", "compare"}
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
GIT_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
PRODUCT_REF_RE = re.compile(r"^[^@\s]+@[0-9a-f]{40}$")
BUILD_RE = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$")
UTC_RE = re.compile(r"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")

REGISTRY_ENVELOPE_KEYS = {
    "profileSchemaVersion",
    "profileRevision",
    "profileHash",
    "registrySchemaVersion",
    "registrySchemaHash",
    "registryRevision",
    "registryHash",
    "authoritySourceRef",
    "authorityArtifactHash",
    "platformFamily",
    "platformBuild",
}
ACQUISITION_ENVELOPE_KEYS = {
    "outputVersion",
    "runId",
    "productRef",
    "scopeAuthorityHash",
    "snapshotFence",
    "asOfUtc",
    "platformBuild",
    "registryRevision",
    "registryHash",
    "physicalArtifactHash",
    "referenceArtifactHash",
    "aggregateVerdict",
    "physicalVolume",
    "referenceVolume",
}
BASE_VOLUME_KEYS = {
    "outputVersion",
    "contractVersion",
    "runId",
    "productRef",
    "scopeAuthorityHash",
    "snapshotFence",
    "platformBuild",
    "artifactHash",
    "artifactLength",
    "store",
}
REFERENCE_VOLUME_KEYS = BASE_VOLUME_KEYS | {
    "registryRevision",
    "registryHash",
    "recordKind",
    "sourceKinds",
    "dispositions",
}
STORE_KEYS = {"schemaVersion", "manifestHash"}
ARTIFACT_BINDING_KEYS = {
    "physicalOutput",
    "referenceOutput",
    "physicalStore",
    "referenceStore",
}
ARTIFACT_DESCRIPTOR_KEYS = {"path", "sha256", "length"}


def configure_validation_release(spec: dict[str, Any]) -> None:
    """Keep validator pins aligned with the selected generator release spec."""
    import generate_registry as generator

    configure_release(spec)
    names = [
        "AUTHORITY_REF",
        "AUTHORITY_TAG",
        "EXPECTED_AUTHORITY_ARTIFACT_HASH",
        "EXPECTED_CONSUMER_COMPATIBILITY_HASH",
        "EXPECTED_PROFILE_HASH",
        "EXPECTED_REGISTRY_HASH",
        "EXPECTED_REGISTRY_SCHEMA_HASH",
        "PLATFORM_BUILD",
        "PROFILE_REVISION",
        "REGISTRY_REVISION",
    ]
    for name in names:
        globals()[name] = getattr(generator, name)
    globals()["KNOWN_DISPOSITIONS"] = set(generator.DISPOSITIONS)
    globals()["KNOWN_SOURCE_KINDS"] = set(generator.SOURCE_KINDS)
STORE_ARTIFACT_DESCRIPTOR_KEYS = ARTIFACT_DESCRIPTOR_KEYS | {"schemaManifestHash"}

PHYSICAL_SOURCE_KINDS = {
    "RawListLibraryFiles",
    "WebRootFiles",
    "ListFormBackingFiles",
    "ListViewBackingFiles",
    "TenantManifest",
}
DISCOVERY_SCOPE_KINDS = {"Tenant", "Geo", "SiteCollection", "Web", "Container", "Folder"}
DISCOVERY_TERMINAL_OUTCOMES = {
    "Pending",
    "Complete",
    "Empty",
    "PolicyExcluded",
    "Denied",
    "Failed",
    "Truncated",
    "Cancelled",
    "Unknown",
}
DISCOVERY_ATTEMPT_STATUSES = {"Running", "Interrupted", "Failed", "Superseded", "Complete"}
DISCOVERY_EXECUTION_STATUSES = {"Running", "Finished", "Failed", "Cancelled"}
DISCOVERY_VERDICTS = {
    "CompleteTenantVerified",
    "CompleteAuthorizedSurface",
    "CompleteDeclaredSubset",
    "Incomplete",
    "Unknown",
}
REFERENCE_VERDICTS = {"CompleteAuthorizedSurface", "Incomplete", "Unknown"}
REFERENCE_APPLICABILITY = {"Applicable", "SystemOrVirtualOnly", "NotApplicable", "Unknown"}
REFERENCE_COUNTEREXAMPLE_STATES = {"NoneObserved", "Observed", "Unknown"}
REFERENCE_EXPECTED_COUNT_STATES = {"Known", "Unknown"}

PHYSICAL_OUTPUT_KEYS = {
    "outputVersion",
    "runId",
    "executionStatus",
    "coverageVerdict",
    "inventory",
    "observations",
    "coverage",
    "denominator",
    "unresolvedGapCodes",
    "unresolvedConflictCount",
}
PHYSICAL_INVENTORY_KEYS = {
    "scopeKey",
    "canonicalInventoryKey",
    "physicalLocator",
    "fileName",
    "identityQuality",
    "permissionContext",
}
PHYSICAL_OBSERVATION_KEYS = {
    "scopeKey",
    "sourceKind",
    "observationKey",
    "factHash",
    "fileName",
    "physicalLocator",
    "permissionContext",
}
PHYSICAL_COVERAGE_KEYS = {
    "scopeKey",
    "parentScopeKey",
    "kind",
    "sourceKind",
    "outcome",
    "expectedCount",
    "counts",
}
PHYSICAL_COUNTS_KEYS = {
    "observedCount",
    "emittedCount",
    "inventoryCount",
    "batchCount",
    "attemptCount",
    "gapCount",
    "conflictCount",
}
PHYSICAL_DENOMINATOR_KEYS = {
    "parentScopeKey",
    "childKind",
    "outcome",
    "expectedCount",
    "observedCount",
    "enumerationFingerprint",
    "permissionContext",
}
REFERENCE_OUTPUT_KEYS = {
    "outputVersion",
    "acquisitionRunId",
    "manifestHash",
    "coverageVerdict",
    "references",
    "denominator",
    "paginationReceipts",
    "gapCodes",
}
REFERENCE_OBSERVATION_KEYS = {
    "referenceObservationId",
    "recordKind",
    "sourceKind",
    "sourceObjectId",
    "acquisitionMethod",
    "referenceId",
    "rawLocator",
    "canonicalRequestPath",
    "matchedAlias",
    "platformBuildRef",
    "registryRevision",
    "registryHash",
    "disposition",
    "reasonCode",
    "linkedPhysicalCanonicalInventoryKey",
    "linkedFileUniqueId",
    "contentOrigin",
    "permissionContext",
    "evidenceRefs",
}
REFERENCE_DENOMINATOR_KEYS = {
    "surfaceContractVersion",
    "acquisitionRunId",
    "snapshotFence",
    "scopeAuthorityHash",
    "scopeKey",
    "parentScopeKey",
    "surfaceId",
    "applicability",
    "applicabilityRuleId",
    "applicabilityRuleVersion",
    "applicabilityRuleHash",
    "applicabilityReviewRef",
    "applicabilityApprovalRef",
    "applicabilityPlatformBinding",
    "runtimeCounterexampleState",
    "authorityKind",
    "authorityLocator",
    "authorityRevision",
    "authorityHash",
    "actualMethod",
    "actualEndpoint",
    "actualSelect",
    "actualFilter",
    "visibilityBoundary",
    "permissionContext",
    "expectedCount",
    "expectedCountState",
    "observedCount",
    "terminalOutcome",
    "aggregateEffect",
    "continuationRemaining",
    "paginationChainHash",
    "paginationOutstandingTokenCount",
    "absenceProofKind",
    "absenceProofRef",
    "providerVersion",
    "productRef",
    "sdkRef",
    "platformBuildRef",
    "registryRevision",
    "registryHash",
    "artifactRunId",
    "asOfUtc",
    "evidenceRefs",
    "requiredAdapter",
    "classificationEffect",
}
REFERENCE_PAGINATION_KEYS = {
    "collectionScopeKey",
    "authorityRevision",
    "actualEndpointHash",
    "pageOrdinal",
    "requestTokenHash",
    "responseItemCount",
    "nextTokenHash",
    "responseDigest",
    "terminalFlag",
    "receivedAtUtc",
}


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def artifact_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def parse_build(value: str) -> tuple[int, int, int, int]:
    if not isinstance(value, str) or not BUILD_RE.fullmatch(value):
        raise ValueError("invalid platform build")
    return tuple(int(part) for part in value.split("."))  # type: ignore[return-value]


def exact_key_errors(value: Any, expected: set[str], label: str) -> list[str]:
    if not isinstance(value, dict):
        return [f"{label} must be an object"]
    actual = set(value)
    missing = sorted(expected - actual)
    extra = sorted(actual - expected)
    errors: list[str] = []
    if missing:
        errors.append(f"{label} missing fields: {missing}")
    if extra:
        errors.append(f"{label} has unsupported fields: {extra}")
    return errors


def _string_errors(value: Any, label: str, nullable: bool = False) -> list[str]:
    if value is None and nullable:
        return []
    if not isinstance(value, str):
        return [f"{label} must be a string" + (" or null" if nullable else "")]
    return []


def _integer_errors(value: Any, label: str, nullable: bool = False) -> list[str]:
    if value is None and nullable:
        return []
    if not isinstance(value, int) or isinstance(value, bool):
        return [f"{label} must be an integer" + (" or null" if nullable else "")]
    return []


def _string_list_errors(value: Any, label: str) -> list[str]:
    if not isinstance(value, list):
        return [f"{label} must be an array"]
    return [
        f"{label}[{index}] must be a string"
        for index, item in enumerate(value)
        if not isinstance(item, str)
    ]


def _enum_errors(value: Any, allowed: set[str], label: str, nullable: bool = False) -> list[str]:
    if value is None and nullable:
        return []
    if value not in allowed:
        return [f"{label} has an unsupported value"]
    return []


def _rows_errors(value: Any, label: str, validator: Any) -> list[str]:
    if not isinstance(value, list):
        return [f"{label} must be an array"]
    errors: list[str] = []
    for index, row in enumerate(value):
        errors.extend(validator(row, f"{label}[{index}]"))
    return errors


def schema_validation_errors(instance: Any, schema: dict[str, Any]) -> list[str]:
    try:
        from jsonschema import Draft202012Validator
    except ImportError as error:  # pragma: no cover - environment diagnosis
        return [f"jsonschema unavailable: {error}"]
    validator = Draft202012Validator(schema)
    return [
        f"{'.'.join(str(part) for part in failure.absolute_path) or '$'}: {failure.message}"
        for failure in sorted(validator.iter_errors(instance), key=lambda item: list(item.absolute_path))
    ]


def validate_authority(authority: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    required = {
        "authoritySchemaVersion",
        "authorityArtifactHash",
        "authorityKind",
        "authoritySourceRef",
        "authoritySourceTag",
        "authorityCommitTime",
        "platformFamily",
        "platformBuildMin",
        "platformBuildMax",
        "sourceArtifacts",
        "deployRows",
        "legacyLcidRedirects",
    }
    missing = sorted(required - set(authority))
    if missing:
        errors.append(f"authority missing fields: {missing}")
        return errors
    expected_values = {
        "authoritySchemaVersion": AUTHORITY_SCHEMA_VERSION,
        "authorityKind": "SPOCoreReleaseShippingManifest",
        "authoritySourceRef": AUTHORITY_REF,
        "authoritySourceTag": AUTHORITY_TAG,
        "platformFamily": PLATFORM_FAMILY,
        "platformBuildMin": PLATFORM_BUILD,
        "platformBuildMax": PLATFORM_BUILD,
    }
    for key, expected in expected_values.items():
        if authority.get(key) != expected:
            errors.append(f"authority {key} is not the frozen value")
    if not UTC_RE.fullmatch(str(authority.get("authorityCommitTime"))):
        errors.append("authorityCommitTime is not canonical UTC Z format")
    expected_hash = object_hash(authority, "authorityArtifactHash")
    if authority.get("authorityArtifactHash") != expected_hash:
        errors.append("authorityArtifactHash mismatch")
    elif authority.get("authorityArtifactHash") != EXPECTED_AUTHORITY_ARTIFACT_HASH:
        errors.append("authorityArtifactHash is not the externally pinned value")
    if not authority.get("deployRows"):
        errors.append("authority deployRows must be non-empty")
    source_artifacts = authority.get("sourceArtifacts", {})
    manifest_rows = source_artifacts.get("shippingManifests", []) if isinstance(source_artifacts, dict) else []
    if not manifest_rows:
        errors.append("authority shippingManifests must be non-empty")
    return errors


def validate_registry(
    registry: dict[str, Any], authority: dict[str, Any] | None = None
) -> list[str]:
    errors: list[str] = []
    required = {
        "registrySchemaVersion",
        "registryRevision",
        "registryHash",
        "authorityKind",
        "authoritySourceRef",
        "authoritySourceTag",
        "authorityArtifactHash",
        "reviewRef",
        "contractReviewRef",
        "compatibilityDecisionRef",
        "platformFamily",
        "platformBuildMin",
        "platformBuildMax",
        "normalization",
        "failureSemantics",
        "applicabilityRules",
        "consumerCompatibility",
        "entryCount",
        "entries",
    }
    missing = sorted(required - set(registry))
    if missing:
        errors.append(f"registry missing fields: {missing}")
        return errors

    expected_values = {
        "registrySchemaVersion": SCHEMA_VERSION,
        "registryRevision": REGISTRY_REVISION,
        "authorityKind": "SPOCoreReleaseShippingManifest",
        "authoritySourceRef": AUTHORITY_REF,
        "authoritySourceTag": AUTHORITY_TAG,
        "contractReviewRef": CONTRACT_REVIEW_REF,
        "compatibilityDecisionRef": COMPATIBILITY_DECISION_REF,
        "platformFamily": PLATFORM_FAMILY,
        "platformBuildMin": PLATFORM_BUILD,
        "platformBuildMax": PLATFORM_BUILD,
    }
    for key, expected in expected_values.items():
        if registry.get(key) != expected:
            errors.append(f"{key} is not the frozen profile value")
    if not GIT_SHA_RE.fullmatch(str(registry.get("authoritySourceRef"))):
        errors.append("authoritySourceRef is not a full Git SHA")
    if not SHA256_RE.fullmatch(str(registry.get("authorityArtifactHash"))):
        errors.append("authorityArtifactHash is not SHA-256")
    elif registry.get("authorityArtifactHash") != EXPECTED_AUTHORITY_ARTIFACT_HASH:
        errors.append("authorityArtifactHash is not the externally pinned value")
    if not SHA256_RE.fullmatch(str(registry.get("registryHash"))):
        errors.append("registryHash is not SHA-256")
    elif registry["registryHash"] != object_hash(registry, "registryHash"):
        errors.append("registryHash mismatch")
    elif registry["registryHash"] != EXPECTED_REGISTRY_HASH:
        errors.append("registryHash is not the externally pinned value")

    entries = registry.get("entries", [])
    if registry.get("entryCount") != len(entries):
        errors.append("entryCount does not match entries length")
    if not entries:
        errors.append("entries must be non-empty")

    rules = {
        rule.get("applicabilityRuleId"): rule
        for rule in registry.get("applicabilityRules", [])
        if isinstance(rule, dict)
    }
    for rule_id, rule in rules.items():
        if not rule_id:
            errors.append("applicability rule has no ID")
            continue
        if rule.get("applicabilityRuleHash") != rule_hash(rule):
            errors.append(f"applicability rule hash mismatch: {rule_id}")

    explicit_path_owners: dict[str, str] = {}
    reference_ids: set[str] = set()
    for index, entry in enumerate(entries):
        prefix = f"entries[{index}]"
        if not isinstance(entry, dict):
            errors.append(f"{prefix}: entry must be an object")
            continue
        reference_id = entry.get("referenceId")
        if not reference_id or reference_id in reference_ids:
            errors.append(f"{prefix}: duplicate or missing referenceId")
        reference_ids.add(str(reference_id))
        disposition = entry.get("downstreamDisposition")
        if disposition not in KNOWN_DISPOSITIONS:
            errors.append(f"{prefix}: unknown downstreamDisposition")
        canonical_path = entry.get("canonicalRequestPath")
        if not isinstance(canonical_path, str) or not canonical_path.casefold().startswith("/_layouts/15/"):
            errors.append(f"{prefix}: invalid canonicalRequestPath")
            continue
        if not canonical_path.casefold().endswith(".aspx"):
            errors.append(f"{prefix}: canonicalRequestPath is not ASPX")
        for path in [canonical_path, *entry.get("aliases", [])]:
            try:
                key = normalize_registry_key(path)
            except ValueError as error:
                errors.append(f"{prefix}: {error}")
                continue
            previous = explicit_path_owners.get(key)
            if previous is not None and previous != reference_id:
                errors.append(f"alias normalization collision: {path} maps to {previous} and {reference_id}")
            explicit_path_owners[key] = str(reference_id)

        rule_id = entry.get("applicabilityRuleId")
        rule = rules.get(rule_id)
        if not rule:
            errors.append(f"{prefix}: applicabilityRuleId does not resolve")
        elif entry.get("applicabilityRuleHash") != rule.get("applicabilityRuleHash"):
            errors.append(f"{prefix}: applicabilityRuleHash does not match rule")

        physical = entry.get("physicalIdentity", {})
        if disposition in NON_PHYSICAL_DISPOSITIONS:
            if physical.get("fileUniqueId") is not None:
                errors.append(f"{prefix}: non-physical disposition carries FileUniqueId")
            if physical.get("physicalLocator") is not None:
                errors.append(f"{prefix}: non-physical disposition carries PhysicalLocator")
            if entry.get("identityAxes", {}).get("ghostedPhysicalFileIdentity") is not None:
                errors.append(f"{prefix}: non-physical disposition carries ghosted identity")

    compatibility = registry.get("consumerCompatibility")
    errors.extend(exact_key_errors(
        compatibility,
        {
            "compatibilityDecisionRef",
            "recordKind",
            "sourceKinds",
            "dispositions",
            "volumeCompatibility",
            "artifactVerification",
        },
        "consumerCompatibility",
    ))
    if isinstance(compatibility, dict):
        if compatibility.get("compatibilityDecisionRef") != COMPATIBILITY_DECISION_REF:
            errors.append("consumer compatibility decision is not frozen")
        if compatibility.get("recordKind") != REFERENCE_RECORD_KIND:
            errors.append("consumer recordKind is not frozen")
        if compatibility.get("sourceKinds") != SOURCE_KINDS:
            errors.append("consumer sourceKinds are not the fixed five wire strings")
        if compatibility.get("volumeCompatibility") != VOLUME_COMPATIBILITY:
            errors.append("consumer volumeCompatibility is not the fixed seven-version profile")
        if compatibility.get("artifactVerification") != ARTIFACT_VERIFICATION:
            errors.append("consumer artifactVerification is not the frozen actual-artifact policy")
        disposition_map = compatibility.get("dispositions")
        errors.extend(exact_key_errors(disposition_map, KNOWN_DISPOSITIONS, "consumer dispositions"))
        if isinstance(disposition_map, dict):
            for disposition, mapping in disposition_map.items():
                errors.extend(exact_key_errors(mapping, CONSUMER_MAPPING_KEYS, f"consumer dispositions.{disposition}"))
                if isinstance(mapping, dict):
                    for consumer, statement in mapping.items():
                        if not isinstance(statement, str) or not statement.strip():
                            errors.append(f"consumer dispositions.{disposition}.{consumer} must be non-empty")

    if registry.get("failureSemantics") != FAILURE_SEMANTICS:
        errors.append("failureSemantics is not the complete frozen fail-closed map")

    if authority is not None:
        errors.extend(validate_authority(authority))
        for registry_key, authority_key in [
            ("authorityArtifactHash", "authorityArtifactHash"),
            ("authoritySourceRef", "authoritySourceRef"),
            ("authoritySourceTag", "authoritySourceTag"),
            ("platformBuildMin", "platformBuildMin"),
            ("platformBuildMax", "platformBuildMax"),
        ]:
            if registry.get(registry_key) != authority.get(authority_key):
                errors.append(f"registry {registry_key} does not match authority volume")
    return errors


def validate_profile(
    profile: dict[str, Any],
    registry: dict[str, Any],
    authority: dict[str, Any] | None = None,
    registry_schema_hash: str | None = None,
) -> list[str]:
    expected_keys = {
        "$schema",
        "profileSchemaVersion",
        "profileRevision",
        "profileHash",
        "registrySchemaVersion",
        "registrySchemaHash",
        "registryRevision",
        "registryHash",
        "authoritySchemaVersion",
        "authorityKind",
        "authoritySourceRef",
        "authoritySourceTag",
        "authorityArtifactHash",
        "platformFamily",
        "platformBuild",
        "consumerProductId",
        "consumerSourceRef",
        "consumerProductRef",
        "contractReviewRef",
        "compatibilityDecisionRef",
        "consumerCompatibilityHash",
        "recordKind",
        "sourceKinds",
        "dispositions",
        "volumeCompatibility",
        "artifactVerification",
        "failureSemantics",
    }
    errors = exact_key_errors(profile, expected_keys, "profile")
    if errors:
        return errors
    expected_values = {
        "$schema": "../schema/aspx-platform-registry-profile.schema.json",
        "profileSchemaVersion": PROFILE_SCHEMA_VERSION,
        "profileRevision": PROFILE_REVISION,
        "registrySchemaVersion": SCHEMA_VERSION,
        "registryRevision": REGISTRY_REVISION,
        "authoritySchemaVersion": AUTHORITY_SCHEMA_VERSION,
        "authorityKind": "SPOCoreReleaseShippingManifest",
        "authoritySourceRef": AUTHORITY_REF,
        "authoritySourceTag": AUTHORITY_TAG,
        "platformFamily": PLATFORM_FAMILY,
        "platformBuild": PLATFORM_BUILD,
        "consumerProductId": CONSUMER_PRODUCT_ID,
        "consumerSourceRef": CONSUMER_SOURCE_REF,
        "consumerProductRef": CONSUMER_PRODUCT_REF,
        "contractReviewRef": CONTRACT_REVIEW_REF,
        "compatibilityDecisionRef": COMPATIBILITY_DECISION_REF,
        "recordKind": REFERENCE_RECORD_KIND,
        "profileHash": EXPECTED_PROFILE_HASH,
        "registrySchemaHash": EXPECTED_REGISTRY_SCHEMA_HASH,
        "registryHash": EXPECTED_REGISTRY_HASH,
        "authorityArtifactHash": EXPECTED_AUTHORITY_ARTIFACT_HASH,
        "consumerCompatibilityHash": EXPECTED_CONSUMER_COMPATIBILITY_HASH,
    }
    for key, expected in expected_values.items():
        if profile.get(key) != expected:
            errors.append(f"profile {key} is not the frozen value")
    if profile.get("profileHash") != object_hash(profile, "profileHash"):
        errors.append("profileHash mismatch")
    if profile.get("registryHash") != registry.get("registryHash"):
        errors.append("profile registryHash does not match registry")
    if profile.get("authorityArtifactHash") != registry.get("authorityArtifactHash"):
        errors.append("profile authorityArtifactHash does not match registry")
    if profile.get("sourceKinds") != SOURCE_KINDS:
        errors.append("profile sourceKinds are not the fixed five wire strings")
    if profile.get("dispositions") != DISPOSITIONS:
        errors.append("profile dispositions are not the fixed seven wire strings")
    if profile.get("volumeCompatibility") != VOLUME_COMPATIBILITY:
        errors.append("profile volumeCompatibility is not frozen")
    if profile.get("artifactVerification") != ARTIFACT_VERIFICATION:
        errors.append("profile artifactVerification is not frozen")
    if profile.get("failureSemantics") != FAILURE_SEMANTICS:
        errors.append("profile failureSemantics is not frozen")
    expected_compatibility_hash = hashlib.sha256(
        canonical_json_bytes(registry.get("consumerCompatibility"))
    ).hexdigest()
    if profile.get("consumerCompatibilityHash") != expected_compatibility_hash:
        errors.append("profile consumerCompatibilityHash does not match registry")
    if registry_schema_hash is not None and profile.get("registrySchemaHash") != registry_schema_hash:
        errors.append("profile registrySchemaHash does not match schema artifact")
    if authority is not None and profile.get("authorityArtifactHash") != authority.get("authorityArtifactHash"):
        errors.append("profile authorityArtifactHash does not match authority")
    return errors


def build_lookup(registry: dict[str, Any]) -> dict[str, dict[str, Any]]:
    lookup: dict[str, dict[str, Any]] = {}
    for entry in registry["entries"]:
        for path in [entry["canonicalRequestPath"], *entry["aliases"]]:
            key = normalize_registry_key(path)
            if key in lookup and lookup[key]["referenceId"] != entry["referenceId"]:
                raise ValueError("alias collision")
            lookup[key] = entry
    return lookup


def lookup_runtime_path(
    registry: dict[str, Any], lookup: dict[str, dict[str, Any]], runtime_path: str
) -> dict[str, Any] | None:
    normalized = normalize_registry_key(runtime_path)
    direct = lookup.get(normalized)
    if direct is not None:
        return direct
    legacy_match = re.fullmatch(
        r"/_layouts/(?:15/)?(?P<lcid>[0-9]+)/(?P<file>[^/]+\.aspx)",
        runtime_path,
        re.IGNORECASE,
    )
    if not legacy_match:
        return None
    candidate_key = normalize_registry_key(f"/_layouts/15/{legacy_match.group('file')}")
    candidate = lookup.get(candidate_key)
    if candidate is None:
        return None
    expected_pattern = f"/_layouts/{{lcid}}/{legacy_match.group('file')}".casefold()
    patterns = {pattern.casefold() for pattern in candidate.get("aliasPatterns", [])}
    return candidate if expected_pattern in patterns else None


def _validate_store(
    store: Any,
    expected_version: str,
    label: str,
) -> tuple[str | None, list[str]]:
    errors = exact_key_errors(store, STORE_KEYS, label)
    if errors:
        return f"{label.upper().replace(' ', '_')}_SHAPE_INVALID", errors
    assert isinstance(store, dict)
    if store.get("schemaVersion") != expected_version:
        return "UNSUPPORTED_STORE_VERSION", [f"{label}.schemaVersion is unsupported"]
    if not SHA256_RE.fullmatch(str(store.get("manifestHash"))):
        return f"{label.upper().replace(' ', '_')}_SHAPE_INVALID", [f"{label}.manifestHash is invalid"]
    return None, []


def _sqlite_connection(database_bytes: bytes) -> sqlite3.Connection:
    connection = sqlite3.connect(":memory:")
    try:
        connection.deserialize(database_bytes)
    except Exception:
        connection.close()
        raise
    return connection


def _sqlite_schema_hash_from_bytes(database_bytes: bytes) -> str:
    with closing(_sqlite_connection(database_bytes)) as connection:
        manifest = sqlite_schema_manifest(connection)
    return hashlib.sha256(canonical_json_bytes(manifest)).hexdigest()


def artifact_evidence_summary(evidence: dict[str, bytes]) -> dict[str, Any]:
    summary: dict[str, Any] = {}
    for key, artifact_bytes in sorted(evidence.items()):
        descriptor: dict[str, Any] = {
            "sha256": hashlib.sha256(artifact_bytes).hexdigest(),
            "length": len(artifact_bytes),
        }
        if key.endswith("Store"):
            try:
                descriptor["schemaManifestHash"] = _sqlite_schema_hash_from_bytes(
                    artifact_bytes
                )
            except sqlite3.DatabaseError:
                descriptor["schemaManifestHash"] = None
        summary[key] = descriptor
    return summary


def load_fixture_artifact_evidence(
    bindings: Any,
    fixture_root: Path,
) -> tuple[dict[str, bytes], list[str]]:
    errors = exact_key_errors(bindings, ARTIFACT_BINDING_KEYS, "artifactBindings")
    if errors:
        return {}, errors
    assert isinstance(bindings, dict)
    resolved_root = fixture_root.resolve()
    evidence: dict[str, bytes] = {}
    for key in sorted(ARTIFACT_BINDING_KEYS):
        descriptor = bindings.get(key)
        expected_keys = (
            STORE_ARTIFACT_DESCRIPTOR_KEYS
            if key.endswith("Store")
            else ARTIFACT_DESCRIPTOR_KEYS
        )
        descriptor_errors = exact_key_errors(
            descriptor, expected_keys, f"artifactBindings.{key}"
        )
        if descriptor_errors:
            errors.extend(descriptor_errors)
            continue
        assert isinstance(descriptor, dict)
        relative_path = descriptor.get("path")
        if not isinstance(relative_path, str) or not relative_path:
            errors.append(f"artifactBindings.{key}.path is invalid")
            continue
        artifact_path = (resolved_root / relative_path).resolve()
        if resolved_root not in artifact_path.parents or not artifact_path.is_file():
            errors.append(f"artifactBindings.{key}.path is outside the fixture root or missing")
            continue
        artifact_bytes = artifact_path.read_bytes()
        evidence[key] = artifact_bytes
        if descriptor.get("sha256") != hashlib.sha256(artifact_bytes).hexdigest():
            errors.append(f"artifactBindings.{key}.sha256 does not match actual bytes")
        if descriptor.get("length") != len(artifact_bytes):
            errors.append(f"artifactBindings.{key}.length does not match actual bytes")
        if key.endswith("Store"):
            try:
                schema_hash = _sqlite_schema_hash_from_bytes(artifact_bytes)
            except sqlite3.DatabaseError as error:
                errors.append(f"artifactBindings.{key} is not readable SQLite: {error}")
            else:
                if descriptor.get("schemaManifestHash") != schema_hash:
                    errors.append(
                        f"artifactBindings.{key}.schemaManifestHash does not match actual SQLite schema"
                    )
    return evidence, errors


def apply_evidence_mutation(
    evidence: dict[str, bytes], mutation: dict[str, Any] | None
) -> dict[str, bytes]:
    mutated = dict(evidence)
    if not mutation:
        return mutated
    kind = mutation["kind"]
    if kind == "appendPhysicalOutputBytes":
        mutated["physicalOutput"] += b"\n"
    elif kind == "appendReferenceOutputBytes":
        mutated["referenceOutput"] += b"\n"
    elif kind in {
        "addPhysicalReferenceRowsField",
        "addPhysicalReferenceObservation",
        "addReferenceUnknownSourceKind",
        "driftReferenceOutputManifestHash",
        "replacePhysicalOutputWithArray",
    }:
        evidence_key = "referenceOutput" if kind in {
            "addReferenceUnknownSourceKind",
            "driftReferenceOutputManifestHash",
        } else "physicalOutput"
        document = json.loads(mutated[evidence_key])
        if kind == "addPhysicalReferenceRowsField":
            document["referenceRows"] = []
        elif kind == "addPhysicalReferenceObservation":
            document["observations"].append(
                {
                    "recordKind": REFERENCE_RECORD_KIND,
                    "sourceKind": "PlatformRegistryReference",
                    "disposition": "ReferenceOnlyAvailable",
                }
            )
        elif kind == "addReferenceUnknownSourceKind":
            document["references"].append(
                {
                    "recordKind": REFERENCE_RECORD_KIND,
                    "sourceKind": "FutureUnknownSourceKind",
                    "disposition": "ReferenceOnlyAvailable",
                }
            )
        elif kind == "driftReferenceOutputManifestHash":
            document["manifestHash"] = "f" * 64
        else:
            document = []
        mutated[evidence_key] = (
            json.dumps(document, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
        ).encode("utf-8")
    elif kind in {"addReferenceTableToPhysicalStore", "dropReferenceObservationTable"}:
        store_key = (
            "physicalStore"
            if kind == "addReferenceTableToPhysicalStore"
            else "referenceStore"
        )
        with closing(_sqlite_connection(mutated[store_key])) as connection:
            if kind == "addReferenceTableToPhysicalStore":
                connection.execute(
                    "CREATE TABLE ReferenceObservations "
                    "(RunId TEXT NOT NULL, ObservationId TEXT NOT NULL, Json TEXT NOT NULL, "
                    "PRIMARY KEY (RunId, ObservationId))"
                )
            else:
                connection.execute("DROP TABLE ReferenceObservations")
            connection.commit()
            mutated[store_key] = connection.serialize()
    elif kind in {
        "changePhysicalPartialIndexPredicate",
        "addPhysicalGeneratedReferenceColumn",
        "addPhysicalReferenceSourceKindRow",
        "addReferenceUnknownSourceKindRow",
    }:
        store_key = "referenceStore" if kind == "addReferenceUnknownSourceKindRow" else "physicalStore"
        with closing(_sqlite_connection(mutated[store_key])) as connection:
            run_table = "ReferenceRuns" if store_key == "referenceStore" else "DiscoveryRuns"
            run_row = connection.execute(f"SELECT RunId FROM {run_table} ORDER BY RunId LIMIT 1").fetchone()
            if run_row is None:
                raise ValueError(f"{run_table} has no fixture run")
            run_id = str(run_row[0])
            if kind == "changePhysicalPartialIndexPredicate":
                connection.execute("DROP INDEX UX_DiscoveryAttempts_Active")
                connection.execute(
                    "CREATE UNIQUE INDEX UX_DiscoveryAttempts_Active "
                    "ON DiscoveryAttempts(RunId, ScopeKey, SourceKind) WHERE Status='Finished'"
                )
            elif kind == "addPhysicalGeneratedReferenceColumn":
                connection.execute(
                    "ALTER TABLE DiscoveryInventory ADD COLUMN ReferenceDerived TEXT "
                    "GENERATED ALWAYS AS (FileName) VIRTUAL"
                )
            elif kind == "addPhysicalReferenceSourceKindRow":
                connection.execute(
                    "INSERT INTO DiscoveryObservations "
                    "(ObservationId, RunId, ScopeKey, SourceKind, ObservationKey, FactHash, "
                    "SourceObjectKey, MetadataJson) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    (
                        "injected-reference-row",
                        run_id,
                        "fixture-scope",
                        "PlatformRegistryReference",
                        "injected-key",
                        "f" * 64,
                        "injected-object",
                        "{}",
                    ),
                )
            else:
                connection.execute(
                    "INSERT INTO ReferenceObservations (RunId, ObservationId, Json) VALUES (?, ?, ?)",
                    (
                        run_id,
                        "unknown-source-kind",
                        json.dumps(
                            {
                                "recordKind": REFERENCE_RECORD_KIND,
                                "sourceKind": "FutureUnknownSourceKind",
                                "disposition": "ReferenceOnlyAvailable",
                            },
                            separators=(",", ":"),
                        ),
                    ),
                )
            connection.commit()
            mutated[store_key] = connection.serialize()
    elif kind in {"rewritePhysicalManifestHash", "rewriteReferenceManifestHash"}:
        store_key = (
            "physicalStore"
            if kind == "rewritePhysicalManifestHash"
            else "referenceStore"
        )
        table = "DiscoveryRuns" if store_key == "physicalStore" else "ReferenceRuns"
        with closing(_sqlite_connection(mutated[store_key])) as connection:
            connection.execute(f"UPDATE {table} SET ManifestHash=?", ("f" * 64,))
            connection.commit()
            mutated[store_key] = connection.serialize()
    elif kind in {
        "rewritePhysicalProductPrefixAndRehashManifest",
        "rewriteReferenceProductPrefixAndRehashManifest",
    }:
        reference_store = kind == "rewriteReferenceProductPrefixAndRehashManifest"
        store_key = "referenceStore" if reference_store else "physicalStore"
        table = "ReferenceRuns" if reference_store else "DiscoveryRuns"
        with closing(_sqlite_connection(mutated[store_key])) as connection:
            row = connection.execute(
                f"SELECT RunId, ManifestJson FROM {table} ORDER BY RunId LIMIT 1"
            ).fetchone()
            if row is None:
                raise ValueError(f"{table} has no fixture run")
            manifest = json.loads(row[1])
            source_ref = str(manifest["productRef"]).rsplit("@", 1)[1]
            manifest["productRef"] = f"pnp/other-assessment@{source_ref}"
            manifest_json = json.dumps(manifest, ensure_ascii=False, separators=(",", ":"))
            manifest_hash = discovery_hash(manifest_json)
            connection.execute(
                f"UPDATE {table} SET ManifestJson=?, ManifestHash=? WHERE RunId=?",
                (manifest_json, manifest_hash, row[0]),
            )
            connection.commit()
            mutated[store_key] = connection.serialize()
        if reference_store:
            output = json.loads(mutated["referenceOutput"])
            output["manifestHash"] = manifest_hash
            mutated["referenceOutput"] = (
                json.dumps(output, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
            ).encode("utf-8")
    else:
        raise ValueError(f"unknown evidence mutation: {kind}")
    return mutated


def _read_store_manifest_hash(
    database_bytes: bytes, reference_store: bool, run_id: str
) -> str:
    table = "ReferenceRuns" if reference_store else "DiscoveryRuns"
    with closing(_sqlite_connection(database_bytes)) as connection:
        row = connection.execute(
            f"SELECT ManifestHash FROM {table} WHERE RunId=?", (run_id,)
        ).fetchone()
    if row is None:
        raise ValueError(f"{table} does not contain run {run_id}")
    return str(row[0])


def _validate_physical_inventory_row(row: Any, label: str) -> list[str]:
    errors = exact_key_errors(row, PHYSICAL_INVENTORY_KEYS, label)
    if errors:
        return errors
    assert isinstance(row, dict)
    for key in ["scopeKey", "canonicalInventoryKey", "fileName", "identityQuality"]:
        errors.extend(_string_errors(row.get(key), f"{label}.{key}"))
    for key in ["physicalLocator", "permissionContext"]:
        errors.extend(_string_errors(row.get(key), f"{label}.{key}", nullable=True))
    return errors


def _validate_physical_observation_row(row: Any, label: str) -> list[str]:
    errors = exact_key_errors(row, PHYSICAL_OBSERVATION_KEYS, label)
    if errors:
        return errors
    assert isinstance(row, dict)
    for key in ["scopeKey", "observationKey", "factHash"]:
        errors.extend(_string_errors(row.get(key), f"{label}.{key}"))
    for key in ["fileName", "physicalLocator", "permissionContext"]:
        errors.extend(_string_errors(row.get(key), f"{label}.{key}", nullable=True))
    errors.extend(_enum_errors(row.get("sourceKind"), PHYSICAL_SOURCE_KINDS, f"{label}.sourceKind"))
    return errors


def _validate_physical_coverage_row(row: Any, label: str) -> list[str]:
    errors = exact_key_errors(row, PHYSICAL_COVERAGE_KEYS, label)
    if errors:
        return errors
    assert isinstance(row, dict)
    for key in ["scopeKey"]:
        errors.extend(_string_errors(row.get(key), f"{label}.{key}"))
    errors.extend(_string_errors(row.get("parentScopeKey"), f"{label}.parentScopeKey", nullable=True))
    errors.extend(_enum_errors(row.get("kind"), DISCOVERY_SCOPE_KINDS, f"{label}.kind"))
    errors.extend(
        _enum_errors(
            row.get("sourceKind"), PHYSICAL_SOURCE_KINDS, f"{label}.sourceKind", nullable=True
        )
    )
    errors.extend(
        _enum_errors(row.get("outcome"), DISCOVERY_TERMINAL_OUTCOMES, f"{label}.outcome")
    )
    errors.extend(_integer_errors(row.get("expectedCount"), f"{label}.expectedCount", nullable=True))
    counts = row.get("counts")
    errors.extend(exact_key_errors(counts, PHYSICAL_COUNTS_KEYS, f"{label}.counts"))
    if isinstance(counts, dict):
        for key in PHYSICAL_COUNTS_KEYS:
            errors.extend(_integer_errors(counts.get(key), f"{label}.counts.{key}"))
    return errors


def _validate_physical_denominator_row(row: Any, label: str) -> list[str]:
    errors = exact_key_errors(row, PHYSICAL_DENOMINATOR_KEYS, label)
    if errors:
        return errors
    assert isinstance(row, dict)
    errors.extend(_string_errors(row.get("parentScopeKey"), f"{label}.parentScopeKey"))
    errors.extend(_enum_errors(row.get("childKind"), DISCOVERY_SCOPE_KINDS, f"{label}.childKind"))
    errors.extend(
        _enum_errors(row.get("outcome"), DISCOVERY_TERMINAL_OUTCOMES, f"{label}.outcome")
    )
    for key in ["expectedCount", "observedCount"]:
        errors.extend(_integer_errors(row.get(key), f"{label}.{key}"))
    errors.extend(
        _string_errors(row.get("enumerationFingerprint"), f"{label}.enumerationFingerprint")
    )
    errors.extend(_string_errors(row.get("permissionContext"), f"{label}.permissionContext", nullable=True))
    return errors


def _validate_physical_output_document(document: Any) -> list[str]:
    errors = exact_key_errors(document, PHYSICAL_OUTPUT_KEYS, "physical output")
    if errors:
        return errors
    assert isinstance(document, dict)
    errors.extend(
        _enum_errors(
            document.get("executionStatus"), DISCOVERY_EXECUTION_STATUSES, "physical output.executionStatus"
        )
    )
    errors.extend(
        _enum_errors(document.get("coverageVerdict"), DISCOVERY_VERDICTS, "physical output.coverageVerdict")
    )
    errors.extend(
        _rows_errors(document.get("inventory"), "physical output.inventory", _validate_physical_inventory_row)
    )
    errors.extend(
        _rows_errors(
            document.get("observations"), "physical output.observations", _validate_physical_observation_row
        )
    )
    errors.extend(
        _rows_errors(document.get("coverage"), "physical output.coverage", _validate_physical_coverage_row)
    )
    errors.extend(
        _rows_errors(
            document.get("denominator"), "physical output.denominator", _validate_physical_denominator_row
        )
    )
    errors.extend(_string_list_errors(document.get("unresolvedGapCodes"), "physical output.unresolvedGapCodes"))
    errors.extend(
        _integer_errors(document.get("unresolvedConflictCount"), "physical output.unresolvedConflictCount")
    )
    return errors


def _validate_reference_observation_row(
    row: Any,
    label: str,
    volume: dict[str, Any],
    registry: dict[str, Any],
) -> list[str]:
    errors = exact_key_errors(row, REFERENCE_OBSERVATION_KEYS, label)
    if errors:
        return errors
    assert isinstance(row, dict)
    for key in [
        "referenceObservationId",
        "platformBuildRef",
        "registryRevision",
        "registryHash",
    ]:
        errors.extend(_string_errors(row.get(key), f"{label}.{key}"))
    for key in [
        "sourceObjectId",
        "acquisitionMethod",
        "referenceId",
        "rawLocator",
        "canonicalRequestPath",
        "matchedAlias",
        "reasonCode",
        "linkedPhysicalCanonicalInventoryKey",
        "linkedFileUniqueId",
        "contentOrigin",
        "permissionContext",
    ]:
        errors.extend(_string_errors(row.get(key), f"{label}.{key}", nullable=True))
    errors.extend(_enum_errors(row.get("recordKind"), {REFERENCE_RECORD_KIND}, f"{label}.recordKind"))
    errors.extend(_enum_errors(row.get("sourceKind"), KNOWN_SOURCE_KINDS, f"{label}.sourceKind"))
    errors.extend(_enum_errors(row.get("disposition"), KNOWN_DISPOSITIONS, f"{label}.disposition"))
    errors.extend(_string_list_errors(row.get("evidenceRefs"), f"{label}.evidenceRefs"))
    bindings = {
        "platformBuildRef": volume.get("platformBuild"),
        "registryRevision": registry.get("registryRevision"),
        "registryHash": registry.get("registryHash"),
    }
    for key, expected in bindings.items():
        if row.get(key) != expected:
            errors.append(f"{label}.{key} does not match the reference volume")
    disposition = row.get("disposition")
    linked_key = row.get("linkedPhysicalCanonicalInventoryKey")
    linked_id = row.get("linkedFileUniqueId")
    if disposition in NON_PHYSICAL_DISPOSITIONS | {"NonAspx"} and (linked_key or linked_id):
        errors.append(f"{label} has physical identity for a non-physical disposition")
    if disposition in LINKED_PHYSICAL_DISPOSITIONS and (not linked_key or not linked_id):
        errors.append(f"{label} is missing linked physical identity")
    if disposition == "LinkedPhysicalGhosted" and row.get("contentOrigin") != "verified-ghosted":
        errors.append(f"{label}.contentOrigin does not prove ghosted identity")
    return errors


def _validate_reference_denominator_row(
    row: Any,
    label: str,
    expected_run_id: str,
    volume: dict[str, Any],
    registry: dict[str, Any],
) -> list[str]:
    errors = exact_key_errors(row, REFERENCE_DENOMINATOR_KEYS, label)
    if errors:
        return errors
    assert isinstance(row, dict)
    errors.extend(_enum_errors(row.get("applicability"), REFERENCE_APPLICABILITY, f"{label}.applicability"))
    errors.extend(
        _enum_errors(
            row.get("runtimeCounterexampleState"),
            REFERENCE_COUNTEREXAMPLE_STATES,
            f"{label}.runtimeCounterexampleState",
        )
    )
    errors.extend(
        _enum_errors(
            row.get("expectedCountState"), REFERENCE_EXPECTED_COUNT_STATES, f"{label}.expectedCountState"
        )
    )
    errors.extend(
        _enum_errors(row.get("terminalOutcome"), DISCOVERY_TERMINAL_OUTCOMES, f"{label}.terminalOutcome")
    )
    errors.extend(_integer_errors(row.get("expectedCount"), f"{label}.expectedCount", nullable=True))
    for key in ["observedCount", "paginationOutstandingTokenCount"]:
        errors.extend(_integer_errors(row.get(key), f"{label}.{key}"))
    if not isinstance(row.get("continuationRemaining"), bool):
        errors.append(f"{label}.continuationRemaining must be a boolean")
    errors.extend(_string_list_errors(row.get("evidenceRefs"), f"{label}.evidenceRefs"))
    if row.get("expectedCountState") == "Unknown" and row.get("expectedCount") is not None:
        errors.append(f"{label}.expectedCount must be null when expectedCountState is Unknown")
    if row.get("expectedCountState") == "Known" and row.get("expectedCount") is None:
        errors.append(f"{label}.expectedCount is required when expectedCountState is Known")
    bindings = {
        "surfaceContractVersion": "aspx-surface-applicability-denominator/v3",
        "acquisitionRunId": expected_run_id,
        "snapshotFence": volume.get("snapshotFence"),
        "scopeAuthorityHash": volume.get("scopeAuthorityHash"),
        "productRef": volume.get("productRef"),
        "platformBuildRef": volume.get("platformBuild"),
        "registryRevision": registry.get("registryRevision"),
        "registryHash": registry.get("registryHash"),
        "artifactRunId": expected_run_id,
    }
    for key, expected in bindings.items():
        if row.get(key) != expected:
            errors.append(f"{label}.{key} does not match the reference volume")
    return errors


def _validate_reference_pagination_row(row: Any, label: str) -> list[str]:
    errors = exact_key_errors(row, REFERENCE_PAGINATION_KEYS, label)
    if errors:
        return errors
    assert isinstance(row, dict)
    for key in ["collectionScopeKey", "authorityRevision", "actualEndpointHash", "responseDigest", "receivedAtUtc"]:
        errors.extend(_string_errors(row.get(key), f"{label}.{key}"))
    for key in ["requestTokenHash", "nextTokenHash"]:
        errors.extend(_string_errors(row.get(key), f"{label}.{key}", nullable=True))
    for key in ["pageOrdinal", "responseItemCount"]:
        errors.extend(_integer_errors(row.get(key), f"{label}.{key}"))
    if not isinstance(row.get("terminalFlag"), bool):
        errors.append(f"{label}.terminalFlag must be a boolean")
    return errors


def _validate_reference_output_document(
    document: Any,
    expected_run_id: str,
    volume: dict[str, Any],
    registry: dict[str, Any],
) -> list[str]:
    errors = exact_key_errors(document, REFERENCE_OUTPUT_KEYS, "reference output")
    if errors:
        return errors
    assert isinstance(document, dict)
    if not SHA256_RE.fullmatch(str(document.get("manifestHash"))):
        errors.append("reference output.manifestHash must be SHA-256")
    errors.extend(
        _enum_errors(document.get("coverageVerdict"), REFERENCE_VERDICTS, "reference output.coverageVerdict")
    )
    errors.extend(
        _rows_errors(
            document.get("references"),
            "reference output.references",
            lambda row, label: _validate_reference_observation_row(row, label, volume, registry),
        )
    )
    errors.extend(
        _rows_errors(
            document.get("denominator"),
            "reference output.denominator",
            lambda row, label: _validate_reference_denominator_row(
                row, label, expected_run_id, volume, registry
            ),
        )
    )
    errors.extend(
        _rows_errors(
            document.get("paginationReceipts"),
            "reference output.paginationReceipts",
            _validate_reference_pagination_row,
        )
    )
    errors.extend(_string_list_errors(document.get("gapCodes"), "reference output.gapCodes"))
    return errors


def _validate_store_enum_column(
    connection: sqlite3.Connection,
    table: str,
    column: str,
    allowed: set[str],
    run_id: str,
    nullable: bool = False,
) -> list[str]:
    errors: list[str] = []
    for row_id, value in connection.execute(
        f'SELECT rowid, "{column}" FROM "{table}" WHERE RunId=?', (run_id,)
    ):
        if value is None and nullable:
            continue
        if value not in allowed:
            errors.append(f"{table}[rowid={row_id}].{column} has an unsupported value")
    return errors


def _validate_physical_store_rows(
    connection: sqlite3.Connection,
    run_id: str,
    output_document: dict[str, Any],
) -> list[str]:
    errors: list[str] = []
    run_row = connection.execute(
        "SELECT ExecutionStatus, Verdict FROM DiscoveryRuns WHERE RunId=?", (run_id,)
    ).fetchone()
    if run_row is None:
        return ["DiscoveryRuns has no matching run"]
    if run_row[0] not in DISCOVERY_EXECUTION_STATUSES:
        errors.append("DiscoveryRuns.ExecutionStatus has an unsupported value")
    if run_row[1] not in DISCOVERY_VERDICTS:
        errors.append("DiscoveryRuns.Verdict has an unsupported value")
    if output_document.get("executionStatus") != run_row[0]:
        errors.append("physical output executionStatus does not match DiscoveryRuns")
    if output_document.get("coverageVerdict") != run_row[1]:
        errors.append("physical output coverageVerdict does not match DiscoveryRuns")
    specifications = [
        ("DiscoveryScopes", "Kind", DISCOVERY_SCOPE_KINDS, False),
        ("DiscoveryScopes", "SourceKind", PHYSICAL_SOURCE_KINDS, True),
        ("DiscoveryScopes", "Outcome", DISCOVERY_TERMINAL_OUTCOMES, False),
        ("DiscoveryChildEnumerations", "ChildKind", DISCOVERY_SCOPE_KINDS, False),
        ("DiscoveryChildEnumerations", "Outcome", DISCOVERY_TERMINAL_OUTCOMES, False),
        ("DiscoveryExpectedChildren", "ChildKind", DISCOVERY_SCOPE_KINDS, False),
        ("DiscoveryExpectedChildren", "SourceKind", PHYSICAL_SOURCE_KINDS, True),
        ("DiscoveryAttempts", "SourceKind", PHYSICAL_SOURCE_KINDS, False),
        ("DiscoveryAttempts", "Status", DISCOVERY_ATTEMPT_STATUSES, False),
        ("DiscoveryObservations", "SourceKind", PHYSICAL_SOURCE_KINDS, False),
        ("DiscoveryGaps", "SourceKind", PHYSICAL_SOURCE_KINDS, False),
    ]
    for table, column, allowed, nullable in specifications:
        errors.extend(
            _validate_store_enum_column(connection, table, column, allowed, run_id, nullable)
        )
    return errors


def _validate_reference_store_rows(
    connection: sqlite3.Connection,
    run_id: str,
    output_document: dict[str, Any],
    volume: dict[str, Any],
    registry: dict[str, Any],
) -> list[str]:
    errors: list[str] = []
    output_reference_ids = {
        row.get("referenceObservationId")
        for row in output_document.get("references", [])
        if isinstance(row, dict)
    }
    store_reference_ids: set[str] = set()
    for observation_id, payload in connection.execute(
        "SELECT ObservationId, Json FROM ReferenceObservations WHERE RunId=?", (run_id,)
    ):
        store_reference_ids.add(str(observation_id))
        try:
            row = json.loads(payload)
        except (TypeError, json.JSONDecodeError) as error:
            errors.append(f"ReferenceObservations[{observation_id}].Json is invalid: {error}")
            continue
        errors.extend(
            _validate_reference_observation_row(
                row, f"ReferenceObservations[{observation_id}]", volume, registry
            )
        )
        if isinstance(row, dict) and row.get("referenceObservationId") != observation_id:
            errors.append(f"ReferenceObservations[{observation_id}] identity does not match Json")
    if output_reference_ids != store_reference_ids:
        errors.append("reference output observations do not match ReferenceObservations snapshot")

    output_surface_ids = {
        row.get("surfaceId")
        for row in output_document.get("denominator", [])
        if isinstance(row, dict)
    }
    store_surface_ids: set[str] = set()
    for surface_id, payload in connection.execute(
        "SELECT SurfaceId, Json FROM ReferenceDenominator WHERE RunId=?", (run_id,)
    ):
        store_surface_ids.add(str(surface_id))
        try:
            row = json.loads(payload)
        except (TypeError, json.JSONDecodeError) as error:
            errors.append(f"ReferenceDenominator[{surface_id}].Json is invalid: {error}")
            continue
        errors.extend(
            _validate_reference_denominator_row(
                row, f"ReferenceDenominator[{surface_id}]", run_id, volume, registry
            )
        )
        if isinstance(row, dict) and row.get("surfaceId") != surface_id:
            errors.append(f"ReferenceDenominator[{surface_id}] identity does not match Json")
    if output_surface_ids != store_surface_ids:
        errors.append("reference output denominator does not match ReferenceDenominator snapshot")

    output_pagination = {
        (row.get("collectionScopeKey"), row.get("pageOrdinal"))
        for row in output_document.get("paginationReceipts", [])
        if isinstance(row, dict)
    }
    store_pagination: set[tuple[Any, Any]] = set()
    for scope_key, ordinal, payload in connection.execute(
        "SELECT ScopeKey, PageOrdinal, Json FROM ReferencePaginationReceipts WHERE RunId=?", (run_id,)
    ):
        store_pagination.add((scope_key, ordinal))
        try:
            row = json.loads(payload)
        except (TypeError, json.JSONDecodeError) as error:
            errors.append(f"ReferencePaginationReceipts[{scope_key},{ordinal}].Json is invalid: {error}")
            continue
        errors.extend(
            _validate_reference_pagination_row(
                row, f"ReferencePaginationReceipts[{scope_key},{ordinal}]"
            )
        )
        if isinstance(row, dict) and (
            row.get("collectionScopeKey") != scope_key or row.get("pageOrdinal") != ordinal
        ):
            errors.append(f"ReferencePaginationReceipts[{scope_key},{ordinal}] identity does not match Json")
    if output_pagination != store_pagination:
        errors.append("reference output pagination does not match ReferencePaginationReceipts snapshot")

    output_gaps = set(output_document.get("gapCodes", []))
    store_gaps = {
        str(row[0])
        for row in connection.execute("SELECT GapCode FROM ReferenceGaps WHERE RunId=?", (run_id,))
    }
    if output_gaps != store_gaps:
        errors.append("reference output gapCodes do not match ReferenceGaps snapshot")
    return errors


def _validate_output_artifact(
    volume: dict[str, Any],
    artifact_bytes: bytes | None,
    expected_run_field: str,
    expected_run_id: str,
    registry: dict[str, Any],
) -> tuple[str | None, list[str], dict[str, Any] | None]:
    if artifact_bytes is None:
        return "ARTIFACT_EVIDENCE_MISSING", ["actual output artifact bytes are missing"], None
    actual_hash = hashlib.sha256(artifact_bytes).hexdigest()
    if volume.get("artifactHash") != actual_hash:
        return "VOLUME_HASH_MISMATCH", ["declared artifactHash does not match actual bytes"], None
    if volume.get("artifactLength") != len(artifact_bytes):
        return "VOLUME_LENGTH_MISMATCH", ["declared artifactLength does not match actual bytes"], None
    try:
        document = json.loads(artifact_bytes)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        return "VOLUME_CONTENT_MISMATCH", [f"output artifact is not valid JSON: {error}"], None
    if not isinstance(document, dict):
        return "VOLUME_CONTENT_MISMATCH", ["output artifact root must be an object"], None
    if document.get("outputVersion") != volume.get("outputVersion"):
        return "VOLUME_CONTENT_MISMATCH", ["actual outputVersion does not match volume binding"], None
    if document.get(expected_run_field) != expected_run_id:
        return "VOLUME_CONTENT_MISMATCH", ["actual acquisition run ID does not match envelope"], None
    if volume.get("outputVersion") == VOLUME_COMPATIBILITY["physicalOutput"]:
        errors = _validate_physical_output_document(document)
    elif volume.get("outputVersion") == VOLUME_COMPATIBILITY["referenceOutput"]:
        errors = _validate_reference_output_document(document, expected_run_id, volume, registry)
    else:
        errors = ["actual outputVersion has no version-bound content validator"]
    if errors:
        return "VOLUME_CONTENT_MISMATCH", errors, None
    return None, [], document


def _validate_store_artifact(
    store: dict[str, Any],
    database_bytes: bytes | None,
    expected_version: str,
    expected_schema_hash: str,
    run_id: str,
    volume: dict[str, Any],
    registry: dict[str, Any],
    reference_store: bool,
    output_document: dict[str, Any],
) -> tuple[str | None, list[str]]:
    if database_bytes is None:
        return "ARTIFACT_EVIDENCE_MISSING", ["actual SQLite store bytes are missing"]
    try:
        with closing(_sqlite_connection(database_bytes)) as connection:
            integrity = connection.execute("PRAGMA integrity_check").fetchone()
            if integrity is None or integrity[0] != "ok":
                return "STORE_INTEGRITY_FAILURE", ["SQLite integrity_check did not return ok"]
            foreign_keys = connection.execute("PRAGMA foreign_key_check").fetchall()
            if foreign_keys:
                return "STORE_INTEGRITY_FAILURE", ["SQLite foreign_key_check returned rows"]
            actual_schema_hash = hashlib.sha256(
                canonical_json_bytes(sqlite_schema_manifest(connection))
            ).hexdigest()
            if actual_schema_hash != expected_schema_hash:
                return "STORE_SCHEMA_MISMATCH", [
                    f"actual SQLite schema hash {actual_schema_hash} is not the frozen {expected_version} schema"
                ]
            if reference_store:
                row = connection.execute(
                    "SELECT ManifestJson, ManifestHash, OutputVersion, CoverageVerdict "
                    "FROM ReferenceRuns WHERE RunId=?",
                    (run_id,),
                ).fetchone()
                row_errors = _validate_reference_store_rows(
                    connection, run_id, output_document, volume, registry
                )
            else:
                row = connection.execute(
                    "SELECT ManifestJson, ManifestHash FROM DiscoveryRuns WHERE RunId=?",
                    (run_id,),
                ).fetchone()
                row_errors = _validate_physical_store_rows(connection, run_id, output_document)
    except sqlite3.DatabaseError as error:
        return "STORE_INTEGRITY_FAILURE", [f"SQLite store cannot be read: {error}"]
    if row_errors:
        return "STORE_ROW_CONTENT_MISMATCH", row_errors
    if row is None:
        return "STORE_MANIFEST_MISMATCH", ["SQLite store has no run manifest for acquisition run ID"]
    manifest_json, manifest_hash = row[0], row[1]
    if store.get("manifestHash") != manifest_hash:
        return "STORE_MANIFEST_MISMATCH", ["declared store manifest hash does not match SQLite row"]
    if discovery_hash(manifest_json) != manifest_hash:
        return "STORE_MANIFEST_MISMATCH", ["SQLite ManifestJson does not hash to ManifestHash"]
    try:
        manifest = json.loads(manifest_json)
    except json.JSONDecodeError as error:
        return "STORE_MANIFEST_MISMATCH", [f"SQLite ManifestJson is invalid: {error}"]
    if manifest.get("contractVersion") != volume.get("contractVersion"):
        return "STORE_MANIFEST_MISMATCH", ["SQLite manifest contractVersion is incompatible"]
    if manifest.get("schemaVersion") != expected_version:
        return "STORE_MANIFEST_MISMATCH", ["SQLite manifest schemaVersion is incompatible"]
    if reference_store:
        if row[2] != volume.get("outputVersion"):
            return "STORE_MANIFEST_MISMATCH", ["SQLite reference OutputVersion is incompatible"]
        if row[3] != output_document.get("coverageVerdict"):
            return "OUTPUT_STORE_MANIFEST_MISMATCH", [
                "reference output coverageVerdict does not match ReferenceRuns"
            ]
        if output_document.get("manifestHash") != manifest_hash:
            return "OUTPUT_STORE_MANIFEST_MISMATCH", [
                "reference output manifestHash does not match ReferenceRuns"
            ]
        required_bindings = {
            "scopeAuthorityHash": volume.get("scopeAuthorityHash"),
            "registryRevision": registry.get("registryRevision"),
            "registryHash": registry.get("registryHash"),
            "platformBuildRef": volume.get("platformBuild"),
            "snapshotFence": volume.get("snapshotFence"),
            "artifactRunId": run_id,
        }
    else:
        required_bindings = {
            "scopePolicyHash": volume.get("scopeAuthorityHash"),
        }
    for key, expected in required_bindings.items():
        if manifest.get(key) != expected:
            return "STORE_MANIFEST_MISMATCH", [f"SQLite manifest {key} does not match volume binding"]
    if manifest.get("productRef") != volume.get("productRef"):
        return "STORE_MANIFEST_MISMATCH", ["SQLite manifest productRef does not exactly match volume productRef"]
    return None, []


def validate_acquisition_envelope(
    envelope: Any,
    registry: dict[str, Any],
    profile: dict[str, Any],
    artifact_evidence: dict[str, bytes] | None = None,
) -> dict[str, Any]:
    if envelope is None:
        return {"verdict": "Unknown", "reasonCode": "ACQUISITION_ENVELOPE_MISSING"}
    if not isinstance(envelope, dict):
        return {"verdict": "Unknown", "reasonCode": "ACQUISITION_ENVELOPE_SHAPE_INVALID"}
    if "physicalVolume" not in envelope:
        return {"verdict": "Unknown", "reasonCode": "PHYSICAL_VOLUME_MISSING"}
    if "referenceVolume" not in envelope:
        return {"verdict": "Unknown", "reasonCode": "REFERENCE_VOLUME_MISSING"}
    envelope_errors = exact_key_errors(envelope, ACQUISITION_ENVELOPE_KEYS, "acquisitionEnvelope")
    if envelope_errors:
        return {"verdict": "Unknown", "reasonCode": "ACQUISITION_ENVELOPE_SHAPE_INVALID", "errors": envelope_errors}
    if envelope.get("outputVersion") != VOLUME_COMPATIBILITY["aggregateOutput"]:
        return {"verdict": "Unknown", "reasonCode": "UNSUPPORTED_OUTPUT_VERSION"}
    physical = envelope["physicalVolume"]
    reference = envelope["referenceVolume"]
    physical_errors = exact_key_errors(physical, BASE_VOLUME_KEYS, "physicalVolume")
    if physical_errors:
        return {"verdict": "Unknown", "reasonCode": "PHYSICAL_VOLUME_SHAPE_INVALID", "errors": physical_errors}
    reference_errors = exact_key_errors(reference, REFERENCE_VOLUME_KEYS, "referenceVolume")
    if reference_errors:
        return {"verdict": "Unknown", "reasonCode": "REFERENCE_VOLUME_SHAPE_INVALID", "errors": reference_errors}
    assert isinstance(physical, dict) and isinstance(reference, dict)
    if physical.get("outputVersion") != VOLUME_COMPATIBILITY["physicalOutput"]:
        return {"verdict": "Unknown", "reasonCode": "UNSUPPORTED_OUTPUT_VERSION"}
    if physical.get("contractVersion") != VOLUME_COMPATIBILITY["physicalContract"]:
        return {"verdict": "Unknown", "reasonCode": "UNSUPPORTED_CONTRACT_VERSION"}
    if reference.get("outputVersion") != VOLUME_COMPATIBILITY["referenceOutput"]:
        return {"verdict": "Unknown", "reasonCode": "REFERENCE_OUTPUT_VERSION_UNSUPPORTED"}
    if reference.get("contractVersion") != VOLUME_COMPATIBILITY["referenceContract"]:
        return {"verdict": "Unknown", "reasonCode": "UNSUPPORTED_CONTRACT_VERSION"}
    reason, errors = _validate_store(physical.get("store"), VOLUME_COMPATIBILITY["physicalStore"], "physical store")
    if reason:
        return {"verdict": "Unknown", "reasonCode": reason, "errors": errors}
    reason, errors = _validate_store(reference.get("store"), VOLUME_COMPATIBILITY["referenceStore"], "reference store")
    if reason:
        return {"verdict": "Unknown", "reasonCode": reason, "errors": errors}
    if reference.get("recordKind") != REFERENCE_RECORD_KIND:
        return {"verdict": "Unknown", "reasonCode": "UNKNOWN_RECORD_KIND"}
    if reference.get("sourceKinds") != SOURCE_KINDS:
        return {"verdict": "Unknown", "reasonCode": "UNKNOWN_SOURCE_KIND"}
    if reference.get("dispositions") != DISPOSITIONS:
        return {"verdict": "Unknown", "reasonCode": "UNKNOWN_DISPOSITION"}

    for volume in [physical, reference]:
        if not SHA256_RE.fullmatch(str(volume.get("artifactHash"))):
            return {"verdict": "Unknown", "reasonCode": "VOLUME_HASH_INVALID"}
        if not isinstance(volume.get("artifactLength"), int) or volume["artifactLength"] < 0:
            return {"verdict": "Unknown", "reasonCode": "VOLUME_LENGTH_INVALID"}
        if not PRODUCT_REF_RE.fullmatch(str(volume.get("productRef"))):
            return {"verdict": "Unknown", "reasonCode": "VOLUME_REF_INVALID"}

    if envelope.get("physicalArtifactHash") != physical.get("artifactHash"):
        return {"verdict": "Unknown", "reasonCode": "VOLUME_HASH_MISMATCH"}
    if envelope.get("referenceArtifactHash") != reference.get("artifactHash"):
        return {"verdict": "Unknown", "reasonCode": "VOLUME_HASH_MISMATCH"}
    if envelope.get("registryRevision") != registry.get("registryRevision") or envelope.get("registryHash") != registry.get("registryHash"):
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_BINDING_MISMATCH"}
    if reference.get("registryRevision") != registry.get("registryRevision") or reference.get("registryHash") != registry.get("registryHash"):
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_BINDING_MISMATCH"}

    for field, reason_code in [
        ("productRef", "VOLUME_REF_MISMATCH"),
        ("runId", "VOLUME_REF_MISMATCH"),
        ("scopeAuthorityHash", "VOLUME_REF_MISMATCH"),
        ("snapshotFence", "VOLUME_FENCE_MISMATCH"),
        ("platformBuild", "VOLUME_BUILD_MISMATCH"),
    ]:
        expected = envelope.get(field)
        if physical.get(field) != expected or reference.get(field) != expected:
            return {"verdict": "Unknown", "reasonCode": reason_code}
    if envelope.get("platformBuild") != profile.get("platformBuild"):
        return {"verdict": "Unknown", "reasonCode": "VOLUME_BUILD_MISMATCH"}
    if envelope.get("productRef") != profile.get("consumerProductRef"):
        return {"verdict": "Unknown", "reasonCode": "VOLUME_REF_MISMATCH"}

    evidence = artifact_evidence or {}
    output_documents: dict[str, dict[str, Any]] = {}
    for volume, evidence_key, run_field in [
        (physical, "physicalOutput", "runId"),
        (reference, "referenceOutput", "acquisitionRunId"),
    ]:
        reason, errors, document = _validate_output_artifact(
            volume,
            evidence.get(evidence_key),
            run_field,
            str(envelope.get("runId")),
            registry,
        )
        if reason:
            return {"verdict": "Unknown", "reasonCode": reason, "errors": errors}
        assert document is not None
        output_documents[evidence_key] = document

    for volume, evidence_key, output_key, store_version, schema_hash, reference_store in [
        (
            physical,
            "physicalStore",
            "physicalOutput",
            VOLUME_COMPATIBILITY["physicalStore"],
            ARTIFACT_VERIFICATION["physicalStoreSchemaHash"],
            False,
        ),
        (
            reference,
            "referenceStore",
            "referenceOutput",
            VOLUME_COMPATIBILITY["referenceStore"],
            ARTIFACT_VERIFICATION["referenceStoreSchemaHash"],
            True,
        ),
    ]:
        reason, errors = _validate_store_artifact(
            volume["store"],
            evidence.get(evidence_key),
            store_version,
            schema_hash,
            str(envelope.get("runId")),
            volume,
            registry,
            reference_store,
            output_documents[output_key],
        )
        if reason:
            return {"verdict": "Unknown", "reasonCode": reason, "errors": errors}
    return {"verdict": "Compatible", "reasonCode": "ACQUISITION_BINDING_VALID"}


def evaluate_registry_request(
    registry: dict[str, Any] | None,
    profile: dict[str, Any] | None,
    request: dict[str, Any],
    authority: dict[str, Any] | None = None,
    registry_schema: dict[str, Any] | None = None,
    registry_schema_hash: str | None = None,
    artifact_evidence: dict[str, bytes] | None = None,
) -> dict[str, Any]:
    if registry is None:
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_VOLUME_MISSING"}
    schema_errors = schema_validation_errors(registry, registry_schema) if registry_schema is not None else []
    if schema_errors:
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_SCHEMA_INVALID", "errors": schema_errors}
    errors = validate_registry(registry, authority)
    if errors:
        reason = "ALIAS_COLLISION" if any("alias normalization collision" in error for error in errors) else "REGISTRY_INVALID"
        return {"verdict": "Unknown", "reasonCode": reason, "errors": errors}
    if profile is None:
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_PROFILE_MISSING"}
    profile_errors = validate_profile(profile, registry, authority, registry_schema_hash)
    if profile_errors:
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_PROFILE_INVALID", "errors": profile_errors}

    registry_envelope = request.get("registryEnvelope")
    if registry_envelope is None:
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_VOLUME_MISSING"}
    envelope_errors = exact_key_errors(registry_envelope, REGISTRY_ENVELOPE_KEYS, "registryEnvelope")
    if envelope_errors:
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_ENVELOPE_INVALID", "errors": envelope_errors}
    assert isinstance(registry_envelope, dict)
    registry_bindings = {
        "profileSchemaVersion": profile["profileSchemaVersion"],
        "profileRevision": profile["profileRevision"],
        "profileHash": profile["profileHash"],
        "registrySchemaVersion": profile["registrySchemaVersion"],
        "registrySchemaHash": profile["registrySchemaHash"],
        "registryRevision": registry["registryRevision"],
        "registryHash": registry["registryHash"],
        "authoritySourceRef": registry["authoritySourceRef"],
        "authorityArtifactHash": registry["authorityArtifactHash"],
        "platformFamily": profile["platformFamily"],
        "platformBuild": profile["platformBuild"],
    }
    for key, expected in registry_bindings.items():
        if registry_envelope.get(key) != expected:
            reason = "SAME_REVISION_CHANGED_HASH" if key == "registryHash" and registry_envelope.get("registryRevision") == registry["registryRevision"] else "REGISTRY_PROFILE_MISMATCH"
            return {"verdict": "Unknown", "reasonCode": reason}

    build = request.get("platformBuild")
    try:
        parse_build(build)
    except ValueError:
        return {"verdict": "Unknown", "reasonCode": "UNKNOWN_PLATFORM_BUILD"}
    if build != profile["platformBuild"]:
        return {"verdict": "Unknown", "reasonCode": "INCOMPATIBLE_PLATFORM_BUILD"}
    if request.get("readerMode") == "legacy-v2-physical-only":
        return {"verdict": "Unknown", "reasonCode": "REFERENCE_COMPLETENESS_UNSUPPORTED"}

    acquisition = validate_acquisition_envelope(
        request.get("acquisitionEnvelope"), registry, profile, artifact_evidence
    )
    if acquisition["verdict"] != "Compatible":
        return acquisition

    runtime_path = request.get("runtimePath")
    if not runtime_path:
        return {"verdict": "Unknown", "reasonCode": "RUNTIME_PATH_MISSING"}
    try:
        lookup = build_lookup(registry)
        entry = lookup_runtime_path(registry, lookup, runtime_path)
    except ValueError:
        return {"verdict": "Unknown", "reasonCode": "ALIAS_COLLISION"}
    if entry is None:
        return {"verdict": "Unknown", "reasonCode": "RUNTIME_PATH_ABSENT"}
    return {
        "verdict": entry["downstreamDisposition"],
        "reasonCode": "REGISTRY_MATCH",
        "referenceId": entry["referenceId"],
        "canonicalRequestPath": entry["canonicalRequestPath"],
    }


def validate_reference_observation(
    observation: dict[str, Any], physical_inventory_keys: list[str] | None = None
) -> dict[str, Any]:
    if observation.get("recordKind", REFERENCE_RECORD_KIND) != REFERENCE_RECORD_KIND:
        return {"verdict": "Unknown", "reasonCode": "UNKNOWN_RECORD_KIND"}
    source_kind = observation.get("sourceKind")
    disposition = observation.get("disposition")
    if source_kind not in KNOWN_SOURCE_KINDS:
        return {"verdict": "Unknown", "reasonCode": "UNKNOWN_SOURCE_KIND"}
    if disposition not in KNOWN_DISPOSITIONS:
        return {"verdict": "Unknown", "reasonCode": "UNKNOWN_DISPOSITION"}
    linked_key = observation.get("linkedPhysicalCanonicalInventoryKey")
    linked_file_id = observation.get("linkedFileUniqueId")
    physical_locator = observation.get("physicalLocator")
    if disposition in NON_PHYSICAL_DISPOSITIONS:
        if linked_key or linked_file_id or physical_locator:
            return {"verdict": "Invalid", "reasonCode": "NON_PHYSICAL_IDENTITY_PRESENT"}
        return {"verdict": disposition, "reasonCode": "OBSERVATION_VALID"}
    if disposition in LINKED_PHYSICAL_DISPOSITIONS:
        if not linked_key or not linked_file_id:
            return {"verdict": "Unknown", "reasonCode": "LINKED_PHYSICAL_IDENTITY_MISSING"}
        matches = (physical_inventory_keys or []).count(linked_key)
        if matches != 1:
            return {"verdict": "Unknown", "reasonCode": "PHYSICAL_JOIN_NOT_UNIQUE"}
        return {"verdict": disposition, "reasonCode": "UNIQUE_PHYSICAL_JOIN"}
    return {"verdict": disposition, "reasonCode": "OBSERVATION_VALID"}


def _rehash_registry(registry: dict[str, Any]) -> None:
    registry["registryHash"] = object_hash(registry, "registryHash")


def apply_artifact_mutation(
    registry: dict[str, Any],
    authority: dict[str, Any],
    profile: dict[str, Any],
    mutation: dict[str, Any] | None,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    mutated_registry = copy.deepcopy(registry)
    mutated_authority = copy.deepcopy(authority)
    mutated_profile = copy.deepcopy(profile)
    if not mutation:
        return mutated_registry, mutated_authority, mutated_profile
    kind = mutation["kind"]
    if kind == "aliasCollision":
        mutated_registry["entries"][1]["aliases"].append(mutated_registry["entries"][0]["aliases"][0])
        _rehash_registry(mutated_registry)
    elif kind == "widenBuildRangeAndRehash":
        for artifact in [mutated_registry, mutated_authority]:
            artifact["platformBuildMin"] = "16.0.27606.11999"
            artifact["platformBuildMax"] = "16.0.27606.12001"
        mutated_authority["authorityArtifactHash"] = object_hash(mutated_authority, "authorityArtifactHash")
        mutated_registry["authorityArtifactHash"] = mutated_authority["authorityArtifactHash"]
        _rehash_registry(mutated_registry)
    elif kind == "reduceSourceKindsAndRehash":
        mutated_registry["consumerCompatibility"]["sourceKinds"] = ["PlatformRegistryReference"]
        _rehash_registry(mutated_registry)
    elif kind == "driftVolumeProfileAndRehash":
        mutated_registry["consumerCompatibility"]["volumeCompatibility"]["referenceOutput"] = "aspx-discovery-output/v2"
        _rehash_registry(mutated_registry)
    elif kind == "emptyFailureSemanticsAndRehash":
        mutated_registry["failureSemantics"] = {}
        _rehash_registry(mutated_registry)
    elif kind == "schemaHashDriftAndRehashProfile":
        mutated_profile["registrySchemaHash"] = "0" * 64
        mutated_profile["profileHash"] = object_hash(mutated_profile, "profileHash")
    else:
        raise ValueError(f"unknown artifact mutation: {kind}")
    return mutated_registry, mutated_authority, mutated_profile


def apply_json_operations(value: dict[str, Any], operations: list[dict[str, Any]]) -> dict[str, Any]:
    result = copy.deepcopy(value)
    for operation in operations:
        path = operation["path"]
        if not path.startswith("/"):
            raise ValueError(f"JSON operation path must be absolute: {path}")
        parts = [part.replace("~1", "/").replace("~0", "~") for part in path[1:].split("/")]
        parent: Any = result
        for part in parts[:-1]:
            parent = parent[int(part)] if isinstance(parent, list) else parent[part]
        leaf = parts[-1]
        if operation["op"] == "remove":
            if isinstance(parent, list):
                del parent[int(leaf)]
            else:
                parent.pop(leaf, None)
        elif operation["op"] in {"add", "replace"}:
            replacement = copy.deepcopy(operation.get("value"))
            if isinstance(parent, list):
                index = int(leaf)
                if operation["op"] == "add":
                    parent.insert(index, replacement)
                else:
                    parent[index] = replacement
            else:
                parent[leaf] = replacement
        else:
            raise ValueError(f"unsupported JSON operation: {operation['op']}")
    return result


def evaluate_fixture_suite(
    fixtures: dict[str, Any],
    registry: dict[str, Any],
    authority: dict[str, Any],
    profile: dict[str, Any],
    registry_schema: dict[str, Any],
    registry_schema_hash: str,
    fixture_root: Path,
) -> dict[str, Any]:
    results: list[dict[str, Any]] = []
    baseline_evidence, evidence_errors = load_fixture_artifact_evidence(
        fixtures.get("artifactBindings"), fixture_root
    )
    if evidence_errors:
        raise ValueError("fixture artifact evidence is invalid: " + "; ".join(evidence_errors))
    cases = [*fixtures["positive"], *fixtures["negative"]]
    for case in cases:
        kind = case["kind"]
        if kind == "readerRequest":
            case_registry, case_authority, case_profile = apply_artifact_mutation(
                registry, authority, profile, case.get("artifactMutation")
            )
            reader_input = apply_json_operations(
                fixtures["readerTemplate"], case.get("operations", [])
            )
            case_evidence = apply_evidence_mutation(
                baseline_evidence, case.get("evidenceMutation")
            )
            acquisition = reader_input.get("acquisitionEnvelope", {})
            for volume_name in case.get("rebindActualArtifactHash", []):
                evidence_key = f"{volume_name}Output"
                volume_key = f"{volume_name}Volume"
                actual_hash = hashlib.sha256(case_evidence[evidence_key]).hexdigest()
                acquisition[volume_key]["artifactHash"] = actual_hash
                acquisition[f"{volume_name}ArtifactHash"] = actual_hash
            for volume_name in case.get("rebindActualArtifact", []):
                evidence_key = f"{volume_name}Output"
                volume_key = f"{volume_name}Volume"
                actual_bytes = case_evidence[evidence_key]
                actual_hash = hashlib.sha256(actual_bytes).hexdigest()
                acquisition[volume_key]["artifactHash"] = actual_hash
                acquisition[volume_key]["artifactLength"] = len(actual_bytes)
                acquisition[f"{volume_name}ArtifactHash"] = actual_hash
            for store_name in case.get("rebindStoreManifestHash", []):
                evidence_key = f"{store_name}Store"
                volume_key = f"{store_name}Volume"
                acquisition[volume_key]["store"]["manifestHash"] = _read_store_manifest_hash(
                    case_evidence[evidence_key],
                    reference_store=store_name == "reference",
                    run_id=str(acquisition["runId"]),
                )
            actual = evaluate_registry_request(
                case_registry,
                case_profile,
                reader_input,
                case_authority,
                registry_schema,
                registry_schema_hash,
                case_evidence,
            )
            receipt_input = {
                "readerInput": reader_input,
                "artifactMutation": case.get("artifactMutation"),
                "evidenceMutation": case.get("evidenceMutation"),
                "artifactEvidence": artifact_evidence_summary(case_evidence),
                "mutatedAuthorityHash": case_authority.get("authorityArtifactHash"),
                "mutatedRegistryHash": case_registry.get("registryHash"),
                "mutatedProfileHash": case_profile.get("profileHash"),
            }
            input_hash = hashlib.sha256(canonical_json_bytes(receipt_input)).hexdigest()
        elif kind == "referenceObservation":
            actual = validate_reference_observation(
                case["observation"], case.get("physicalInventoryKeys")
            )
            input_hash = hashlib.sha256(canonical_json_bytes(case["observation"])).hexdigest()
        elif kind == "dedupeJoin":
            observations = case["observations"]
            rows = [
                validate_reference_observation(row, case["physicalInventoryKeys"])
                for row in observations
            ]
            physical_actions = {
                row["linkedPhysicalCanonicalInventoryKey"] for row in observations
            }
            actual = {
                "verdict": "Valid" if all(row["reasonCode"] == "UNIQUE_PHYSICAL_JOIN" for row in rows) else "Invalid",
                "reasonCode": "ONE_PHYSICAL_MANY_REFERENCES" if len(physical_actions) == 1 else "PHYSICAL_DOUBLE_COUNT",
                "referenceCount": len(rows),
                "physicalActionCount": len(physical_actions),
            }
            input_hash = hashlib.sha256(canonical_json_bytes(observations)).hexdigest()
        else:
            raise ValueError(f"unknown fixture kind: {kind}")
        passed = (
            actual.get("verdict") == case.get("expectedVerdict")
            and actual.get("reasonCode") == case.get("expectedReasonCode")
        )
        if "expectedReferenceCount" in case:
            passed = passed and actual.get("referenceCount") == case["expectedReferenceCount"]
        if "expectedPhysicalActionCount" in case:
            passed = passed and actual.get("physicalActionCount") == case["expectedPhysicalActionCount"]
        results.append(
            {
                "id": case["id"],
                "kind": kind,
                "inputHash": input_hash,
                "expectedVerdict": case.get("expectedVerdict"),
                "expectedReasonCode": case.get("expectedReasonCode"),
                "actualVerdict": actual.get("verdict"),
                "actualReasonCode": actual.get("reasonCode"),
                "artifactMutation": (
                    case.get("artifactMutation", {}).get("kind")
                    if case.get("artifactMutation")
                    else None
                ),
                "evidenceMutation": (
                    case.get("evidenceMutation", {}).get("kind")
                    if case.get("evidenceMutation")
                    else None
                ),
                "operationCount": len(case.get("operations", [])),
                "errorCount": len(actual.get("errors", [])),
                "passed": passed,
            }
        )
    return {
        "receiptSchemaVersion": "aspx-platform-registry-negative-receipts/v3",
        "registryRevision": registry["registryRevision"],
        "registryHash": registry["registryHash"],
        "profileRevision": profile["profileRevision"],
        "profileHash": profile["profileHash"],
        "registrySchemaHash": registry_schema_hash,
        "artifactEvidence": artifact_evidence_summary(baseline_evidence),
        "fixtureProvenance": fixtures.get("fixtureProvenance"),
        "caseCount": len(results),
        "passCount": sum(1 for row in results if row["passed"]),
        "results": results,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("registry", type=Path)
    parser.add_argument("--authority", type=Path, required=True)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--schema", type=Path, required=True)
    parser.add_argument("--profile-schema", type=Path)
    parser.add_argument("--fixtures", type=Path)
    parser.add_argument("--receipt-out", type=Path)
    parser.add_argument("--release-spec", type=Path)
    args = parser.parse_args()

    if args.release_spec is not None:
        configure_validation_release(load_json(args.release_spec))

    registry = load_json(args.registry)
    authority = load_json(args.authority)
    profile = load_json(args.profile)
    registry_schema = load_json(args.schema)
    registry_schema_hash = artifact_sha256(args.schema)
    errors = schema_validation_errors(registry, registry_schema)
    errors.extend(validate_registry(registry, authority))
    errors.extend(validate_profile(profile, registry, authority, registry_schema_hash))
    profile_schema = None
    if args.profile_schema:
        profile_schema = load_json(args.profile_schema)
        errors.extend(
            f"profile schema: {error}"
            for error in schema_validation_errors(profile, profile_schema)
        )
        errors.extend(
            f"profile schema resource: {error}"
            for error in validate_profile_schema_resource(
                profile_schema,
                registry_schema,
            )
        )
    if errors:
        for error in errors:
            print(error)
        return 1

    summary: dict[str, Any] = {
        "status": "valid",
        "registryRevision": registry["registryRevision"],
        "registryHash": registry["registryHash"],
        "profileRevision": profile["profileRevision"],
        "profileHash": profile["profileHash"],
        "registrySchemaHash": registry_schema_hash,
        "entryCount": registry["entryCount"],
    }
    if profile_schema is not None and args.profile_schema is not None:
        summary["profileSchemaResourceId"] = profile_schema["$id"]
        summary["profileSchemaHash"] = artifact_sha256(args.profile_schema)
    if args.fixtures:
        receipts = evaluate_fixture_suite(
            load_json(args.fixtures),
            registry,
            authority,
            profile,
            registry_schema,
            registry_schema_hash,
            args.fixtures.parent,
        )
        summary["fixtureCases"] = receipts["caseCount"]
        summary["fixturePasses"] = receipts["passCount"]
        if receipts["passCount"] != receipts["caseCount"]:
            failed = [row["id"] for row in receipts["results"] if not row["passed"]]
            print(f"fixture failures: {failed}")
            return 1
        if args.receipt_out:
            args.receipt_out.parent.mkdir(parents=True, exist_ok=True)
            args.receipt_out.write_text(
                json.dumps(receipts, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
                encoding="utf-8",
                newline="\n",
            )
    print(json.dumps(summary, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
