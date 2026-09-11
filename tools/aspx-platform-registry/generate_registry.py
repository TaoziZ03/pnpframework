#!/usr/bin/env python3
"""Generate the CCD setup/virtual ASPX registry from frozen SPO.Core manifests.

The generator intentionally reads only a caller-selected SPO.Core Git ref. It
does not accept Assessment output, tenant observations, or CUPCollect paths as
input. The deployed destination rows in otools/deploy/*.xml are the finite
setup-artifact authority. FilesToRedirect.sts.xms is a second, bounded authority
for legacy LCID aliases handled by SPLayoutsMappedFile.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import sqlite3
import subprocess
from collections import defaultdict
from contextlib import closing
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


SCHEMA_VERSION = "aspx-platform-registry/v1"
AUTHORITY_SCHEMA_VERSION = "aspx-platform-authority/v1"
PROFILE_SCHEMA_VERSION = "aspx-platform-registry-profile/v2"
REGISTRY_REVISION = "spo-online-16.0.27708.12757-r1"
PROFILE_REVISION = "spo-online-16.0.27708.12757-profile-r1"
PROFILE_SCHEMA_RESOURCE_ID = (
    "urn:ccd:pnp:aspx-platform-registry-profile:"
    "spo-online-16.0.27708.12757:r1"
)
PLATFORM_FAMILY = "SharePointOnline-16"
PLATFORM_BUILD = "16.0.27708.12757"
CONSUMER_PRODUCT_ID = "pnp/assessment"
CONSUMER_SOURCE_REF = "3012555317d5a8ee981b9e103206f3f0680333d8"
CONSUMER_PRODUCT_REF = f"{CONSUMER_PRODUCT_ID}@{CONSUMER_SOURCE_REF}"
AUTHORITY_REF = "1826a78bef6194afb25edc44b9abf61b7798de0a"
AUTHORITY_TAG = "build/main/16.0.27708.12757"
CONTRACT_REVIEW_REF = (
    "CCD-394#document-aspx-surface-applicability-denominator-v3"
    "@7d44f61d-919e-478a-ab8e-bb4c8f9b4b4a"
)
COMPATIBILITY_DECISION_REF = "CCD-411:approve_with_changes"
DEFAULT_INDEPENDENT_REVIEW_REF = "CCD-502"
EXPECTED_AUTHORITY_ARTIFACT_HASH = (
    "9d2bef816a17fb0e0da93c53620e647e97acbfae1d61a58fb59b2d574b2d3af8"
)
EXPECTED_REGISTRY_HASH = (
    "a9bf07d1e45cab08583f3691b77fe91d06e0de4757812cbdc7023ba4c92b953c"
)
EXPECTED_REGISTRY_SCHEMA_HASH = (
    "d1fbce7acf41c27d78cd68222f141d311da3ce8bfa006b57c4c5d742237d6ac6"
)
EXPECTED_CONSUMER_COMPATIBILITY_HASH = (
    "7a38a2b1e643637d2e6a8e34d41e3784416238519bc99e526d74ac4b65435e7a"
)
EXPECTED_PROFILE_HASH = (
    "b73d44d9a98e8aeb810e98a8e546975150641f9e86be0bf5f2f7200885f1726b"
)
ARTIFACT_STEM = f"spo-online-{PLATFORM_BUILD}"
RELEASE_SPEC_VERSION = "aspx-platform-registry-release-spec/v1"
INCLUDE_VIRTUAL_SOURCE_PROVENANCE = False

REFERENCE_RECORD_KIND = "AspxReferenceObservation"
SOURCE_KINDS = [
    "ListFormReference",
    "ListViewReference",
    "WebWelcomePageReference",
    "PlatformRegistryReference",
    "RuntimeRequestReference",
]
DISPOSITIONS = [
    "ReferenceOnlyAvailable",
    "ReferenceUnavailable",
    "LinkedPhysicalGhosted",
    "LinkedPhysicalCustomized",
    "VirtualHandler",
    "NonAspx",
    "Unknown",
]
VOLUME_COMPATIBILITY = {
    "physicalOutput": "aspx-discovery-output/v2",
    "physicalContract": "aspx-discovery/v2",
    "physicalStore": "aspx-discovery-sqlite/v2",
    "referenceOutput": "aspx-reference-output/v1",
    "referenceContract": "aspx-reference/v1",
    "referenceStore": "aspx-reference-sqlite/v1",
    "aggregateOutput": "aspx-acquisition-verdict/v1",
}
ARTIFACT_VERIFICATION = {
    "actualOutputBytes": "Required",
    "actualOutputLength": "Required",
    "jsonOutputVersion": "Exact",
    "jsonTypedContent": "ExactVersionedConformance",
    "acquisitionRunId": "Exact",
    "sqliteIntegrityCheck": "Required",
    "sqliteSchemaManifestAlgorithm": "sqlite-schema-manifest/v2",
    "physicalStoreSchemaHash": "899aa84b3c1786e1e4754d23b943b4609d23dd4b19d34819c1b7e80ea1f4e504",
    "referenceStoreSchemaHash": "73eabd7a2001f7dbaaafa48db4f0c6489b57305a630b53aa2194d1c8cb9af5ec",
    "sqliteTypedRows": "ExactVersionedConformance",
    "sqliteRunManifestHash": "Required",
    "outputStoreManifestBinding": "Required",
}
FAILURE_SEMANTICS = {
    "unknownBuild": "Unknown",
    "missingRegistryVolume": "Unknown",
    "registryProfileMismatch": "Unknown",
    "sameRevisionChangedHash": "Unknown",
    "aliasCollision": "Unknown",
    "runtimePathAbsent": "Unknown",
    "unknownSourceKindOrDisposition": "Unknown",
    "missingAcquisitionEnvelope": "Unknown",
    "missingPhysicalVolume": "Unknown",
    "missingReferenceVolume": "Unknown",
    "unsupportedOutputVersion": "Unknown",
    "unsupportedContractVersion": "Unknown",
    "unsupportedStoreVersion": "Unknown",
    "missingArtifactEvidence": "Unknown",
    "volumeHashMismatch": "Unknown",
    "volumeLengthMismatch": "Unknown",
    "volumeContentMismatch": "Unknown",
    "storeIntegrityFailure": "Unknown",
    "storeSchemaMismatch": "Unknown",
    "storeRowContentMismatch": "Unknown",
    "storeManifestMismatch": "Unknown",
    "outputStoreManifestMismatch": "Unknown",
    "volumeRefMismatch": "Unknown",
    "volumeFenceMismatch": "Unknown",
    "volumeBuildMismatch": "Unknown",
    "bindingMismatch": "Unknown",
}

DEPLOY_PATHSPEC = "otools/deploy/*.xml"
EQUIVALENCE_ADMISSION_MODE = "cross-build-equivalence-certificate/v1"
REDIRECT_MAP_PATH = "sts/template/sts/layouts/FilesToRedirect.sts.xms"
VIRTUAL_HANDLER_SOURCE_PATH = "sts/stsom/ApplicationRuntime/spvirtualpathprovider.cs"
VIRTUAL_FILE_SOURCE_PATH = "sts/stsom/ApplicationRuntime/spvirtualfile.cs"

DEST_RE = re.compile(r'dest="([^"]+)"', re.IGNORECASE)
LAYOUT_DEST_RE = re.compile(
    r"^FILES\\Program Files\\Common Files\\Microsoft Shared(?: Debug)?\\"
    r"Web Server Extensions\\16\\TEMPLATE\\LAYOUTS\\(.+\.aspx)$",
    re.IGNORECASE,
)
GREP_LINE_RE = re.compile(r"^[0-9a-f]{40}:(.*?):([0-9]+):(.*)$")


def configure_release(spec: dict[str, Any]) -> None:
    """Select one immutable exact-build release without changing the legacy default."""
    required = {
        "releaseSpecVersion",
        "platformBuild",
        "registryRevision",
        "profileRevision",
        "profileSchemaResourceId",
        "authorityRef",
        "authorityTag",
        "artifactStem",
        "independentReviewRef",
        "expectedAuthorityArtifactHash",
        "expectedRegistryHash",
        "expectedRegistrySchemaHash",
        "expectedConsumerCompatibilityHash",
        "expectedProfileHash",
    }
    missing = sorted(required - set(spec))
    extra = sorted(set(spec) - required - {"equivalenceProof"})
    if missing or extra:
        raise ValueError(
            f"release spec keys are not closed: missing={missing}, extra={extra}"
        )
    if spec["releaseSpecVersion"] != RELEASE_SPEC_VERSION:
        raise ValueError("unsupported release spec version")
    build = str(spec["platformBuild"])
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+", build):
        raise ValueError("release spec platformBuild is invalid")
    artifact_stem = str(spec["artifactStem"])
    if artifact_stem != f"spo-online-{build}":
        raise ValueError("release spec artifactStem is not build-derived")
    if spec["registryRevision"] != f"spo-online-{build}-r1":
        raise ValueError("release spec registryRevision is not exact-build r1")
    if spec["profileRevision"] != f"spo-online-{build}-profile-r1":
        raise ValueError("release spec profileRevision is not exact-build profile r1")
    expected_resource_id = (
        "urn:ccd:pnp:aspx-platform-registry-profile:"
        f"spo-online-{build}:r1"
    )
    if spec["profileSchemaResourceId"] != expected_resource_id:
        raise ValueError("release spec profileSchemaResourceId is not build-derived")
    if not re.fullmatch(r"[0-9a-f]{40}", str(spec["authorityRef"])):
        raise ValueError("release spec authorityRef is not a full Git commit")
    if not str(spec["authorityTag"]).endswith(f"/{build}"):
        raise ValueError("release spec authorityTag is not build-bound")
    for key in [
        "expectedAuthorityArtifactHash",
        "expectedRegistryHash",
        "expectedRegistrySchemaHash",
        "expectedConsumerCompatibilityHash",
        "expectedProfileHash",
    ]:
        if not re.fullmatch(r"[0-9a-f]{64}", str(spec[key])):
            raise ValueError(f"release spec {key} is not SHA-256")
    if not str(spec["independentReviewRef"]).strip():
        raise ValueError("release spec independentReviewRef is empty")

    equivalence = spec.get("equivalenceProof")
    if equivalence is not None:
        if not isinstance(equivalence, dict):
            raise ValueError("release spec equivalenceProof must be an object")
        expected_keys = {
            "admissionMode",
            "registryRevision",
            "profileRevision",
            "registryArtifactStem",
            "registrySchemaFile",
            "profileSchemaFile",
            "profileSchemaResourceId",
            "expectedRegistryHash",
            "expectedRegistrySchemaHash",
            "expectedProfileHash",
            "expectedProfileSchemaHash",
        }
        missing_equivalence = sorted(expected_keys - set(equivalence))
        extra_equivalence = sorted(set(equivalence) - expected_keys)
        if missing_equivalence or extra_equivalence:
            raise ValueError(
                "release spec equivalenceProof keys are not closed: "
                f"missing={missing_equivalence}, extra={extra_equivalence}"
            )
        if equivalence["admissionMode"] != EQUIVALENCE_ADMISSION_MODE:
            raise ValueError("release spec equivalenceProof admissionMode is invalid")
        expected_stem = f"spo-online-{build}-equivalence"
        if equivalence["registryArtifactStem"] != expected_stem:
            raise ValueError("release spec equivalence registryArtifactStem is not build-derived")
        if equivalence["registryRevision"] != f"{expected_stem}-r1":
            raise ValueError("release spec equivalence registryRevision is not exact-build r1")
        if equivalence["profileRevision"] != f"{expected_stem}-profile-r1":
            raise ValueError("release spec equivalence profileRevision is not exact-build profile r1")
        if equivalence["registrySchemaFile"] != "aspx-platform-registry-equivalence.schema.json":
            raise ValueError("release spec equivalence registrySchemaFile is invalid")
        if equivalence["profileSchemaFile"] != "aspx-platform-registry-equivalence-profile.schema.json":
            raise ValueError("release spec equivalence profileSchemaFile is invalid")
        expected_equivalence_resource_id = (
            "urn:ccd:pnp:aspx-platform-registry-profile:"
            f"{expected_stem}:r1"
        )
        if equivalence["profileSchemaResourceId"] != expected_equivalence_resource_id:
            raise ValueError("release spec equivalence profileSchemaResourceId is not build-derived")
        for key in [
            "expectedRegistryHash",
            "expectedRegistrySchemaHash",
            "expectedProfileHash",
            "expectedProfileSchemaHash",
        ]:
            if not re.fullmatch(r"[0-9a-f]{64}", str(equivalence[key])):
                raise ValueError(f"release spec equivalenceProof {key} is not SHA-256")

    global PLATFORM_BUILD, REGISTRY_REVISION, PROFILE_REVISION
    global PROFILE_SCHEMA_RESOURCE_ID, AUTHORITY_REF, AUTHORITY_TAG
    global ARTIFACT_STEM, DEFAULT_INDEPENDENT_REVIEW_REF
    global INCLUDE_VIRTUAL_SOURCE_PROVENANCE
    global EXPECTED_AUTHORITY_ARTIFACT_HASH, EXPECTED_REGISTRY_HASH
    global EXPECTED_REGISTRY_SCHEMA_HASH, EXPECTED_CONSUMER_COMPATIBILITY_HASH
    global EXPECTED_PROFILE_HASH

    PLATFORM_BUILD = build
    REGISTRY_REVISION = str(spec["registryRevision"])
    PROFILE_REVISION = str(spec["profileRevision"])
    PROFILE_SCHEMA_RESOURCE_ID = str(spec["profileSchemaResourceId"])
    AUTHORITY_REF = str(spec["authorityRef"])
    AUTHORITY_TAG = str(spec["authorityTag"])
    ARTIFACT_STEM = artifact_stem
    DEFAULT_INDEPENDENT_REVIEW_REF = str(spec["independentReviewRef"])
    INCLUDE_VIRTUAL_SOURCE_PROVENANCE = True
    EXPECTED_AUTHORITY_ARTIFACT_HASH = str(spec["expectedAuthorityArtifactHash"])
    EXPECTED_REGISTRY_HASH = str(spec["expectedRegistryHash"])
    EXPECTED_REGISTRY_SCHEMA_HASH = str(spec["expectedRegistrySchemaHash"])
    EXPECTED_CONSUMER_COMPATIBILITY_HASH = str(
        spec["expectedConsumerCompatibilityHash"]
    )
    EXPECTED_PROFILE_HASH = str(spec["expectedProfileHash"])


def canonical_json_bytes(value: Any) -> bytes:
    return json.dumps(
        value,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")


def object_hash(value: dict[str, Any], hash_field: str) -> str:
    material = copy.deepcopy(value)
    material.pop(hash_field, None)
    return hashlib.sha256(canonical_json_bytes(material)).hexdigest()


def artifact_sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def discovery_hash(*values: str | None) -> str:
    canonical = "|".join(
        f"{len(value or '')}:{value or ''}" for value in values
    )
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def normalize_sql_definition(sql: str | None) -> str | None:
    """Normalize supported producer DDL without rewriting quoted SQL content.

    The v2 fingerprint is deliberately fail-closed rather than a general SQL
    equivalence algorithm. It normalizes line endings and collapses whitespace
    only outside string/identifier quotes. Keyword case, punctuation, quoted
    identifiers and literal values remain exact.
    """
    if sql is None:
        return None
    source = sql.replace("\r\n", "\n").replace("\r", "\n").strip()
    normalized: list[str] = []
    quote: str | None = None
    pending_space = False
    index = 0
    while index < len(source):
        character = source[index]
        if quote is not None:
            normalized.append(character)
            if quote == "[":
                if character == "]":
                    if index + 1 < len(source) and source[index + 1] == "]":
                        normalized.append(source[index + 1])
                        index += 1
                    else:
                        quote = None
            elif character == quote:
                if index + 1 < len(source) and source[index + 1] == quote:
                    normalized.append(source[index + 1])
                    index += 1
                else:
                    quote = None
        elif character in {"'", '"', "`", "["}:
            if pending_space and normalized:
                normalized.append(" ")
            pending_space = False
            quote = character
            normalized.append(character)
        elif character.isspace():
            pending_space = True
        else:
            if pending_space and normalized:
                normalized.append(" ")
            pending_space = False
            normalized.append(character)
        index += 1
    return "".join(normalized)


def sqlite_where_predicate(definition_sql: str | None) -> str | None:
    normalized = normalize_sql_definition(definition_sql)
    if normalized is None:
        return None
    quote: str | None = None
    index = 0
    while index < len(normalized):
        character = normalized[index]
        if quote is not None:
            if quote == "[":
                if character == "]":
                    if index + 1 < len(normalized) and normalized[index + 1] == "]":
                        index += 1
                    else:
                        quote = None
            elif character == quote:
                if index + 1 < len(normalized) and normalized[index + 1] == quote:
                    index += 1
                else:
                    quote = None
        elif character in {"'", '"', "`", "["}:
            quote = character
        elif normalized[index : index + 5].casefold() == "where":
            previous = normalized[index - 1] if index else " "
            following = normalized[index + 5] if index + 5 < len(normalized) else " "
            if not (previous.isalnum() or previous == "_") and not (
                following.isalnum() or following == "_"
            ):
                return normalized[index + 5 :].strip()
        index += 1
    return None


def sqlite_schema_manifest(connection: sqlite3.Connection) -> dict[str, Any]:
    definitions = {
        (row[0], row[1]): normalize_sql_definition(row[2])
        for row in connection.execute(
            "SELECT type, name, sql FROM sqlite_schema "
            "WHERE type IN ('table','index') AND name NOT LIKE 'sqlite_%'"
        )
    }
    table_names = [
        row[0]
        for row in connection.execute(
            "SELECT name FROM sqlite_master "
            "WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
        )
    ]
    tables: list[dict[str, Any]] = []
    indexes: list[dict[str, Any]] = []
    for table_name in table_names:
        quoted_table = '"' + table_name.replace('"', '""') + '"'
        columns = [
            {
                "name": row[1],
                "type": (row[2] or "").upper(),
                "notNull": bool(row[3]),
                "defaultValue": row[4],
                "primaryKeyOrdinal": row[5],
                "hidden": row[6],
            }
            for row in connection.execute(f"PRAGMA table_xinfo({quoted_table})")
        ]
        foreign_keys = [
            {
                "id": row[0],
                "sequence": row[1],
                "table": row[2],
                "from": row[3],
                "to": row[4],
                "onUpdate": row[5],
                "onDelete": row[6],
                "match": row[7],
            }
            for row in connection.execute(f"PRAGMA foreign_key_list({quoted_table})")
        ]
        tables.append(
            {
                "name": table_name,
                "definitionSql": definitions.get(("table", table_name)),
                "columns": columns,
                "foreignKeys": foreign_keys,
            }
        )
        for index_row in connection.execute(f"PRAGMA index_list({quoted_table})"):
            index_name = index_row[1]
            quoted_index = '"' + index_name.replace('"', '""') + '"'
            index_columns = [
                {
                    "sequence": row[0],
                    "columnId": row[1],
                    "name": row[2],
                    "descending": bool(row[3]),
                    "collation": row[4],
                    "key": bool(row[5]),
                }
                for row in connection.execute(f"PRAGMA index_xinfo({quoted_index})")
            ]
            indexes.append(
                {
                    "table": table_name,
                    "name": index_name,
                    "unique": bool(index_row[2]),
                    "origin": index_row[3],
                    "partial": bool(index_row[4]),
                    "definitionSql": definitions.get(("index", index_name)),
                    "wherePredicateSql": sqlite_where_predicate(
                        definitions.get(("index", index_name))
                    ),
                    "columns": index_columns,
                }
            )
    indexes.sort(key=lambda row: (row["table"], row["name"]))
    return {
        "algorithm": "sqlite-schema-manifest/v2",
        "tables": tables,
        "indexes": indexes,
    }


def sqlite_schema_hash(path: Path) -> str:
    uri = path.resolve().as_uri() + "?mode=ro"
    with closing(sqlite3.connect(uri, uri=True)) as connection:
        return hashlib.sha256(
            canonical_json_bytes(sqlite_schema_manifest(connection))
        ).hexdigest()


def canonical_utc_from_epoch(epoch_text: str) -> str:
    if not re.fullmatch(r"[0-9]+", epoch_text):
        raise ValueError("authority commit epoch is invalid")
    instant = datetime.fromtimestamp(int(epoch_text), tz=timezone.utc)
    return instant.isoformat(timespec="seconds").replace("+00:00", "Z")


def rule_hash(value: dict[str, Any]) -> str:
    material = copy.deepcopy(value)
    material.pop("applicabilityRuleHash", None)
    return hashlib.sha256(canonical_json_bytes(material)).hexdigest()


def run_git(git_executable: str, repo: str, args: Iterable[str]) -> str:
    command = [git_executable, "-C", repo, *args]
    completed = subprocess.run(
        command,
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="strict",
    )
    if completed.returncode != 0:
        raise RuntimeError(
            f"git command failed ({completed.returncode}): {' '.join(command)}\n"
            f"{completed.stderr.strip()}"
        )
    return completed.stdout


def parse_tree_blobs(tree_output: str) -> dict[str, str]:
    blobs: dict[str, str] = {}
    for line in tree_output.splitlines():
        metadata, path = line.split("\t", 1)
        _mode, kind, object_id = metadata.split(" ", 2)
        if kind == "blob":
            blobs[path] = object_id
    return blobs


def parse_deploy_input_paths(grep_output: str, ref: str) -> list[str]:
    """Parse the exact path denominator selected by Git's deploy pathspec."""
    prefix = f"{ref}:"
    paths: list[str] = []
    for raw_line in grep_output.splitlines():
        if not raw_line.startswith(prefix):
            raise ValueError(f"unexpected deploy input line: {raw_line}")
        path = raw_line[len(prefix):]
        if not path.startswith("otools/deploy/") or not path.endswith(".xml"):
            raise ValueError(f"unexpected deploy input path: {path}")
        paths.append(path)
    unique = sorted(set(paths))
    if len(unique) != len(paths):
        raise ValueError("deploy input enumeration returned duplicate paths")
    if not unique:
        raise ValueError("the frozen deploy authority returned no XML inputs")
    return unique


