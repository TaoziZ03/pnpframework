#!/usr/bin/env python3

from __future__ import annotations

import copy
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

import cross_build_equivalence as equivalence  # noqa: E402


class CrossBuildEquivalenceTests(unittest.TestCase):
    def member(self, path: str = "otools/deploy/a.xml", seed: str = "a") -> dict:
        return {
            "path": path,
            "pathCaseFold": path.casefold(),
            "kind": "shipping_manifest",
            "blobId": seed * 40,
            "sha256": seed * 64,
            "byteLength": 1,
            "parserId": equivalence.DEPLOY_PARSER,
            "semanticHash": seed * 64,
        }

    def closure(self, build: str, member: dict | None = None) -> dict:
        value = {
            "closureVersion": equivalence.CLOSURE_VERSION,
            "build": build,
            "sourceTag": f"release/{build}",
            "commit": "1" * 40,
            "tree": "2" * 40,
            "selection": {"rule": "fixture"},
            "members": [member or self.member()],
            "closureHash": "",
        }
        value["closureHash"] = equivalence.canonical_hash(value, "closureHash")
        return value

    def certificate(self) -> dict:
        source_closure = self.closure("16.0.1.1")
        target_closure = self.closure("16.0.1.2")
        generator_members = [
            {
                "path": "generate_registry.py",
                "blobId": "3" * 40,
                "sha256": "4" * 64,
                "byteLength": 1,
                "parserId": equivalence.GENERATOR_PARSER,
            }
        ]
        binding = {
            "revision": "registry-r1",
            "canonicalHash": "5" * 64,
            "fileSha256": "6" * 64,
            "payloadHash": "7" * 64,
            "authority": {
                "revision": "authority/v1",
                "canonicalHash": "8" * 64,
                "fileSha256": "9" * 64,
            },
            "profile": {
                "revision": "profile-r1",
                "canonicalHash": "a" * 64,
                "fileSha256": "b" * 64,
            },
        }
        value = {
            "certificateVersion": equivalence.CERTIFICATE_VERSION,
            "certificateHash": "",
            "issuedAtUtc": "2026-09-11T00:00:00Z",
            "source": {
                "build": "16.0.1.1",
                "ref": "release/16.0.1.1",
                "commit": "1" * 40,
                "tree": "2" * 40,
            },
            "target": {
                "build": "16.0.1.2",
                "ref": "release/16.0.1.2",
                "commit": "1" * 40,
                "tree": "2" * 40,
            },
            "generator": {
                "generatorVersion": equivalence.GENERATOR_VERSION,
                "commit": "3" * 40,
                "tree": "4" * 40,
                "canonicalization": "canonical-json/sorted-utf8-no-whitespace/v1",
                "members": generator_members,
                "manifestHash": equivalence.semantic_hash(generator_members),
            },
            "releaseSpec": {
                "bindingVersion": equivalence.RELEASE_SPEC_BINDING_VERSION,
                "fileName": "release-spec.json",
                "releaseSpecVersion": "aspx-platform-registry-release-spec/v1",
                "canonicalHash": "e" * 64,
                "fileSha256": "f" * 64,
            },
            "schemaBindings": {
                "registrySchemaHash": "c" * 64,
                "profileSchemaHash": "d" * 64,
            },
            "sourceRegistry": binding,
            "targetRegistry": {**copy.deepcopy(binding), "revision": "registry-r2"},
            "sourceClosure": source_closure,
            "targetClosure": target_closure,
            "comparison": {
                "equivalent": True,
                "sourceMemberCount": 1,
                "targetMemberCount": 1,
                "added": [],
                "deleted": [],
                "changed": [],
                "renameOrAliasCandidates": [],
                "caseChanges": [],
                "uncertainties": [],
            },
            "proofDisposition": "equivalent_reuse",
        }
        value["certificateHash"] = equivalence.canonical_hash(value, "certificateHash")
        return value

    def test_exact_closure_equivalence_and_real_release_payload_match(self) -> None:
        source = self.closure("16.0.1.1")
        target = self.closure("16.0.1.2")
        self.assertTrue(equivalence.compare_closures(source, target)["equivalent"])

        old_registry = equivalence.load_json(
            ROOT / "registry" / "spo-online-16.0.27708.12757.registry.json"
        )
        new_registry = equivalence.load_json(
            ROOT
            / "versioned"
            / "spo-online-16.0.27709.12000"
            / "registry"
            / "spo-online-16.0.27709.12000.registry.json"
        )
        self.assertEqual(1161, old_registry["entryCount"])
        self.assertEqual(
            equivalence.registry_payload_hash(old_registry),
            equivalence.registry_payload_hash(new_registry),
        )

    def test_one_byte_added_deleted_parser_and_case_changes_fail_closed(self) -> None:
        source = self.closure("16.0.1.1")

        one_byte = self.closure("16.0.1.2", self.member(seed="b"))
        result = equivalence.compare_closures(source, one_byte)
        self.assertFalse(result["equivalent"])
        self.assertEqual(["blobId", "sha256", "semanticHash"], result["changed"][0]["fields"])

        added = self.closure("16.0.1.2")
        added["members"].append(self.member("otools/deploy/b.xml", "b"))
        self.assertEqual(["otools/deploy/b.xml"], equivalence.compare_closures(source, added)["added"])

        deleted = self.closure("16.0.1.2")
        deleted["members"] = []
        self.assertEqual(["otools/deploy/a.xml"], equivalence.compare_closures(source, deleted)["deleted"])

        parser_change = self.closure("16.0.1.2")
        parser_change["members"][0]["parserId"] = "otools-deploy-xml/v3"
        self.assertIn("parserId", equivalence.compare_closures(source, parser_change)["changed"][0]["fields"])

        case_change = self.closure("16.0.1.2", self.member("otools/deploy/A.xml"))
        case_result = equivalence.compare_closures(source, case_change)
        self.assertFalse(case_result["equivalent"])
        self.assertEqual(
            [{"from": "otools/deploy/a.xml", "to": "otools/deploy/A.xml"}],
            case_result["caseChanges"],
        )

    def test_unresolved_include_and_manifest_parser_behavior_fail_closed(self) -> None:
        with self.assertRaises(equivalence.ProofUnavailable):
            equivalence._include_paths("<root><Import /></root>", "otools/deploy/a.xml")
        with self.assertRaises(equivalence.ProofUnavailable):
            equivalence._xml_semantics(
                "otools/deploy/a.xml", b"<root>", equivalence.DEPLOY_PARSER
            )

    def test_shared_generator_enumerator_captures_nested_input_mutation(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            repo = Path(directory)
            git = "/usr/bin/git"
            git_environment = {
                **os.environ,
                "GIT_AUTHOR_NAME": "CCD Test",
                "GIT_AUTHOR_EMAIL": "ccd@example.invalid",
                "GIT_COMMITTER_NAME": "CCD Test",
                "GIT_COMMITTER_EMAIL": "ccd@example.invalid",
            }
            files = {
                "otools/deploy/a.xml": (
                    '<Root><File dest="FILES\\Program Files\\Common Files\\Microsoft Shared\\'
                    'Web Server Extensions\\16\\TEMPLATE\\LAYOUTS\\a.aspx" /></Root>'
                ),
                "otools/deploy/packages/microsoft.sharepoint.warehouse.template_14.xml":
                    "<Root><Package Name=\"warehouse\" /></Root>",
                equivalence.REDIRECT_PATH: "<file>redirect.aspx</file>",
                equivalence.VIRTUAL_PATHS[0]: "class SPLayoutsMappedFile { SPVirtualPathProvider provider; }",
                equivalence.VIRTUAL_PATHS[1]: "class SPVirtualFile { }",
            }
            for relative, content in files.items():
                path = repo / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            subprocess.run([git, "init", "-q", str(repo)], check=True)
            subprocess.run([git, "-C", str(repo), "add", "."], check=True)
            subprocess.run(
                [git, "-C", str(repo), "commit", "-qm", "source"],
                check=True, env=git_environment,
            )
            source_ref = subprocess.run(
                [git, "-C", str(repo), "rev-parse", "HEAD"],
                check=True, capture_output=True, text=True,
            ).stdout.strip()
            nested = repo / "otools/deploy/packages/microsoft.sharepoint.warehouse.template_14.xml"
            nested.write_text("<Root><Package Name=\"warehouse-v2\" /></Root>", encoding="utf-8")
            subprocess.run([git, "-C", str(repo), "add", str(nested)], check=True)
            subprocess.run(
                [git, "-C", str(repo), "commit", "-qm", "target"],
                check=True, env=git_environment,
            )
            target_ref = subprocess.run(
                [git, "-C", str(repo), "rev-parse", "HEAD"],
                check=True, capture_output=True, text=True,
            ).stdout.strip()

            source = equivalence.collect_spocore_closure(
                git, str(repo), source_ref, "16.0.1.1", "release/16.0.1.1"
            )["closure"]
            target = equivalence.collect_spocore_closure(
                git, str(repo), target_ref, "16.0.1.2", "release/16.0.1.2"
            )["closure"]
            nested_path = "otools/deploy/packages/microsoft.sharepoint.warehouse.template_14.xml"
            self.assertIn(nested_path, {member["path"] for member in source["members"]})
            comparison = equivalence.compare_closures(source, target)
            self.assertFalse(comparison["equivalent"])
            self.assertEqual(nested_path, comparison["changed"][0]["path"])

    def test_source_build_must_match_admitted_profile_and_exact_artifacts(self) -> None:
        artifacts = {
            "registry": {
                "platformBuildMin": "16.0.1.1",
                "platformBuildMax": "16.0.1.1",
                "registryRevision": "registry-r1",
                "registryHash": "a" * 64,
            },
            "authority": {
                "platformBuildMin": "16.0.1.1",
                "platformBuildMax": "16.0.1.1",
                "authorityArtifactHash": "b" * 64,
            },
            "profile": {
                "platformBuild": "16.0.9.9",
                "registryRevision": "registry-r1",
                "registryHash": "a" * 64,
                "authorityArtifactHash": "b" * 64,
            },
        }
        with self.assertRaisesRegex(
            equivalence.ProofUnavailable, "source profile build"
        ):
            equivalence.validate_release_artifact_build(
                "source", artifacts, "16.0.1.1"
            )

    def test_certificate_tamper_generator_change_wrong_target_revocation_cycle_and_length(self) -> None:
        certificate = self.certificate()
        self.assertEqual([], equivalence.validate_certificate(certificate))

        tampered = copy.deepcopy(certificate)
        tampered["target"]["build"] = "16.0.1.3"
        self.assertIn("certificate canonical hash mismatch", equivalence.validate_certificate(tampered))

        generator_change = copy.deepcopy(certificate)
        generator_change["generator"]["members"][0]["sha256"] = "e" * 64
        generator_change["certificateHash"] = equivalence.canonical_hash(generator_change, "certificateHash")
        self.assertIn("generator manifest hash mismatch", equivalence.validate_certificate(generator_change))

        source_build_drift = copy.deepcopy(certificate)
        source_build_drift["source"]["build"] = "16.0.9.9"
        source_build_drift["certificateHash"] = equivalence.canonical_hash(
            source_build_drift, "certificateHash"
        )
        self.assertIn(
            "certificate source identity does not match closure",
            equivalence.validate_certificate(source_build_drift),
        )

        missing_release_spec = copy.deepcopy(certificate)
        del missing_release_spec["releaseSpec"]
        missing_release_spec["certificateHash"] = equivalence.canonical_hash(
            missing_release_spec, "certificateHash"
        )
        self.assertIn(
            "release spec binding is missing",
            equivalence.validate_certificate(missing_release_spec),
        )

        wrong_target = equivalence.validate_certificate_chain(
            [certificate], "16.0.1.3", "registry-r2", {"registry-r1"}
        )
        self.assertIn("equivalence certificate target build mismatch", wrong_target)

        revoked = equivalence.validate_certificate_chain(
            [certificate],
            "16.0.1.2",
            "registry-r2",
            {"registry-r1"},
            {certificate["certificateHash"]},
        )
        self.assertIn("equivalence certificate is revoked", revoked)

        cycle = equivalence.validate_certificate_chain(
            [certificate, certificate],
            "16.0.1.2",
            "registry-r2",
            {"registry-r1"},
        )
        self.assertIn("equivalence certificate chain contains a cycle", cycle)

        overlong = equivalence.validate_certificate_chain(
            [certificate, certificate],
            "16.0.1.2",
            "registry-r2",
            {"registry-r1"},
            max_length=1,
        )
        self.assertIn("equivalence certificate chain is overlong", overlong)

    def test_failed_proof_explicitly_invokes_full_exact_generation(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            benchmark = Path(directory) / "benchmark.json"
            args = SimpleNamespace(
                spocore_repo="Q:/spocore/src",
                git_executable="git",
                target_release_spec=Path("release-spec.json"),
                output_root=Path("target-release"),
                benchmark_out=benchmark,
            )
            completed = SimpleNamespace(returncode=0)
            with mock.patch.object(equivalence.subprocess, "run", return_value=completed) as run:
                actual = equivalence.run_fallback(
                    args, "one-byte input change", time.perf_counter()
                )
            self.assertEqual(0, actual)
            command = run.call_args.args[0]
            self.assertTrue(str(command[1]).endswith("generate_registry.py"))
            self.assertIn("--release-spec", command)
            receipt = equivalence.load_json(benchmark)
            self.assertEqual("full_exact_generation_fallback", receipt["mode"])
            self.assertEqual("one-byte input change", receipt["fallbackReason"])
            self.assertEqual(0, receipt["exitCode"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
