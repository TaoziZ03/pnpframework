#!/usr/bin/env python3
"""Strict validator and reader-shaped fail-closed evaluator for registry v1."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
from pathlib import Path
from typing import Any

from generate_registry import (
    AUTHORITY_REF,
    AUTHORITY_SCHEMA_VERSION,
    AUTHORITY_TAG,
    COMPATIBILITY_DECISION_REF,
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
    normalize_registry_key,
    object_hash,
    rule_hash,
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
    "producerRef",
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
    "producerRef",
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
        {"compatibilityDecisionRef", "recordKind", "sourceKinds", "dispositions", "volumeCompatibility"},
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
        "contractReviewRef",
        "compatibilityDecisionRef",
        "consumerCompatibilityHash",
        "recordKind",
        "sourceKinds",
        "dispositions",
        "volumeCompatibility",
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


def validate_acquisition_envelope(
    envelope: Any,
    registry: dict[str, Any],
    profile: dict[str, Any],
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
        if not GIT_SHA_RE.fullmatch(str(volume.get("producerRef"))):
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
    return {"verdict": "Compatible", "reasonCode": "ACQUISITION_BINDING_VALID"}


def evaluate_registry_request(
    registry: dict[str, Any] | None,
    profile: dict[str, Any] | None,
    request: dict[str, Any],
    authority: dict[str, Any] | None = None,
    registry_schema: dict[str, Any] | None = None,
    registry_schema_hash: str | None = None,
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
        request.get("acquisitionEnvelope"), registry, profile
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
) -> dict[str, Any]:
    results: list[dict[str, Any]] = []
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
            actual = evaluate_registry_request(
                case_registry,
                case_profile,
                reader_input,
                case_authority,
                registry_schema,
                registry_schema_hash,
            )
            receipt_input = {
                "readerInput": reader_input,
                "artifactMutation": case.get("artifactMutation"),
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
                "operationCount": len(case.get("operations", [])),
                "errorCount": len(actual.get("errors", [])),
                "passed": passed,
            }
        )
    return {
        "receiptSchemaVersion": "aspx-platform-registry-negative-receipts/v1",
        "registryRevision": registry["registryRevision"],
        "registryHash": registry["registryHash"],
        "profileRevision": profile["profileRevision"],
        "profileHash": profile["profileHash"],
        "registrySchemaHash": registry_schema_hash,
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
    args = parser.parse_args()

    registry = load_json(args.registry)
    authority = load_json(args.authority)
    profile = load_json(args.profile)
    registry_schema = load_json(args.schema)
    registry_schema_hash = artifact_sha256(args.schema)
    errors = schema_validation_errors(registry, registry_schema)
    errors.extend(validate_registry(registry, authority))
    errors.extend(validate_profile(profile, registry, authority, registry_schema_hash))
    if args.profile_schema:
        errors.extend(
            f"profile schema: {error}"
            for error in schema_validation_errors(profile, load_json(args.profile_schema))
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
    if args.fixtures:
        receipts = evaluate_fixture_suite(
            load_json(args.fixtures),
            registry,
            authority,
            profile,
            registry_schema,
            registry_schema_hash,
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