def enumerate_deploy_input_paths(
    git_executable: str,
    repo: str,
    ref: str,
) -> list[str]:
    """Instrument the generator's recursive Git pathspec selection.

    Git's ``otools/deploy/*.xml`` pathspec also selects nested files for
    ``git grep``.  The proof and generator call this one enumerator so their
    closure denominator cannot drift.
    """
    return parse_deploy_input_paths(
        run_git(
            git_executable,
            repo,
            ["grep", "-l", "-e", "", ref, "--", DEPLOY_PATHSPEC],
        ),
        ref,
    )


def extract_deploy_rows(grep_output: str) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for raw_line in grep_output.splitlines():
        match = GREP_LINE_RE.match(raw_line)
        if not match:
            raise ValueError(f"unexpected git grep line: {raw_line}")
        manifest_path, line_text, xml_text = match.groups()
        dest_match = DEST_RE.search(xml_text)
        if not dest_match:
            continue
        destination = dest_match.group(1)
        layout_match = LAYOUT_DEST_RE.match(destination)
        if not layout_match:
            continue
        relative_path = layout_match.group(1).replace("\\", "/")
        rows.append(
            {
                "manifestPath": manifest_path,
                "manifestLine": int(line_text),
                "destination": destination,
                "relativeLayoutPath": relative_path,
                "flavor": "Debug" if "Microsoft Shared Debug" in destination else "Ship",
            }
        )
    if not rows:
        raise ValueError("the frozen deploy authority returned no ASPX layout rows")
    return rows


