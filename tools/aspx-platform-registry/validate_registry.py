#!/usr/bin/env python3
"""Semantic validator and fail-closed compatibility evaluator for registry v1."""

from __future__ import annotations

import argparse
import copy
import json
import re
from pathlib import Path
from typing import Any

from generate_registry import (
    AUTHORITY_REF,
    AUTHORITY_SCHEMA_VERSION,
    PLATFORM_FAMILY,
    SCHEMA_VERSION,
    normalize_registry_key,
    object_hash,
    rule_hash,
)


KNOWN_DISPOSITIONS = {
    "ReferenceOnlyAvailable",
    "ReferenceUnavailable",
    "LinkedPhysicalGhosted",
    "LinkedPhysicalCustomized",
    "VirtualHandler",
    "NonAspx",
    "Unknown",
}
KNOWN_SOURCE_KINDS = {
    "ListFormReference",
    "ListViewReference",
    "WebWelcomePageReference",
    "PlatformRegistryReference",
    "RuntimeRequestReference",
}
NON_PHYSICAL_DISPOSITIONS = {
    "ReferenceOnlyAvailable",
    "ReferenceUnavailable",
    "VirtualHandler",
}
LINKED_PHYSICAL_DISPOSITIONS = {
    "LinkedPhysicalGhosted",
    "LinkedPhysicalCustomized",
}
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
GIT_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
BUILD_RE = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$")


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def parse_build(value: str) -> tuple[int, int, int, int]:
    if not isinstance(value, str) or not BUILD_RE.fullmatch(value):
        raise ValueError("invalid platform build")
    return tuple(int(part) for part in value.split("."))  # type: ignore[return-value]


