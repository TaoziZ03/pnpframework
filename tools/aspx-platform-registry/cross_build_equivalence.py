#!/usr/bin/env python3
"""Fail-closed cross-build proof and exact registry-payload reuse.

The proof enumerates the same bounded SPO.Core authority that the registry
generator consumes.  It records every selected blob and a parser-versioned
semantic digest.  Reuse is allowed only when the source and target closures
are exactly equal.  Any uncertainty runs the existing exact-build generator.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import io
import json
import posixpath
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any, Iterable

import generate_registry as registry_generator


CERTIFICATE_VERSION = "cross-build-equivalence-certificate/v1"
CLOSURE_VERSION = "aspx-registry-input-closure/v1"
GENERATOR_VERSION = "aspx-registry-generator/v2-equivalence"
RELEASE_SPEC_BINDING_VERSION = "aspx-platform-registry-release-spec-binding/v1"
DEPLOY_PARSER = "otools-deploy-xml/v2-exact-order"
REDIRECT_PARSER = "files-to-redirect/v2-exact-order"
SOURCE_PARSER = "virtual-path-provider-source/v1-exact-bytes"
GENERATOR_PARSER = "registry-generator-input/v1-exact-bytes"
MAX_CHAIN_LENGTH = 8

DEPLOY_ROOT = "otools/deploy"
REDIRECT_PATH = registry_generator.REDIRECT_MAP_PATH
VIRTUAL_PATHS = (
    registry_generator.VIRTUAL_HANDLER_SOURCE_PATH,
    registry_generator.VIRTUAL_FILE_SOURCE_PATH,
)
GENERATOR_INPUT_PATHS = (
    "tools/aspx-platform-registry/cross_build_equivalence.py",
    "tools/aspx-platform-registry/generate_registry.py",
    "tools/aspx-platform-registry/validate_registry.py",
    "tools/aspx-platform-registry/schema/cross-build-equivalence-certificate.schema.json",
)
INCLUDE_TAG_RE = re.compile(r"<\s*(?:[A-Za-z_][\w.-]*:)?(include|import)\b([^>]*)>", re.IGNORECASE)
ATTRIBUTE_RE = re.compile(r"([A-Za-z_][\w:.-]*)\s*=\s*(['\"])(.*?)\2", re.DOTALL)
DEST_RE = re.compile(r'dest\s*=\s*"([^"]+)"', re.IGNORECASE)
LAYOUT_DEST_RE = registry_generator.LAYOUT_DEST_RE
HASH_RE = re.compile(r"[0-9a-f]{64}")
GIT_SHA_RE = re.compile(r"[0-9a-f]{40}")


class ProofUnavailable(RuntimeError):
    """A bounded uncertainty that requires full exact generation."""


def canonical_json_bytes(value: Any) -> bytes:
    return json.dumps(
        value, ensure_ascii=False, sort_keys=True, separators=(",", ":")
    ).encode("utf-8")


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def canonical_hash(value: dict[str, Any], field: str) -> str:
    material = copy.deepcopy(value)
    material.pop(field, None)
    return sha256_bytes(canonical_json_bytes(material))


def run_git_bytes(git_executable: str, repo: str, args: Iterable[str]) -> bytes:
    command = [git_executable, "-C", repo, *args]
    completed = subprocess.run(command, check=False, capture_output=True)
    if completed.returncode != 0:
        stderr = completed.stderr.decode("utf-8", errors="replace").strip()
        raise ProofUnavailable(
            f"git command failed ({completed.returncode}): {' '.join(command)}: {stderr}"
        )
    return completed.stdout


def run_git_text(git_executable: str, repo: str, args: Iterable[str]) -> str:
    try:
        return run_git_bytes(git_executable, repo, args).decode("utf-8", errors="strict")
    except UnicodeDecodeError as error:
        raise ProofUnavailable(f"Git object is not strict UTF-8: {error}") from error


def resolve_git_identity(git_executable: str, repo: str, ref: str) -> dict[str, str]:
    commit = run_git_text(git_executable, repo, ["rev-parse", f"{ref}^{{commit}}"] ).strip()
    tree = run_git_text(git_executable, repo, ["show", "-s", "--format=%T", commit]).strip()
    if not GIT_SHA_RE.fullmatch(commit) or not GIT_SHA_RE.fullmatch(tree):
        raise ProofUnavailable("Git commit/tree identity is not a full SHA-1")
    return {"ref": ref, "commit": commit, "tree": tree}


def list_tree_blobs(
    git_executable: str, repo: str, ref: str, paths: Iterable[str]
) -> dict[str, str]:
    output = run_git_bytes(
        git_executable,
        repo,
        ["ls-tree", "-r", "-z", "--full-tree", ref, "--", *paths],
    )
    blobs: dict[str, str] = {}
    for record in output.split(b"\0"):
        if not record:
            continue
        try:
            metadata, raw_path = record.split(b"\t", 1)
            _mode, kind, raw_object_id = metadata.split(b" ", 2)
            path = raw_path.decode("utf-8", errors="strict")
            object_id = raw_object_id.decode("ascii", errors="strict")
        except (ValueError, UnicodeDecodeError) as error:
            raise ProofUnavailable(f"unparseable git ls-tree record: {error}") from error
        if kind == b"blob":
            blobs[path] = object_id
    return blobs


def read_blob(git_executable: str, repo: str, ref: str, path: str) -> bytes:
    return run_git_bytes(git_executable, repo, ["show", f"{ref}:{path}"])


def read_blobs(
    git_executable: str, repo: str, path_to_blob: dict[str, str]
) -> dict[str, bytes]:
    """Read a complete object set through one Git process.

    The shared host Git bridge has non-trivial startup cost.  A single
    ``cat-file --batch`` call keeps closure enumeration bounded without
    changing which immutable objects are read.
    """
    ordered = sorted(path_to_blob.items())
    command = [git_executable, "-C", repo, "cat-file", "--batch"]
    completed = subprocess.run(
        command,
        input=b"".join(object_id.encode("ascii") + b"\n" for _, object_id in ordered),
        check=False,
        capture_output=True,
    )
    if completed.returncode != 0:
        stderr = completed.stderr.decode("utf-8", errors="replace").strip()
        raise ProofUnavailable(
            f"git cat-file --batch failed ({completed.returncode}): {stderr}"
        )
    stream = io.BytesIO(completed.stdout)
    result: dict[str, bytes] = {}
    for path, expected_object_id in ordered:
        header = stream.readline().rstrip(b"\n")
        parts = header.split(b" ")
        if len(parts) != 3 or parts[1] != b"blob":
            raise ProofUnavailable(f"unexpected cat-file header for {path}: {header!r}")
        object_id = parts[0].decode("ascii", errors="strict")
        size = int(parts[2])
        if object_id != expected_object_id:
            raise ProofUnavailable(
                f"cat-file object mismatch for {path}: expected {expected_object_id}, got {object_id}"
            )
        data = stream.read(size)
        if len(data) != size or stream.read(1) != b"\n":
            raise ProofUnavailable(f"truncated cat-file payload for {path}")
        result[path] = data
    if stream.read(1):
        raise ProofUnavailable("cat-file returned unexpected trailing data")
    return result


def semantic_hash(value: Any) -> str:
    return sha256_bytes(canonical_json_bytes(value))


def _include_paths(xml_text: str, containing_path: str) -> list[str]:
    includes: list[str] = []
    for match in INCLUDE_TAG_RE.finditer(xml_text):
        attributes = {
            name.casefold(): value
            for name, _quote, value in ATTRIBUTE_RE.findall(match.group(2))
        }
        candidates = [
            attributes[key]
            for key in ("href", "path", "file", "src", "project")
            if key in attributes
        ]
        if len(candidates) != 1:
            raise ProofUnavailable(
                f"unresolved {match.group(1).lower()} in {containing_path}: "
                "exactly one path attribute is required"
            )
        candidate = candidates[0].replace("\\", "/")
        if "://" in candidate or candidate.startswith("/"):
            raise ProofUnavailable(
                f"external or absolute include is not allowed: {containing_path} -> {candidate}"
            )
        resolved = posixpath.normpath(
            posixpath.join(posixpath.dirname(containing_path), candidate)
        )
        if resolved == ".." or resolved.startswith("../"):
            raise ProofUnavailable(
                f"include escapes repository root: {containing_path} -> {candidate}"
            )
        includes.append(resolved)
    return includes


def _xml_semantics(path: str, data: bytes, parser_id: str) -> tuple[Any, list[str]]:
    try:
        text = data.decode("utf-8", errors="strict")
    except UnicodeDecodeError as error:
        raise ProofUnavailable(f"{path} cannot be parsed as strict UTF-8: {error}") from error

    if parser_id == DEPLOY_PARSER:
        try:
            ET.fromstring(text)
        except ET.ParseError as error:
            raise ProofUnavailable(
                f"{path} cannot be parsed as strict deploy XML: {error}"
            ) from error

    includes = _include_paths(text, path)
    if parser_id == DEPLOY_PARSER:
        destinations: list[dict[str, Any]] = []
        for line_number, line in enumerate(text.splitlines(), start=1):
            destination_match = DEST_RE.search(line)
            if destination_match is None:
                continue
            destination = destination_match.group(1)
            layout_match = LAYOUT_DEST_RE.match(destination)
            if layout_match is None:
                continue
            destinations.append(
                {
                    "line": line_number,
                    "destination": destination,
                    "relativeLayoutPath": layout_match.group(1).replace("\\", "/"),
                    "flavor": "Debug" if "Microsoft Shared Debug" in destination else "Ship",
                }
            )
        return {"destinations": destinations, "includes": includes}, includes

    redirects = registry_generator.parse_redirect_map(text)
    ordered_redirects = [redirects[key] for key in sorted(redirects)]
    return {"redirects": ordered_redirects, "includes": includes}, includes


def _source_semantics(path: str, data: bytes) -> Any:
    try:
        text = data.decode("utf-8", errors="strict")
    except UnicodeDecodeError as error:
        raise ProofUnavailable(f"{path} is not strict UTF-8 source: {error}") from error
    required = {
        registry_generator.VIRTUAL_HANDLER_SOURCE_PATH: (
            "SPLayoutsMappedFile",
            "SPVirtualPathProvider",
        ),
        registry_generator.VIRTUAL_FILE_SOURCE_PATH: ("SPVirtualFile",),
    }[path]
    missing = [symbol for symbol in required if symbol not in text]
    if missing:
        raise ProofUnavailable(f"{path} is missing required symbols: {missing}")
    return {"requiredSymbols": list(required), "present": True}


def collect_spocore_closure(
    git_executable: str,
    repo: str,
    ref: str,
    build: str,
    source_tag: str,
    blob_cache: dict[str, bytes] | None = None,
    semantic_cache: dict[tuple[str, str, str], tuple[Any, list[str]]] | None = None,
) -> dict[str, Any]:
    blob_cache = blob_cache if blob_cache is not None else {}
    semantic_cache = semantic_cache if semantic_cache is not None else {}
    identity = resolve_git_identity(git_executable, repo, ref)
    try:
        deploy_paths = registry_generator.enumerate_deploy_input_paths(
            git_executable, repo, identity["commit"]
        )
    except (RuntimeError, ValueError) as error:
        raise ProofUnavailable(f"deploy input enumeration failed: {error}") from error
    blobs = list_tree_blobs(
        git_executable,
        repo,
        identity["commit"],
        [*deploy_paths, REDIRECT_PATH, *VIRTUAL_PATHS],
    )
    for required in (REDIRECT_PATH, *VIRTUAL_PATHS):
        if required not in blobs:
            raise ProofUnavailable(f"required generator input is missing: {required}")

    selected: dict[str, tuple[str, str]] = {
        path: ("shipping_manifest", DEPLOY_PARSER) for path in deploy_paths
    }
    selected[REDIRECT_PATH] = ("legacy_redirect_map", REDIRECT_PARSER)
    for path in VIRTUAL_PATHS:
        selected[path] = ("virtual_path_provider_source", SOURCE_PARSER)

    members: list[dict[str, Any]] = []
    missing_blob_paths = {
        path: blobs[path] for path in selected if blobs[path] not in blob_cache
    }
    if missing_blob_paths:
        for path, data in read_blobs(git_executable, repo, missing_blob_paths).items():
            blob_cache[blobs[path]] = data
    pending = list(selected)
    parsed: set[str] = set()
    deploy_rows: list[dict[str, Any]] = []
    redirect_map: dict[str, dict[str, Any]] | None = None
    while pending:
        path = pending.pop(0)
        if path in parsed:
            continue
        parsed.add(path)
        if path not in blobs:
            extra = list_tree_blobs(git_executable, repo, identity["commit"], [path])
            blobs.update(extra)
        blob_id = blobs.get(path)
        if blob_id is None:
            raise ProofUnavailable(f"unresolved include/import source: {path}")
        if blob_id not in blob_cache:
            blob_cache[blob_id] = read_blobs(
                git_executable, repo, {path: blob_id}
            )[path]
        data = blob_cache[blob_id]
        kind, parser_id = selected[path]
        semantic_key = (path, blob_id, parser_id)
        cached_semantics = semantic_cache.get(semantic_key)
        if cached_semantics is not None:
            semantics, includes = copy.deepcopy(cached_semantics)
        elif parser_id in {DEPLOY_PARSER, REDIRECT_PARSER}:
            semantics, includes = _xml_semantics(path, data, parser_id)
            semantic_cache[semantic_key] = copy.deepcopy((semantics, includes))
        else:
            semantics = _source_semantics(path, data)
            includes = []
            semantic_cache[semantic_key] = copy.deepcopy((semantics, includes))
        if parser_id in {DEPLOY_PARSER, REDIRECT_PARSER}:
            if parser_id == DEPLOY_PARSER:
                for row in semantics["destinations"]:
                    deploy_rows.append(
                        {
                            "manifestPath": path,
                            "manifestLine": row["line"],
                            "destination": row["destination"],
                            "relativeLayoutPath": row["relativeLayoutPath"],
                            "flavor": row["flavor"],
                        }
                    )
            else:
                redirect_map = {
                    item["fileName"].casefold(): item for item in semantics["redirects"]
                }
        for include in includes:
            if include not in selected:
                selected[include] = ("indirect_xml_input", DEPLOY_PARSER)
                pending.append(include)
        members.append(
            {
                "path": path,
                "pathCaseFold": path.casefold(),
                "kind": kind,
                "blobId": blob_id,
                "sha256": sha256_bytes(data),
                "byteLength": len(data),
                "parserId": parser_id,
                "semanticHash": semantic_hash(semantics),
            }
        )
    if not deploy_rows:
        raise ProofUnavailable("strict closure parser found no ASPX deploy rows")
    if redirect_map is None:
        raise ProofUnavailable("redirect map was not parsed")
    members.sort(key=lambda item: item["path"])
    closure = {
        "closureVersion": CLOSURE_VERSION,
        "build": build,
        "sourceTag": source_tag,
        "commit": identity["commit"],
        "tree": identity["tree"],
        "selection": {
            "deployRoot": DEPLOY_ROOT,
            "deployPathspec": registry_generator.DEPLOY_PATHSPEC,
            "deployRule": "shared generator git-grep pathspec enumeration; parse every selected blob",
            "redirectMap": REDIRECT_PATH,
            "virtualSources": list(VIRTUAL_PATHS),
            "includeRule": "resolve repository-relative include/import paths recursively",
        },
        "members": members,
        "closureHash": "",
    }
    closure["closureHash"] = canonical_hash(closure, "closureHash")
    return {
        "closure": closure,
        "deployRows": deploy_rows,
        "blobs": blobs,
        "redirectBlob": blobs[REDIRECT_PATH],
        "redirectMap": redirect_map,
        "commitTime": registry_generator.canonical_utc_from_epoch(
            run_git_text(
                git_executable,
                repo,
                ["show", "-s", "--format=%ct", identity["commit"]],
            ).strip()
        ),
    }


def collect_generator_identity(
    git_executable: str, repo: str, ref: str
) -> dict[str, Any]:
    identity = resolve_git_identity(git_executable, repo, ref)
    blobs = list_tree_blobs(git_executable, repo, identity["commit"], GENERATOR_INPUT_PATHS)
    blob_bytes = read_blobs(git_executable, repo, blobs)
    members: list[dict[str, Any]] = []
    for path in GENERATOR_INPUT_PATHS:
        if path not in blobs:
            raise ProofUnavailable(f"generator input is not committed at {ref}: {path}")
        data = blob_bytes[path]
        members.append(
            {
                "path": path,
                "blobId": blobs[path],
                "sha256": sha256_bytes(data),
                "byteLength": len(data),
                "parserId": GENERATOR_PARSER,
            }
        )
    return {
        "generatorVersion": GENERATOR_VERSION,
        "commit": identity["commit"],
        "tree": identity["tree"],
        "canonicalization": "canonical-json/sorted-utf8-no-whitespace/v1",
        "members": members,
        "manifestHash": semantic_hash(members),
    }


def compare_closures(source: dict[str, Any], target: dict[str, Any]) -> dict[str, Any]:
    source_members = {item["path"]: item for item in source["members"]}
    target_members = {item["path"]: item for item in target["members"]}
    added = sorted(set(target_members) - set(source_members))
    deleted = sorted(set(source_members) - set(target_members))
    changed: list[dict[str, Any]] = []
    for path in sorted(set(source_members) & set(target_members)):
        left = source_members[path]
        right = target_members[path]
        fields = [
            field
            for field in ("blobId", "sha256", "byteLength", "parserId", "semanticHash")
            if left.get(field) != right.get(field)
        ]
        if fields:
            changed.append({"path": path, "fields": fields})
    renamed: list[dict[str, str]] = []
    for deleted_path in deleted:
        for added_path in added:
            if source_members[deleted_path]["sha256"] == target_members[added_path]["sha256"]:
                renamed.append({"from": deleted_path, "to": added_path})
    case_changes = [
        pair
        for pair in renamed
        if pair["from"].casefold() == pair["to"].casefold()
        and pair["from"] != pair["to"]
    ]
    equivalent = not (added or deleted or changed)
    return {
        "equivalent": equivalent,
        "sourceMemberCount": len(source_members),
        "targetMemberCount": len(target_members),
        "added": added,
        "deleted": deleted,
        "changed": changed,
        "renameOrAliasCandidates": renamed,
        "caseChanges": case_changes,
        "uncertainties": [],
    }


def registry_payload_hash(registry: dict[str, Any]) -> str:
    payload = {
        key: registry[key]
        for key in (
            "normalization",
            "failureSemantics",
            "applicabilityRules",
            "consumerCompatibility",
            "entryCount",
            "entries",
        )
    }
    return semantic_hash(payload)


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )


def json_file_bytes(value: Any) -> bytes:
    return (
        json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n"
    ).encode("utf-8")


def source_release_artifacts(root: Path, build: str) -> dict[str, Any]:
    stem = f"spo-online-{build}"
    paths = {
        "authority": root / "authority" / f"{stem}.authority.json",
        "registry": root / "registry" / f"{stem}.registry.json",
        "profile": root / "profile" / f"{stem}.profile.json",
        "registrySchema": root / "schema" / "aspx-platform-registry.schema.json",
        "profileSchema": root / "schema" / "aspx-platform-registry-profile.schema.json",
    }
    missing = [str(path) for path in paths.values() if not path.is_file()]
    if missing:
        raise ProofUnavailable(f"source admitted release is incomplete: {missing}")
    return {
        "paths": paths,
        **{key: load_json(path) for key, path in paths.items()},
    }


def validate_release_artifact_build(
    label: str,
    artifacts: dict[str, Any],
    expected_build: str,
) -> None:
    registry = artifacts["registry"]
    authority = artifacts["authority"]
    profile = artifacts["profile"]
    if (
        registry.get("platformBuildMin") != expected_build
        or registry.get("platformBuildMax") != expected_build
    ):
        raise ProofUnavailable(f"{label} registry is not exact build {expected_build}")
    if (
        authority.get("platformBuildMin") != expected_build
        or authority.get("platformBuildMax") != expected_build
    ):
        raise ProofUnavailable(f"{label} authority is not exact build {expected_build}")
    if profile.get("platformBuild") != expected_build:
        raise ProofUnavailable(f"{label} profile build does not match {expected_build}")
    if profile.get("registryRevision") != registry.get("registryRevision"):
        raise ProofUnavailable(f"{label} profile registry revision mismatch")
    if profile.get("registryHash") != registry.get("registryHash"):
        raise ProofUnavailable(f"{label} profile registry hash mismatch")
    if profile.get("authorityArtifactHash") != authority.get("authorityArtifactHash"):
        raise ProofUnavailable(f"{label} profile authority hash mismatch")


def _artifact_binding(document: dict[str, Any], path: Path, revision_field: str, hash_field: str) -> dict[str, Any]:
    return {
        "revision": document[revision_field],
        "canonicalHash": document[hash_field],
        "fileSha256": sha256_bytes(path.read_bytes()),
    }


def _release_spec_binding(path: Path, document: dict[str, Any]) -> dict[str, Any]:
    return {
        "bindingVersion": RELEASE_SPEC_BINDING_VERSION,
        "fileName": path.name,
        "releaseSpecVersion": document["releaseSpecVersion"],
        "canonicalHash": semantic_hash(document),
        "fileSha256": sha256_bytes(path.read_bytes()),
    }


def _equivalence_registry_schema(
    exact_schema: dict[str, Any],
    registry: dict[str, Any],
    target_build: str,
) -> dict[str, Any]:
    schema = copy.deepcopy(exact_schema)
    schema["$id"] = (
        "urn:ccd:pnp:aspx-platform-registry-equivalence:"
        f"spo-online-{target_build}:r1"
    )
    required = list(schema["required"])
    if "admission" not in required:
        required.append("admission")
    schema["required"] = required
    properties = schema["properties"]
    for name in (
        "$schema",
        "registryRevision",
        "registryHash",
        "authoritySourceRef",
        "authoritySourceTag",
        "authorityArtifactHash",
        "authorityArtifactPath",
        "platformBuildMin",
        "platformBuildMax",
    ):
        if name in properties and name in registry:
            properties[name] = {"const": registry[name]}
    properties["admission"] = {
        "type": "object",
        "additionalProperties": False,
        "required": ["mode", "certificateVersion"],
        "properties": {
            "mode": {"const": registry_generator.EQUIVALENCE_ADMISSION_MODE},
            "certificateVersion": {"const": CERTIFICATE_VERSION},
        },
    }
    return schema


def _equivalence_profile_schema(
    exact_schema: dict[str, Any],
    profile: dict[str, Any],
    resource_id: str,
) -> dict[str, Any]:
    schema = copy.deepcopy(exact_schema)
    schema["$id"] = resource_id
    properties = schema["properties"]
    for name, value in profile.items():
        if name in properties and isinstance(properties[name], dict) and "const" in properties[name]:
            properties[name] = {"const": value}
    return schema


def derive_reused_target(
    source_artifacts: dict[str, Any],
    target_collected: dict[str, Any],
    target_release_spec: dict[str, Any],
    output_root: Path,
) -> dict[str, Any]:
    equivalence = target_release_spec.get("equivalenceProof")
    if not isinstance(equivalence, dict):
        raise ProofUnavailable("target release spec has no equivalenceProof contract")
    registry_generator.configure_release(target_release_spec)
    authority = registry_generator.build_authority(
        target_collected["deployRows"],
        target_collected["blobs"],
        target_collected["redirectBlob"],
        target_collected["redirectMap"],
        target_collected["commitTime"],
    )
    source_registry = source_artifacts["registry"]
    registry = copy.deepcopy(source_registry)
    registry.update(
        {
            "$schema": f"../schema/{equivalence['registrySchemaFile']}",
            "registryRevision": equivalence["registryRevision"],
            "registryHash": "",
            "authorityKind": authority["authorityKind"],
            "authoritySourceRef": authority["authoritySourceRef"],
            "authoritySourceTag": authority["authoritySourceTag"],
            "authorityArtifactHash": authority["authorityArtifactHash"],
            "authorityArtifactPath": f"authority/{registry_generator.ARTIFACT_STEM}.authority.json",
            "reviewRef": registry_generator.DEFAULT_INDEPENDENT_REVIEW_REF,
            "generatedAtUtc": authority["authorityCommitTime"],
            "platformBuildMin": registry_generator.PLATFORM_BUILD,
            "platformBuildMax": registry_generator.PLATFORM_BUILD,
            "admission": {
                "mode": registry_generator.EQUIVALENCE_ADMISSION_MODE,
                "certificateVersion": CERTIFICATE_VERSION,
            },
        }
    )
    registry["registryHash"] = registry_generator.object_hash(registry, "registryHash")

    exact_registry_schema_path = output_root / "schema" / "aspx-platform-registry.schema.json"
    exact_profile_schema_path = output_root / "schema" / "aspx-platform-registry-profile.schema.json"
    if not exact_registry_schema_path.is_file() or not exact_profile_schema_path.is_file():
        raise ProofUnavailable("target registry/profile schemas are missing")
    registry_schema = _equivalence_registry_schema(
        load_json(exact_registry_schema_path), registry, registry_generator.PLATFORM_BUILD
    )
    registry_schema_bytes = json_file_bytes(registry_schema)
    registry_schema_hash = sha256_bytes(registry_schema_bytes)

    profile = registry_generator.build_profile(registry, authority, registry_schema_hash)
    profile.update(
        {
            "$schema": f"../schema/{equivalence['profileSchemaFile']}",
            "profileRevision": equivalence["profileRevision"],
            "profileHash": "",
        }
    )
    profile["profileHash"] = registry_generator.object_hash(profile, "profileHash")
    profile_schema = _equivalence_profile_schema(
        load_json(exact_profile_schema_path),
        profile,
        equivalence["profileSchemaResourceId"],
    )
    profile_schema_bytes = json_file_bytes(profile_schema)

    stem = equivalence["registryArtifactStem"]
    paths = {
        "authority": output_root / "authority" / f"{registry_generator.ARTIFACT_STEM}.authority.json",
        "registry": output_root / "registry" / f"{stem}.registry.json",
        "profile": output_root / "profile" / f"{stem}.profile.json",
        "registrySchema": output_root / "schema" / equivalence["registrySchemaFile"],
        "profileSchema": output_root / "schema" / equivalence["profileSchemaFile"],
    }
    return {
        "authority": authority,
        "registry": registry,
        "profile": profile,
        "registrySchema": registry_schema,
        "profileSchema": profile_schema,
        "paths": paths,
        "hashes": {
            "authorityArtifactHash": authority["authorityArtifactHash"],
            "registryHash": registry["registryHash"],
            "registrySchemaHash": registry_schema_hash,
            "profileHash": profile["profileHash"],
            "profileSchemaHash": sha256_bytes(profile_schema_bytes),
        },
    }


def build_reused_target(
    source_artifacts: dict[str, Any],
    target_collected: dict[str, Any],
    target_release_spec: dict[str, Any],
    output_root: Path,
) -> dict[str, Any]:
    target = derive_reused_target(
        source_artifacts, target_collected, target_release_spec, output_root
    )
    equivalence = target_release_spec["equivalenceProof"]
    authority = target["authority"]
    registry = target["registry"]
    profile = target["profile"]
    actual_expected = {
        "authorityArtifactHash": (authority["authorityArtifactHash"], target_release_spec["expectedAuthorityArtifactHash"]),
        "registryHash": (registry["registryHash"], equivalence["expectedRegistryHash"]),
        "registrySchemaHash": (profile["registrySchemaHash"], equivalence["expectedRegistrySchemaHash"]),
        "consumerCompatibilityHash": (profile["consumerCompatibilityHash"], target_release_spec["expectedConsumerCompatibilityHash"]),
        "profileHash": (profile["profileHash"], equivalence["expectedProfileHash"]),
        "profileSchemaHash": (target["hashes"]["profileSchemaHash"], equivalence["expectedProfileSchemaHash"]),
    }
    drift = [f"{key}: expected {expected}, got {actual}" for key, (actual, expected) in actual_expected.items() if actual != expected]
    if drift:
        raise ProofUnavailable("reused target artifacts do not match pinned exact release: " + "; ".join(drift))

    authority_path = target["paths"]["authority"]
    if authority_path.is_file() and authority_path.read_bytes() != json_file_bytes(authority):
        raise ProofUnavailable("existing exact target authority would be mutated")
    registry_generator.write_json(authority_path, authority)
    registry_generator.write_json(target["paths"]["registry"], registry)
    registry_generator.write_json(target["paths"]["profile"], profile)
    registry_generator.write_json(target["paths"]["registrySchema"], target["registrySchema"])
    registry_generator.write_json(target["paths"]["profileSchema"], target["profileSchema"])
    return target


def build_certificate(
    source_collected: dict[str, Any],
    target_collected: dict[str, Any],
    generator: dict[str, Any],
    comparison: dict[str, Any],
    source_artifacts: dict[str, Any],
    target_artifacts: dict[str, Any],
    target_release_spec: dict[str, Any],
    target_release_spec_path: Path,
) -> dict[str, Any]:
    source_paths = source_artifacts["paths"]
    target_paths = target_artifacts["paths"]
    source_registry = source_artifacts["registry"]
    target_registry = target_artifacts["registry"]
    source_payload = registry_payload_hash(source_registry)
    target_payload = registry_payload_hash(target_registry)
    if source_payload != target_payload:
        raise ProofUnavailable("source and target registry payload hashes differ")
    certificate = {
        "certificateVersion": CERTIFICATE_VERSION,
        "certificateHash": "",
        "issuedAtUtc": target_collected["commitTime"],
        "source": {
            "build": source_collected["closure"]["build"],
            "ref": source_collected["closure"]["sourceTag"],
            "commit": source_collected["closure"]["commit"],
            "tree": source_collected["closure"]["tree"],
        },
        "target": {
            "build": target_collected["closure"]["build"],
            "ref": target_collected["closure"]["sourceTag"],
            "commit": target_collected["closure"]["commit"],
            "tree": target_collected["closure"]["tree"],
        },
        "generator": generator,
        "releaseSpec": _release_spec_binding(
            target_release_spec_path, target_release_spec
        ),
        "schemaBindings": {
            "registrySchemaHash": sha256_bytes(target_paths["registrySchema"].read_bytes()),
            "profileSchemaHash": sha256_bytes(target_paths["profileSchema"].read_bytes()),
        },
        "sourceRegistry": {
            **_artifact_binding(source_registry, source_paths["registry"], "registryRevision", "registryHash"),
            "payloadHash": source_payload,
            "authority": _artifact_binding(source_artifacts["authority"], source_paths["authority"], "authoritySchemaVersion", "authorityArtifactHash"),
            "profile": _artifact_binding(source_artifacts["profile"], source_paths["profile"], "profileRevision", "profileHash"),
        },
        "targetRegistry": {
            **_artifact_binding(target_registry, target_paths["registry"], "registryRevision", "registryHash"),
            "payloadHash": target_payload,
            "authority": _artifact_binding(target_artifacts["authority"], target_paths["authority"], "authoritySchemaVersion", "authorityArtifactHash"),
            "profile": _artifact_binding(target_artifacts["profile"], target_paths["profile"], "profileRevision", "profileHash"),
        },
        "sourceClosure": source_collected["closure"],
        "targetClosure": target_collected["closure"],
        "comparison": comparison,
        "proofDisposition": "equivalent_reuse",
    }
    certificate["certificateHash"] = canonical_hash(certificate, "certificateHash")
    return certificate


def validate_certificate(
    certificate: dict[str, Any],
    release_spec_path: Path | None = None,
) -> list[str]:
    errors: list[str] = []
    if certificate.get("certificateVersion") != CERTIFICATE_VERSION:
        errors.append("unsupported certificate version")
    if certificate.get("certificateHash") != canonical_hash(certificate, "certificateHash"):
        errors.append("certificate canonical hash mismatch")
    if certificate.get("proofDisposition") != "equivalent_reuse":
        errors.append("certificate disposition is not equivalent_reuse")
    comparison = certificate.get("comparison")
    if not isinstance(comparison, dict) or comparison.get("equivalent") is not True:
        errors.append("certificate comparison is not equivalent")
    for side in ("source", "target"):
        identity = certificate.get(side)
        if not isinstance(identity, dict):
            errors.append(f"certificate {side} identity is missing")
            continue
        if not GIT_SHA_RE.fullmatch(str(identity.get("commit"))):
            errors.append(f"certificate {side} commit is invalid")
        if not GIT_SHA_RE.fullmatch(str(identity.get("tree"))):
            errors.append(f"certificate {side} tree is invalid")
    source_payload = certificate.get("sourceRegistry", {}).get("payloadHash")
    target_payload = certificate.get("targetRegistry", {}).get("payloadHash")
    if not HASH_RE.fullmatch(str(source_payload)) or source_payload != target_payload:
        errors.append("registry payload binding is not equal")
    generator = certificate.get("generator")
    if not isinstance(generator, dict):
        errors.append("generator identity is missing")
    else:
        if generator.get("generatorVersion") != GENERATOR_VERSION:
            errors.append("generator version mismatch")
        if generator.get("manifestHash") != semantic_hash(generator.get("members")):
            errors.append("generator manifest hash mismatch")
        if not GIT_SHA_RE.fullmatch(str(generator.get("commit"))):
            errors.append("generator commit is invalid")
        if not GIT_SHA_RE.fullmatch(str(generator.get("tree"))):
            errors.append("generator tree is invalid")
    release_spec = certificate.get("releaseSpec")
    if not isinstance(release_spec, dict):
        errors.append("release spec binding is missing")
    else:
        if release_spec.get("bindingVersion") != RELEASE_SPEC_BINDING_VERSION:
            errors.append("release spec binding version mismatch")
        for field in ("canonicalHash", "fileSha256"):
            if not HASH_RE.fullmatch(str(release_spec.get(field))):
                errors.append(f"release spec {field} is invalid")
        if release_spec_path is not None:
            release_spec_document = load_json(release_spec_path)
            if release_spec.get("fileSha256") != sha256_bytes(release_spec_path.read_bytes()):
                errors.append("release spec file hash mismatch")
            if release_spec.get("canonicalHash") != semantic_hash(release_spec_document):
                errors.append("release spec canonical hash mismatch")
            if release_spec.get("releaseSpecVersion") != release_spec_document.get("releaseSpecVersion"):
                errors.append("release spec version mismatch")
    for closure_name in ("sourceClosure", "targetClosure"):
        closure = certificate.get(closure_name)
        if not isinstance(closure, dict) or closure.get("closureHash") != canonical_hash(closure, "closureHash"):
            errors.append(f"{closure_name} hash mismatch")
    for side, closure_name in (("source", "sourceClosure"), ("target", "targetClosure")):
        identity = certificate.get(side, {})
        closure = certificate.get(closure_name, {})
        expected = {
            "build": closure.get("build"),
            "ref": closure.get("sourceTag"),
            "commit": closure.get("commit"),
            "tree": closure.get("tree"),
        }
        if any(identity.get(field) != value for field, value in expected.items()):
            errors.append(f"certificate {side} identity does not match closure")
    return errors


def validate_certificate_chain(
    certificates: list[dict[str, Any]],
    target_build: str,
    target_registry_revision: str,
    admitted_source_revisions: set[str],
    revoked_hashes: set[str] | None = None,
    max_length: int = MAX_CHAIN_LENGTH,
) -> list[str]:
    revoked_hashes = revoked_hashes or set()
    errors: list[str] = []
    if not certificates:
        return ["equivalence certificate chain is missing"]
    if len(certificates) > max_length:
        errors.append("equivalence certificate chain is overlong")
    seen_hashes: set[str] = set()
    expected_build = target_build
    expected_revision = target_registry_revision
    for index, certificate in enumerate(certificates):
        errors.extend(f"certificate[{index}]: {error}" for error in validate_certificate(certificate))
        certificate_hash = certificate.get("certificateHash")
        if certificate_hash in seen_hashes:
            errors.append("equivalence certificate chain contains a cycle")
        seen_hashes.add(str(certificate_hash))
        if certificate_hash in revoked_hashes:
            errors.append("equivalence certificate is revoked")
        if certificate.get("target", {}).get("build") != expected_build:
            errors.append("equivalence certificate target build mismatch")
        if certificate.get("targetRegistry", {}).get("revision") != expected_revision:
            errors.append("equivalence certificate target registry mismatch")
        expected_build = str(certificate.get("source", {}).get("build"))
        expected_revision = str(certificate.get("sourceRegistry", {}).get("revision"))
    if expected_revision not in admitted_source_revisions:
        errors.append("certificate chain does not terminate at an admitted source registry")
    return errors


def run_fallback(args: argparse.Namespace, reason: str, started: float) -> int:
    command = [
        sys.executable,
        str(Path(__file__).with_name("generate_registry.py")),
        "--spocore-repo",
        args.spocore_repo,
        "--git-executable",
        args.git_executable,
        "--release-spec",
        str(args.target_release_spec),
        "--output-root",
        str(args.output_root),
    ]
    fallback_started = time.perf_counter()
    completed = subprocess.run(command, check=False)
    benchmark = {
        "benchmarkVersion": "cross-build-equivalence-benchmark/v1",
        "mode": "full_exact_generation_fallback",
        "proofSeconds": round(fallback_started - started, 6),
        "fullGenerationSeconds": round(time.perf_counter() - fallback_started, 6),
        "fallbackReason": reason,
        "exitCode": completed.returncode,
    }
    write_json(args.benchmark_out, benchmark)
    print(json.dumps(benchmark, sort_keys=True))
    return completed.returncode


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--spocore-repo", required=True)
    parser.add_argument("--git-executable", default="git")
    parser.add_argument("--source-build", required=True)
    parser.add_argument("--source-ref", required=True)
    parser.add_argument("--source-tag", required=True)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--target-release-spec", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--certificate-out", type=Path, required=True)
    parser.add_argument("--benchmark-out", type=Path, required=True)
    parser.add_argument("--generator-repo", default=str(Path(__file__).resolve().parents[2]))
    parser.add_argument("--generator-ref", default="HEAD")
    parser.add_argument("--no-fallback", action="store_true")
    args = parser.parse_args()
    started = time.perf_counter()
    try:
        target_spec = load_json(args.target_release_spec)
        blob_cache: dict[str, bytes] = {}
        semantic_cache: dict[tuple[str, str, str], tuple[Any, list[str]]] = {}
        source_collected = collect_spocore_closure(
            args.git_executable,
            args.spocore_repo,
            args.source_ref,
            args.source_build,
            args.source_tag,
            blob_cache,
            semantic_cache,
        )
        target_collected = collect_spocore_closure(
            args.git_executable,
            args.spocore_repo,
            target_spec["authorityRef"],
            target_spec["platformBuild"],
            target_spec["authorityTag"],
            blob_cache,
            semantic_cache,
        )
        comparison = compare_closures(source_collected["closure"], target_collected["closure"])
        if not comparison["equivalent"]:
            raise ProofUnavailable("input closures are not exactly equivalent")
        generator = collect_generator_identity(
            args.git_executable, args.generator_repo, args.generator_ref
        )
        source_artifacts = source_release_artifacts(args.source_root, args.source_build)
        validate_release_artifact_build("source", source_artifacts, args.source_build)
        target_artifacts = build_reused_target(
            source_artifacts, target_collected, target_spec, args.output_root
        )
        validate_release_artifact_build(
            "target", target_artifacts, target_spec["platformBuild"]
        )
        certificate = build_certificate(
            source_collected,
            target_collected,
            generator,
            comparison,
            source_artifacts,
            target_artifacts,
            target_spec,
            args.target_release_spec,
        )
        errors = validate_certificate(certificate, args.target_release_spec)
        if errors:
            raise ProofUnavailable("generated certificate is invalid: " + "; ".join(errors))
        write_json(args.certificate_out, certificate)
        benchmark = {
            "benchmarkVersion": "cross-build-equivalence-benchmark/v1",
            "mode": "equivalence_reuse",
            "proofSeconds": round(time.perf_counter() - started, 6),
            "fullGenerationSeconds": None,
            "fallbackReason": None,
            "exitCode": 0,
        }
        write_json(args.benchmark_out, benchmark)
        print(
            json.dumps(
                {
                    **benchmark,
                    "certificateHash": certificate["certificateHash"],
                    "sourceClosureHash": source_collected["closure"]["closureHash"],
                    "targetClosureHash": target_collected["closure"]["closureHash"],
                    "registryPayloadHash": certificate["sourceRegistry"]["payloadHash"],
                    "entryCount": target_artifacts["registry"]["entryCount"],
                },
                sort_keys=True,
            )
        )
        return 0
    except (KeyError, OSError, ValueError, ProofUnavailable) as error:
        if args.no_fallback:
            print(f"equivalence proof failed closed: {error}", file=sys.stderr)
            return 2
        return run_fallback(args, str(error), started)


if __name__ == "__main__":
    raise SystemExit(main())