def parse_redirect_map(xml_text: str) -> dict[str, dict[str, Any]]:
    aliases: dict[str, dict[str, Any]] = {}
    for attrs, file_name in re.findall(
        r"<file(?P<attrs>[^>]*)>(?P<name>[^<]+)</file>", xml_text, re.IGNORECASE
    ):
        name = file_name.strip().replace("\\", "/")
        key = name.casefold()
        if key in aliases:
            raise ValueError(f"duplicate redirect-map entry: {name}")
        aliases[key] = {
            "fileName": name,
            "serverTransferOnPost": bool(
                re.search(r'serverTransferOnPost\s*=\s*"true"', attrs, re.IGNORECASE)
            ),
        }
    if not aliases:
        raise ValueError("FilesToRedirect.sts.xms returned no alias entries")
    return aliases


def normalize_registry_key(path: str) -> str:
    """Normalize an explicit registry path; request URL parsing stays consumer-side."""

    if not isinstance(path, str) or not path.startswith("/"):
        raise ValueError("registry path must be an absolute server-relative path")
    if "?" in path or "#" in path or "\\" in path:
        raise ValueError("registry path cannot contain query, fragment, or backslash")
    if "%2f" in path.casefold() or "%5c" in path.casefold():
        raise ValueError("encoded separators are not allowed")
    segments = path.split("/")
    if any(segment in (".", "..") for segment in segments):
        raise ValueError("dot segments are not allowed")
    while "//" in path:
        path = path.replace("//", "/")
    folded = path.casefold()
    if folded.startswith("/_layouts/") and not folded.startswith("/_layouts/15/"):
        path = "/_layouts/15/" + path[len("/_layouts/") :]
    return path.casefold()