def validate_authority(authority: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    required = {
        "authoritySchemaVersion",
        "authorityArtifactHash",
        "authorityKind",
        "authoritySourceRef",
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
    if authority["authoritySchemaVersion"] != AUTHORITY_SCHEMA_VERSION:
        errors.append("authoritySchemaVersion is not supported")
    if authority["authoritySourceRef"] != AUTHORITY_REF:
        errors.append("authoritySourceRef is not the frozen authority")
    expected_hash = object_hash(authority, "authorityArtifactHash")
    if authority["authorityArtifactHash"] != expected_hash:
        errors.append("authorityArtifactHash mismatch")
    if not authority["deployRows"]:
        errors.append("authority deployRows must be non-empty")
    manifest_rows = authority["sourceArtifacts"].get("shippingManifests", [])
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
        "authorityArtifactHash",
        "reviewRef",
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

    if registry["registrySchemaVersion"] != SCHEMA_VERSION:
        errors.append("registrySchemaVersion is not supported")
    if registry["authoritySourceRef"] != AUTHORITY_REF:
        errors.append("authoritySourceRef is not the frozen authority")
    if registry["platformFamily"] != PLATFORM_FAMILY:
        errors.append("platformFamily is not supported")
    if not GIT_SHA_RE.fullmatch(str(registry["authoritySourceRef"])):
        errors.append("authoritySourceRef is not a full Git SHA")
    if not SHA256_RE.fullmatch(str(registry["authorityArtifactHash"])):
        errors.append("authorityArtifactHash is not SHA-256")
    if not SHA256_RE.fullmatch(str(registry["registryHash"])):
        errors.append("registryHash is not SHA-256")
    elif registry["registryHash"] != object_hash(registry, "registryHash"):
        errors.append("registryHash mismatch")

    try:
        minimum = parse_build(registry["platformBuildMin"])
        maximum = parse_build(registry["platformBuildMax"])
        if minimum > maximum:
            errors.append("platform build range is reversed")
    except ValueError as error:
        errors.append(str(error))

    entries = registry["entries"]
    if registry["entryCount"] != len(entries):
        errors.append("entryCount does not match entries length")
    if not entries:
        errors.append("entries must be non-empty")

    rules = {
        rule.get("applicabilityRuleId"): rule
        for rule in registry["applicabilityRules"]
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
        reference_id = entry.get("referenceId")
        if not reference_id or reference_id in reference_ids:
            errors.append(f"{prefix}: duplicate or missing referenceId")
        reference_ids.add(reference_id)
        disposition = entry.get("downstreamDisposition")
        if disposition not in KNOWN_DISPOSITIONS:
            errors.append(f"{prefix}: unknown downstreamDisposition")
        canonical_path = entry.get("canonicalRequestPath")
        if not isinstance(canonical_path, str) or not canonical_path.casefold().startswith(
            "/_layouts/15/"
        ):
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
                errors.append(
                    f"alias normalization collision: {path} maps to {previous} and {reference_id}"
                )
            explicit_path_owners[key] = reference_id

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

    compatibility = registry["consumerCompatibility"]
    disposition_map = compatibility.get("dispositions", {})
    missing_dispositions = sorted(KNOWN_DISPOSITIONS - set(disposition_map))
    if missing_dispositions:
        errors.append(f"consumer mapping missing dispositions: {missing_dispositions}")
    if "PlatformRegistryReference" not in compatibility.get("sourceKinds", []):
        errors.append("consumer mapping lacks PlatformRegistryReference")

    for key, value in registry["failureSemantics"].items():
        if value != "Unknown":
            errors.append(f"failureSemantics.{key} must fail closed to Unknown")

    if authority is not None:
        errors.extend(validate_authority(authority))
        if registry["authorityArtifactHash"] != authority.get("authorityArtifactHash"):
            errors.append("registry authorityArtifactHash does not match authority volume")
        if registry["authoritySourceRef"] != authority.get("authoritySourceRef"):
            errors.append("registry authoritySourceRef does not match authority volume")
        if registry["platformBuildMin"] != authority.get("platformBuildMin"):
            errors.append("registry platformBuildMin does not match authority volume")
        if registry["platformBuildMax"] != authority.get("platformBuildMax"):
            errors.append("registry platformBuildMax does not match authority volume")

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


def evaluate_registry_request(
    registry: dict[str, Any] | None,
    request: dict[str, Any],
) -> dict[str, Any]:
    if registry is None or not request.get("registryVolumePresent", True):
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_VOLUME_MISSING"}
    errors = validate_registry(registry)
    if errors:
        reason = (
            "ALIAS_COLLISION"
            if any("alias normalization collision" in error for error in errors)
            else "REGISTRY_INVALID"
        )
        return {"verdict": "Unknown", "reasonCode": reason, "errors": errors}
    build = request.get("platformBuild")
    try:
        parsed_build = parse_build(build)
    except ValueError:
        return {"verdict": "Unknown", "reasonCode": "UNKNOWN_PLATFORM_BUILD"}
    if not (
        parse_build(registry["platformBuildMin"])
        <= parsed_build
        <= parse_build(registry["platformBuildMax"])
    ):
        return {"verdict": "Unknown", "reasonCode": "INCOMPATIBLE_PLATFORM_BUILD"}

    declared_revision = request.get("registryRevision")
    declared_hash = request.get("registryHash")
    if declared_revision != registry["registryRevision"]:
        return {"verdict": "Unknown", "reasonCode": "REGISTRY_REVISION_MISMATCH"}
    if declared_hash != registry["registryHash"]:
        reason = (
            "SAME_REVISION_CHANGED_HASH"
            if declared_revision == registry["registryRevision"]
            else "REGISTRY_HASH_MISMATCH"
        )
        return {"verdict": "Unknown", "reasonCode": reason}

    if request.get("readerMode") == "legacy-v2-physical-only":
        return {"verdict": "Unknown", "reasonCode": "REFERENCE_COMPLETENESS_UNSUPPORTED"}
    if request.get("referenceOutputVersion", "aspx-reference-output/v1") != (
        "aspx-reference-output/v1"
    ):
        return {"verdict": "Unknown", "reasonCode": "REFERENCE_OUTPUT_VERSION_UNSUPPORTED"}
    if request.get("mixedBinding"):
        return {"verdict": "Unknown", "reasonCode": "BINDING_MISMATCH"}
    if request.get("preCompanionV2Incompatible"):
        return {"verdict": "Unknown", "reasonCode": "HISTORICAL_COMPOSITION_INCOMPATIBLE"}

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


def apply_fixture_mutation(
    registry: dict[str, Any], mutation: dict[str, Any] | None
) -> dict[str, Any]:
    mutated = copy.deepcopy(registry)
    if not mutation:
        return mutated
    kind = mutation["kind"]
    if kind == "aliasCollision":
        mutated["entries"][1]["aliases"].append(mutated["entries"][0]["aliases"][0])
    elif kind == "registryHashDrift":
        mutated["registryHash"] = "0" * 64
    elif kind == "unknownDisposition":
        mutated["entries"][0]["downstreamDisposition"] = "FutureDisposition"
        mutated["registryHash"] = object_hash(mutated, "registryHash")
    elif kind == "referenceTableUnderV2":
        mutated["consumerCompatibility"]["volumeCompatibility"]["referenceVolume"] = (
            "aspx-discovery-output/v2"
        )
        mutated["registryHash"] = object_hash(mutated, "registryHash")
    else:
        raise ValueError(f"unknown fixture mutation: {kind}")
    return mutated


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("registry", type=Path)
    parser.add_argument("--authority", type=Path)
    args = parser.parse_args()
    registry = load_json(args.registry)
    authority = load_json(args.authority) if args.authority else None
    errors = validate_registry(registry, authority)
    if errors:
        for error in errors:
            print(error)
        return 1
    print(
        json.dumps(
            {
                "status": "valid",
                "registryRevision": registry["registryRevision"],
                "registryHash": registry["registryHash"],
                "entryCount": registry["entryCount"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
