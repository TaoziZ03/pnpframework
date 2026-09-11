#!/usr/bin/env python3

from __future__ import annotations

import copy
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