def reference_id(canonical_request_path: str) -> str:
    digest = hashlib.sha256(canonical_request_path.casefold().encode("utf-8")).hexdigest()
    return f"spo-layouts-{digest[:32]}"


def create_rule() -> dict[str, Any]:
    rule = {
        "applicabilityRuleId": "spo.setup.layouts.shipping-manifest.v1",
        "applicabilityRuleVersion": "1",
        "applicabilityRuleHash": "",
        "reviewRef": CONTRACT_REVIEW_REF,
        "definition": {
            "admitWhen": (
                "The frozen release shipping manifest contains a File destination under "
                "Web Server Extensions\\16\\TEMPLATE\\LAYOUTS ending in .aspx."
            ),
            "identityAxes": [
                "requestReferenceIdentity",
                "setupArtifactIdentity",
                "virtualHandlerIdentity",
                "ghostedPhysicalFileIdentity",
            ],
            "absenceRule": (
                "Absence is known only after a compatible registry volume validates. "
                "A runtime path missing from the registry is a counterexample and yields Unknown."
            ),
        },
    }
    rule["applicabilityRuleHash"] = rule_hash(rule)
    return rule


def create_virtual_mapping_rule() -> dict[str, Any]:
    rule = {
        "applicabilityRuleId": "spo.layouts.legacy-lcid-virtual-map.v1",
        "applicabilityRuleVersion": "1",
        "applicabilityRuleHash": "",
        "reviewRef": CONTRACT_REVIEW_REF,
        "definition": {
            "admitWhen": (
                "The frozen FilesToRedirect.sts.xms authority contains the ASPX name."
            ),
            "handler": "Microsoft.SharePoint.ApplicationRuntime.SPLayoutsMappedFile",
            "targetResolution": (
                "If the target setup artifact is absent from the same frozen shipping "
                "authority, retain the request identity as ReferenceUnavailable."
            ),
            "identityAxes": [
                "requestReferenceIdentity",
                "setupArtifactIdentity",
                "virtualHandlerIdentity",
                "ghostedPhysicalFileIdentity",
            ],
        },
    }
    rule["applicabilityRuleHash"] = rule_hash(rule)
    return rule


def create_consumer_compatibility() -> dict[str, Any]:
    return {
        "compatibilityDecisionRef": COMPATIBILITY_DECISION_REF,
        "recordKind": REFERENCE_RECORD_KIND,
        "sourceKinds": SOURCE_KINDS,
        "dispositions": {
            "ReferenceOnlyAvailable": {
                "assessment": "Emit a typed reference observation with no physical identity.",
                "pnpGraph": "Create a Reference node or edge; do not create PageArtifact bytes.",
                "repro": "ReuseTargetProvider only with target capability evidence; otherwise Delegate.",
                "compare": "Compare registry-bound request identity; payload equality is not implied.",
            },
            "ReferenceUnavailable": {
                "assessment": "Preserve unavailable evidence; do not synthesize an identity or empty payload.",
                "pnpGraph": "Keep an explicit gap edge and no physical node.",
                "repro": "Skip unsafe dependent writes; required dependencies remain Delegate/degraded.",
                "compare": "Denied/failed is Incomplete; unresolved registry identity is Unknown.",
            },
            "LinkedPhysicalGhosted": {
                "assessment": "Link to one verified physical inventory key and FileUniqueId.",
                "pnpGraph": "Reference the existing physical node exactly once.",
                "repro": "Plan one physical action regardless of reference count.",
                "compare": "Compare physical content once plus origin/registry reference semantics.",
            },
            "LinkedPhysicalCustomized": {
                "assessment": "Link to one verified customized physical inventory key and FileUniqueId.",
                "pnpGraph": "Reference the existing customized physical node exactly once.",
                "repro": "Plan one physical action and preserve customized origin.",
                "compare": "Compare physical content once plus customized-origin reference semantics.",
            },
            "VirtualHandler": {
                "assessment": "Emit a typed runtime dependency with no physical identity.",
                "pnpGraph": "Create a runtime/reference dependency, not a PageArtifact.",
                "repro": "Never upload; require target capability evidence or Delegate.",
                "compare": "Compare handler/path/registry/build; incompatible binding is Unknown.",
            },
            "NonAspx": {
                "assessment": "Retain the source observation as a negative ASPX classification.",
                "pnpGraph": "Do not create an ASPX PageArtifact from this observation.",
                "repro": "No ASPX write branch.",
                "compare": "Exclude from ASPX equality while retaining evidence.",
            },
            "Unknown": {
                "assessment": "Preserve raw evidence and fail closed.",
                "pnpGraph": "Do not guess a physical or virtual node kind.",
                "repro": "Do not execute an unsafe dependent write.",
                "compare": "Aggregate remains Unknown.",
            },
        },
        "volumeCompatibility": VOLUME_COMPATIBILITY,
        "artifactVerification": ARTIFACT_VERIFICATION,
    }


def build_profile(
    registry: dict[str, Any],
    authority: dict[str, Any],
    registry_schema_hash: str,
) -> dict[str, Any]:
    compatibility = registry["consumerCompatibility"]
    profile = {
        "$schema": "../schema/aspx-platform-registry-profile.schema.json",
        "profileSchemaVersion": PROFILE_SCHEMA_VERSION,
        "profileRevision": PROFILE_REVISION,
        "profileHash": "",
        "registrySchemaVersion": SCHEMA_VERSION,
        "registrySchemaHash": registry_schema_hash,
        "registryRevision": registry["registryRevision"],
        "registryHash": registry["registryHash"],
        "authoritySchemaVersion": authority["authoritySchemaVersion"],
        "authorityKind": authority["authorityKind"],
        "authoritySourceRef": authority["authoritySourceRef"],
        "authoritySourceTag": authority["authoritySourceTag"],
        "authorityArtifactHash": authority["authorityArtifactHash"],
        "platformFamily": PLATFORM_FAMILY,
        "platformBuild": PLATFORM_BUILD,
        "consumerProductId": CONSUMER_PRODUCT_ID,
        "consumerSourceRef": CONSUMER_SOURCE_REF,
        "consumerProductRef": CONSUMER_PRODUCT_REF,
        "contractReviewRef": CONTRACT_REVIEW_REF,
        "compatibilityDecisionRef": COMPATIBILITY_DECISION_REF,
        "consumerCompatibilityHash": hashlib.sha256(
            canonical_json_bytes(compatibility)
        ).hexdigest(),
        "recordKind": REFERENCE_RECORD_KIND,
        "sourceKinds": SOURCE_KINDS,
        "dispositions": DISPOSITIONS,
        "volumeCompatibility": VOLUME_COMPATIBILITY,
        "artifactVerification": ARTIFACT_VERIFICATION,
        "failureSemantics": FAILURE_SEMANTICS,
    }
    profile["profileHash"] = object_hash(profile, "profileHash")
    return profile


def build_authority(
    rows: list[dict[str, Any]],
    manifest_blobs: dict[str, str],
    redirect_blob: str,
    redirect_map: dict[str, dict[str, Any]],
    commit_time: str,
) -> dict[str, Any]:
    used_manifests = sorted({row["manifestPath"] for row in rows})
    missing_blobs = [path for path in used_manifests if path not in manifest_blobs]
    if missing_blobs:
        raise ValueError(f"missing manifest blob IDs: {missing_blobs}")
    enriched_rows = []
    for row in rows:
        enriched_rows.append(
            {
                **row,
                "manifestBlobId": manifest_blobs[row["manifestPath"]],
            }
        )
    authority = {
        "authoritySchemaVersion": AUTHORITY_SCHEMA_VERSION,
        "authorityArtifactHash": "",
        "authorityKind": "SPOCoreReleaseShippingManifest",
        "authoritySourceRef": AUTHORITY_REF,
        "authoritySourceTag": AUTHORITY_TAG,
        "authorityCommitTime": commit_time,
        "platformFamily": PLATFORM_FAMILY,
        "platformBuildMin": PLATFORM_BUILD,
        "platformBuildMax": PLATFORM_BUILD,
        "extractionAlgorithm": "ccd-spocore-layouts-deploy-manifest/v1",
        "independenceBoundary": {
            "allowedInputs": [
                "SPO.Core otools/deploy/*.xml at authoritySourceRef",
                f"SPO.Core {REDIRECT_MAP_PATH} at authoritySourceRef",
                f"SPO.Core {VIRTUAL_HANDLER_SOURCE_PATH} at authoritySourceRef",
                f"SPO.Core {VIRTUAL_FILE_SOURCE_PATH} at authoritySourceRef",
            ],
            "forbiddenInputs": [
                "Assessment provider output",
                "Assessment scanner-observed paths",
                "CUPCollect scan output",
                "tenant runtime enumeration",
            ],
        },
        "sourceArtifacts": {
            "shippingManifests": [
                {
                    "path": path,
                    "blobId": manifest_blobs[path],
                    "matchedRowCount": sum(1 for row in rows if row["manifestPath"] == path),
                }
                for path in used_manifests
            ],
            "legacyLcidRedirectMap": {
                "path": REDIRECT_MAP_PATH,
                "blobId": redirect_blob,
                "entryCount": len(redirect_map),
            },
            "virtualHandlerSources": [
                {
                    "path": VIRTUAL_HANDLER_SOURCE_PATH,
                    "blobId": manifest_blobs[VIRTUAL_HANDLER_SOURCE_PATH],
                    "symbols": [
                        "Microsoft.SharePoint.ApplicationRuntime.SPLayoutsMappedFile",
                        "Microsoft.SharePoint.ApplicationRuntime.SPVirtualPathProvider",
                    ],
                },
                {
                    "path": VIRTUAL_FILE_SOURCE_PATH,
                    "blobId": manifest_blobs[VIRTUAL_FILE_SOURCE_PATH],
                    "symbols": [
                        "Microsoft.SharePoint.ApplicationRuntime.SPVirtualFile",
                    ],
                },
            ] if INCLUDE_VIRTUAL_SOURCE_PROVENANCE else [
                VIRTUAL_HANDLER_SOURCE_PATH,
                VIRTUAL_FILE_SOURCE_PATH,
            ],
        },
        "deployRows": enriched_rows,
        "legacyLcidRedirects": [redirect_map[key] for key in sorted(redirect_map)],
    }
    authority["authorityArtifactHash"] = object_hash(authority, "authorityArtifactHash")
    return authority


def build_registry(
    authority: dict[str, Any],
    independent_review_ref: str,
) -> dict[str, Any]:
    rows = authority["deployRows"]
    redirect_map = {
        row["fileName"].casefold(): row for row in authority["legacyLcidRedirects"]
    }
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        grouped[row["relativeLayoutPath"].casefold()].append(row)

    rule = create_rule()
    virtual_mapping_rule = create_virtual_mapping_rule()
    entries: list[dict[str, Any]] = []
    matched_redirect_keys: set[str] = set()
    for normalized_relative in sorted(grouped):
        evidence_rows = sorted(
            grouped[normalized_relative],
            key=lambda row: (
                0 if row["flavor"] == "Ship" else 1,
                row["manifestPath"],
                row["manifestLine"],
                row["destination"],
            ),
        )
        relative_path = evidence_rows[0]["relativeLayoutPath"]
        canonical_path = f"/_layouts/15/{relative_path}"
        versionless_alias = f"/_layouts/{relative_path}"
        root_file_key = relative_path.casefold() if "/" not in relative_path else None
        redirect = redirect_map.get(root_file_key or "")
        alias_patterns = []
        virtual_handler_identity = None
        if redirect:
            matched_redirect_keys.add(root_file_key or "")
            alias_patterns.append(f"/_layouts/{{lcid}}/{redirect['fileName']}")
            virtual_handler_identity = {
                "handlerType": "Microsoft.SharePoint.ApplicationRuntime.SPLayoutsMappedFile",
                "sourcePath": VIRTUAL_HANDLER_SOURCE_PATH,
                "authorityMapPath": REDIRECT_MAP_PATH,
                "serverTransferOnPost": redirect["serverTransferOnPost"],
            }

        distinct_sources = []
        seen_source_keys: set[tuple[str, str]] = set()
        for evidence in evidence_rows:
            source_key = (evidence["manifestPath"], evidence["manifestBlobId"])
            if source_key in seen_source_keys:
                continue
            seen_source_keys.add(source_key)
            distinct_sources.append(
                {
                    "manifestPath": evidence["manifestPath"],
                    "manifestBlobId": evidence["manifestBlobId"],
                }
            )

        entry = {
            "referenceId": reference_id(canonical_path),
            "surfaceKind": "SetupLayoutApplicationPage",
            "canonicalRequestPath": canonical_path,
            "aliases": [versionless_alias],
            "aliasPatterns": alias_patterns,
            "handlerOrArtifactType": "SetupArtifact.AspxApplicationPage",
            "applicabilityRuleId": rule["applicabilityRuleId"],
            "applicabilityRuleHash": rule["applicabilityRuleHash"],
            "expectedAvailability": "AvailableForExactPlatformBuild",
            "downstreamDisposition": "ReferenceOnlyAvailable",
            "identityAxes": {
                "requestReferenceIdentity": {
                    "canonicalRequestPath": canonical_path,
                    "aliases": [versionless_alias],
                    "aliasPatterns": alias_patterns,
                },
                "setupArtifactIdentity": {
                    "relativeLayoutPath": relative_path,
                    "shippingManifestEvidence": distinct_sources,
                },
                "virtualHandlerIdentity": virtual_handler_identity,
                "ghostedPhysicalFileIdentity": None,
            },
            "physicalIdentity": {
                "storageIdentity": "NoPhysicalFile",
                "fileUniqueId": None,
                "physicalLocator": None,
            },
            "contentOrigin": "SetupArtifact",
        }
        entries.append(entry)

    redirect_map_blob = authority["sourceArtifacts"]["legacyLcidRedirectMap"]["blobId"]
    for redirect_key in sorted(set(redirect_map) - matched_redirect_keys):
        redirect = redirect_map[redirect_key]
        relative_path = redirect["fileName"]
        canonical_path = f"/_layouts/15/{relative_path}"
        versionless_alias = f"/_layouts/{relative_path}"
        alias_pattern = f"/_layouts/{{lcid}}/{relative_path}"
        entries.append(
            {
                "referenceId": reference_id(canonical_path),
                "surfaceKind": "VirtualMappedRequest",
                "canonicalRequestPath": canonical_path,
                "aliases": [versionless_alias],
                "aliasPatterns": [alias_pattern],
                "handlerOrArtifactType": "VirtualHandler.SPLayoutsMappedFile",
                "applicabilityRuleId": virtual_mapping_rule["applicabilityRuleId"],
                "applicabilityRuleHash": virtual_mapping_rule["applicabilityRuleHash"],
                "expectedAvailability": "ReferenceTargetAbsentAtFrozenBuild",
                "downstreamDisposition": "ReferenceUnavailable",
                "identityAxes": {
                    "requestReferenceIdentity": {
                        "canonicalRequestPath": canonical_path,
                        "aliases": [versionless_alias],
                        "aliasPatterns": [alias_pattern],
                    },
                    "setupArtifactIdentity": None,
                    "virtualHandlerIdentity": {
                        "handlerType": (
                            "Microsoft.SharePoint.ApplicationRuntime.SPLayoutsMappedFile"
                        ),
                        "sourcePath": VIRTUAL_HANDLER_SOURCE_PATH,
                        "authorityMapPath": REDIRECT_MAP_PATH,
                        "authorityMapBlobId": redirect_map_blob,
                        "serverTransferOnPost": redirect["serverTransferOnPost"],
                        "mappedTargetState": "AbsentFromFrozenShippingManifest",
                    },
                    "ghostedPhysicalFileIdentity": None,
                },
                "physicalIdentity": {
                    "storageIdentity": "NoPhysicalFile",
                    "fileUniqueId": None,
                    "physicalLocator": None,
                },
                "contentOrigin": "VirtualHandler",
            }
        )

    entries.sort(key=lambda entry: entry["canonicalRequestPath"].casefold())

    registry = {
        "$schema": "../schema/aspx-platform-registry.schema.json",
        "registrySchemaVersion": SCHEMA_VERSION,
        "registryRevision": REGISTRY_REVISION,
        "registryHash": "",
        "authorityKind": authority["authorityKind"],
        "authoritySourceRef": AUTHORITY_REF,
        "authoritySourceTag": AUTHORITY_TAG,
        "authorityArtifactHash": authority["authorityArtifactHash"],
        "authorityArtifactPath": (
            f"authority/{ARTIFACT_STEM}.authority.json"
        ),
        "reviewRef": independent_review_ref,
        "contractReviewRef": CONTRACT_REVIEW_REF,
        "compatibilityDecisionRef": COMPATIBILITY_DECISION_REF,
        "generatedAtUtc": authority["authorityCommitTime"],
        "platformFamily": PLATFORM_FAMILY,
        "platformBuildMin": PLATFORM_BUILD,
        "platformBuildMax": PLATFORM_BUILD,
        "normalization": {
            "algorithmId": "sharepoint-layout-request-path/v1",
            "caseSensitivity": "OrdinalIgnoreCase",
            "queryAndFragment": "RemoveBeforeLookupButPreserveInRawLocator",
            "siteQualifiedPrefix": "StripOnlyThroughTheFirstExact_LayoutsSegment",
            "versionlessLayouts": "Map /_layouts/x to /_layouts/15/x",
            "legacyLcidAlias": (
                "Map /_layouts/{decimal-lcid}/x only when x is present in "
                "FilesToRedirect.sts.xms"
            ),
            "encodedSeparators": "Reject",
            "dotSegments": "Reject",
            "collisionRule": (
                "If any normalized explicit path or expanded reviewed alias maps to more "
                "than one referenceId, reject the registry and return Unknown."
            ),
        },
        "failureSemantics": FAILURE_SEMANTICS,
        "applicabilityRules": [rule, virtual_mapping_rule],
        "consumerCompatibility": create_consumer_compatibility(),
        "entryCount": len(entries),
        "entries": entries,
    }
    validate_explicit_alias_collisions(registry)
    registry["registryHash"] = object_hash(registry, "registryHash")
    return registry


def validate_explicit_alias_collisions(registry: dict[str, Any]) -> None:
    owners: dict[str, str] = {}
    for entry in registry["entries"]:
        for path in [entry["canonicalRequestPath"], *entry["aliases"]]:
            key = normalize_registry_key(path)
            previous = owners.get(key)
            if previous is not None and previous != entry["referenceId"]:
                raise ValueError(
                    f"alias normalization collision: {path} maps to {previous} and "
                    f"{entry['referenceId']}"
                )
            owners[key] = entry["referenceId"]


def collect_source(
    git_executable: str,
    repo: str,
) -> tuple[list[dict[str, Any]], dict[str, str], str, dict[str, dict[str, Any]], str]:
    resolved = run_git(git_executable, repo, ["rev-parse", AUTHORITY_REF]).strip()
    if resolved != AUTHORITY_REF:
        raise ValueError(f"authority ref resolved to unexpected object: {resolved}")
    commit_epoch = run_git(
        git_executable, repo, ["show", "-s", "--format=%ct", AUTHORITY_REF]
    ).strip()
    commit_time = canonical_utc_from_epoch(commit_epoch)
    deploy_input_paths = enumerate_deploy_input_paths(
        git_executable, repo, AUTHORITY_REF
    )
    grep_output = run_git(
        git_executable,
        repo,
        [
            "grep",
            "-n",
            "-i",
            "-E",
            r"TEMPLATE\\LAYOUTS.*\.aspx",
            AUTHORITY_REF,
            "--",
            DEPLOY_PATHSPEC,
        ],
    )
    rows = extract_deploy_rows(grep_output)
    unexpected_rows = sorted(
        {row["manifestPath"] for row in rows} - set(deploy_input_paths)
    )
    if unexpected_rows:
        raise ValueError(
            "deploy row enumeration escaped the instrumented input denominator: "
            f"{unexpected_rows}"
        )
    tree_output = run_git(
        git_executable,
        repo,
        [
            "ls-tree",
            "-r",
            AUTHORITY_REF,
            "--",
            "otools/deploy",
            REDIRECT_MAP_PATH,
            VIRTUAL_HANDLER_SOURCE_PATH,
            VIRTUAL_FILE_SOURCE_PATH,
        ],
    )
    blobs = parse_tree_blobs(tree_output)
    redirect_blob = blobs.get(REDIRECT_MAP_PATH)
    if not redirect_blob:
        raise ValueError("redirect-map blob ID is missing from the frozen tree")
    redirect_xml = run_git(
        git_executable, repo, ["show", f"{AUTHORITY_REF}:{REDIRECT_MAP_PATH}"]
    )
    redirect_map = parse_redirect_map(redirect_xml)
    return rows, blobs, redirect_blob, redirect_map, commit_time


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def validate_profile_schema_resource(
    profile_schema: dict[str, Any],
    registry_schema: dict[str, Any] | None = None,
) -> list[str]:
    errors: list[str] = []
    if profile_schema.get("$id") != PROFILE_SCHEMA_RESOURCE_ID:
        errors.append("profile schema $id is not the frozen revision-bound resource ID")
    properties = profile_schema.get("properties")
    if not isinstance(properties, dict):
        errors.append("profile schema properties must be an object")
        return errors
    expected_consts = {
        "profileSchemaVersion": PROFILE_SCHEMA_VERSION,
        "profileRevision": PROFILE_REVISION,
    }
    for key, expected in expected_consts.items():
        definition = properties.get(key)
        if not isinstance(definition, dict) or definition.get("const") != expected:
            errors.append(f"profile schema {key} const is not the frozen value")
    if registry_schema is not None and profile_schema.get("$id") == registry_schema.get("$id"):
        errors.append("profile and registry schemas share one resource ID")
    return errors


def bind_fixture_document(
    fixtures: dict[str, Any],
    registry: dict[str, Any],
    profile: dict[str, Any],
    fixture_root: Path,
) -> dict[str, Any]:
    bound = copy.deepcopy(fixtures)
    artifact_bindings = bound["artifactBindings"]
    artifact_paths: dict[str, Path] = {}
    for key, descriptor in artifact_bindings.items():
        artifact_path = (fixture_root / descriptor["path"]).resolve()
        if fixture_root.resolve() not in artifact_path.parents:
            raise ValueError(f"fixture artifact escapes fixture root: {descriptor['path']}")
        if not artifact_path.is_file():
            raise ValueError(f"fixture artifact is missing: {artifact_path}")
        artifact_paths[key] = artifact_path
        descriptor["sha256"] = artifact_sha256(artifact_path)
        descriptor["length"] = artifact_path.stat().st_size
        if key.endswith("Store"):
            schema_hash = sqlite_schema_hash(artifact_path)
            expected_schema_hash = ARTIFACT_VERIFICATION[
                "physicalStoreSchemaHash"
                if key == "physicalStore"
                else "referenceStoreSchemaHash"
            ]
            if schema_hash != expected_schema_hash:
                raise ValueError(
                    f"{key} schema drift: expected {expected_schema_hash}, got {schema_hash}"
                )
            descriptor["schemaManifestHash"] = schema_hash

    reader = bound["readerTemplate"]
    registry_envelope = reader["registryEnvelope"]
    registry_envelope.update(
        {
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
    )
    reader["platformBuild"] = profile["platformBuild"]
    acquisition = reader["acquisitionEnvelope"]
    acquisition.update(
        {
            "platformBuild": profile["platformBuild"],
            "productRef": profile["consumerProductRef"],
            "registryRevision": registry["registryRevision"],
            "registryHash": registry["registryHash"],
        }
    )
    physical = acquisition["physicalVolume"]
    reference = acquisition["referenceVolume"]
    physical_binding = artifact_bindings["physicalOutput"]
    reference_binding = artifact_bindings["referenceOutput"]
    physical.update(
        {
            "artifactHash": physical_binding["sha256"],
            "artifactLength": physical_binding["length"],
            "productRef": profile["consumerProductRef"],
        }
    )
    physical["platformBuild"] = profile["platformBuild"]
    reference.update(
        {
            "artifactHash": reference_binding["sha256"],
            "artifactLength": reference_binding["length"],
            "productRef": profile["consumerProductRef"],
            "platformBuild": profile["platformBuild"],
            "registryRevision": registry["registryRevision"],
            "registryHash": registry["registryHash"],
            "recordKind": profile["recordKind"],
            "sourceKinds": profile["sourceKinds"],
            "dispositions": profile["dispositions"],
        }
    )
    acquisition["physicalArtifactHash"] = physical["artifactHash"]
    acquisition["referenceArtifactHash"] = reference["artifactHash"]

    run_id = acquisition["runId"]
    for evidence_key, expected_version, run_field in [
        ("physicalOutput", VOLUME_COMPATIBILITY["physicalOutput"], "runId"),
        ("referenceOutput", VOLUME_COMPATIBILITY["referenceOutput"], "acquisitionRunId"),
    ]:
        document = load_json(artifact_paths[evidence_key])
        if document.get("outputVersion") != expected_version:
            raise ValueError(f"{evidence_key} outputVersion is not {expected_version}")
        if document.get(run_field) != run_id:
            raise ValueError(f"{evidence_key} run ID does not match reader fixture")
    with closing(sqlite3.connect(
        artifact_paths["physicalStore"].resolve().as_uri() + "?mode=ro", uri=True
    )) as connection:
        row = connection.execute(
            "SELECT ManifestHash FROM DiscoveryRuns WHERE RunId=?", (run_id,)
        ).fetchone()
        if row is None:
            raise ValueError("physical SQLite fixture does not contain the acquisition run")
        physical["store"]["manifestHash"] = row[0]
    with closing(sqlite3.connect(
        artifact_paths["referenceStore"].resolve().as_uri() + "?mode=ro", uri=True
    )) as connection:
        row = connection.execute(
            "SELECT ManifestHash FROM ReferenceRuns WHERE RunId=?", (run_id,)
        ).fetchone()
        if row is None:
            raise ValueError("reference SQLite fixture does not contain the acquisition run")
        reference["store"]["manifestHash"] = row[0]
    return bound


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--spocore-repo", required=True)
    parser.add_argument("--git-executable", default="git")
    parser.add_argument("--release-spec", type=Path)
    parser.add_argument(
        "--output-root",
        type=Path,
        default=Path(__file__).resolve().parent,
    )
    parser.add_argument("--independent-review-ref")
    args = parser.parse_args()

    if args.release_spec is not None:
        configure_release(load_json(args.release_spec))

    rows, blobs, redirect_blob, redirect_map, commit_time = collect_source(
        args.git_executable, args.spocore_repo
    )
    authority = build_authority(rows, blobs, redirect_blob, redirect_map, commit_time)
    registry = build_registry(
        authority,
        args.independent_review_ref or DEFAULT_INDEPENDENT_REVIEW_REF,
    )

    registry_schema_path = (
        args.output_root / "schema" / "aspx-platform-registry.schema.json"
    )
    if not registry_schema_path.is_file():
        raise ValueError(f"registry schema is missing: {registry_schema_path}")
    profile_schema_path = (
        args.output_root / "schema" / "aspx-platform-registry-profile.schema.json"
    )
    if not profile_schema_path.is_file():
        raise ValueError(f"profile schema is missing: {profile_schema_path}")
    profile_schema_errors = validate_profile_schema_resource(
        load_json(profile_schema_path),
        load_json(registry_schema_path),
    )
    if profile_schema_errors:
        raise ValueError("; ".join(profile_schema_errors))
    profile = build_profile(
        registry,
        authority,
        artifact_sha256(registry_schema_path),
    )
    frozen_bindings = {
        "authorityArtifactHash": (
            authority["authorityArtifactHash"],
            EXPECTED_AUTHORITY_ARTIFACT_HASH,
        ),
        "registryHash": (registry["registryHash"], EXPECTED_REGISTRY_HASH),
        "registrySchemaHash": (
            profile["registrySchemaHash"],
            EXPECTED_REGISTRY_SCHEMA_HASH,
        ),
        "consumerCompatibilityHash": (
            profile["consumerCompatibilityHash"],
            EXPECTED_CONSUMER_COMPATIBILITY_HASH,
        ),
        "profileHash": (profile["profileHash"], EXPECTED_PROFILE_HASH),
    }
    drift = [
        f"{key}: expected {expected}, got {actual}"
        for key, (actual, expected) in frozen_bindings.items()
        if actual != expected
    ]
    if drift:
        raise ValueError(
            "frozen registry profile drift requires a new reviewed revision: "
            + "; ".join(drift)
        )
    fixture_path = args.output_root / "fixtures" / "contract-cases.json"
    if not fixture_path.is_file():
        raise ValueError(f"contract fixtures are missing: {fixture_path}")
    fixtures = bind_fixture_document(
        load_json(fixture_path), registry, profile, fixture_path.parent
    )

    write_json(
        args.output_root / "authority" / f"{ARTIFACT_STEM}.authority.json",
        authority,
    )
    write_json(
        args.output_root / "registry" / f"{ARTIFACT_STEM}.registry.json",
        registry,
    )
    write_json(
        args.output_root / "profile" / f"{ARTIFACT_STEM}.profile.json",
        profile,
    )
    write_json(fixture_path, fixtures)
    print(
        json.dumps(
            {
                "authoritySourceRef": AUTHORITY_REF,
                "authorityArtifactHash": authority["authorityArtifactHash"],
                "registryRevision": registry["registryRevision"],
                "registryHash": registry["registryHash"],
                "profileRevision": profile["profileRevision"],
                "profileHash": profile["profileHash"],
                "registrySchemaHash": profile["registrySchemaHash"],
                "entryCount": registry["entryCount"],
                "platformBuildMin": registry["platformBuildMin"],
                "platformBuildMax": registry["platformBuildMax"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
