#!/usr/bin/env python3

from __future__ import annotations

import copy
import hashlib
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RELEASE = ROOT / "versioned" / "spo-online-16.0.27709.12000"
sys.path.insert(0, str(ROOT))

import generate_registry as generator  # noqa: E402
import cross_build_equivalence as equivalence  # noqa: E402
import validate_registry as validator  # noqa: E402


class ExactBuild27709ReleaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.spec = validator.load_json(RELEASE / "release-spec.json")
        generator.configure_release(cls.spec)
        validator.configure_validation_release(cls.spec)
        cls.authority = validator.load_json(
            RELEASE / "authority" / "spo-online-16.0.27709.12000.authority.json"
        )
        cls.registry = validator.load_json(
            RELEASE / "registry" / "spo-online-16.0.27709.12000.registry.json"
        )
        cls.profile = validator.load_json(
            RELEASE / "profile" / "spo-online-16.0.27709.12000.profile.json"
        )
        cls.schema_path = RELEASE / "schema" / "aspx-platform-registry.schema.json"
        cls.schema = validator.load_json(cls.schema_path)
        cls.profile_schema = validator.load_json(
            RELEASE / "schema" / "aspx-platform-registry-profile.schema.json"
        )
        cls.fixtures = validator.load_json(RELEASE / "fixtures" / "contract-cases.json")

    def test_exact_source_authority_and_release_are_valid(self) -> None:
        schema_hash = validator.artifact_sha256(self.schema_path)
        self.assertEqual([], validator.schema_validation_errors(self.registry, self.schema))
        self.assertEqual(
            [], validator.schema_validation_errors(self.profile, self.profile_schema)
        )
        self.assertEqual([], validator.validate_authority(self.authority))
        self.assertEqual([], validator.validate_registry(self.registry, self.authority))
        self.assertEqual(
            [],
            validator.validate_profile(
                self.profile, self.registry, self.authority, schema_hash
            ),
        )
        self.assertEqual("ab4856999051acfe946fab5632b45ce6427287aa", self.authority["authoritySourceRef"])
        self.assertEqual("release/16.0.27709.12000", self.authority["authoritySourceTag"])
        self.assertEqual(1161, self.registry["entryCount"])

    def test_build_binding_is_exact_and_negative_semantics_fail_closed(self) -> None:
        self.assertEqual("16.0.27709.12000", self.registry["platformBuildMin"])
        self.assertEqual("16.0.27709.12000", self.registry["platformBuildMax"])
        self.assertEqual("16.0.27709.12000", self.profile["platformBuild"])
        schema_hash = validator.artifact_sha256(self.schema_path)
        for build, reason in [
            ("unknown", "UNKNOWN_PLATFORM_BUILD"),
            ("16.0.27708.12757", "INCOMPATIBLE_PLATFORM_BUILD"),
        ]:
            with self.subTest(build=build):
                request = copy.deepcopy(self.fixtures["readerTemplate"])
                request["platformBuild"] = build
                actual = validator.evaluate_registry_request(
                    self.registry,
                    self.profile,
                    request,
                    self.authority,
                    self.schema,
                    schema_hash,
                )
                self.assertEqual("Unknown", actual["verdict"])
                self.assertEqual(reason, actual["reasonCode"])

    def test_all_schema_collision_volume_and_drift_fixtures_pass(self) -> None:
        receipts = validator.evaluate_fixture_suite(
            self.fixtures,
            self.registry,
            self.authority,
            self.profile,
            self.schema,
            validator.artifact_sha256(self.schema_path),
            RELEASE / "fixtures",
        )
        failures = [row for row in receipts["results"] if not row["passed"]]
        self.assertEqual([], failures)
        self.assertEqual(58, receipts["caseCount"])
        self.assertEqual(58, receipts["passCount"])

    def test_prior_release_files_remain_byte_immutable(self) -> None:
        immutable = validator.load_json(RELEASE / "old-release-immutability.json")
        for relative, expected in immutable["files"].items():
            with self.subTest(path=relative):
                actual = hashlib.sha256((ROOT / relative).read_bytes()).hexdigest()
                self.assertEqual(expected, actual)

    def test_new_authority_is_independent_even_when_bounded_source_blobs_match(self) -> None:
        old = validator.load_json(
            ROOT / "authority" / "spo-online-16.0.27708.12757.authority.json"
        )
        self.assertNotEqual(old["authoritySourceRef"], self.authority["authoritySourceRef"])
        self.assertNotEqual(old["authorityArtifactHash"], self.authority["authorityArtifactHash"])
        self.assertEqual(old["deployRows"], self.authority["deployRows"])
        self.assertEqual(old["legacyLcidRedirects"], self.authority["legacyLcidRedirects"])
        self.assertEqual(
            old["sourceArtifacts"]["shippingManifests"],
            self.authority["sourceArtifacts"]["shippingManifests"],
        )
        self.assertEqual(
            old["sourceArtifacts"]["legacyLcidRedirectMap"],
            self.authority["sourceArtifacts"]["legacyLcidRedirectMap"],
        )
        virtual_sources = self.authority["sourceArtifacts"]["virtualHandlerSources"]
        self.assertEqual(2, len(virtual_sources))
        for source in virtual_sources:
            self.assertRegex(source["blobId"], r"^[0-9a-f]{40}$")
            self.assertTrue(source["symbols"])

    def test_equivalence_variant_is_explicit_schema_valid_and_release_spec_bound(self) -> None:
        equivalence_registry = validator.load_json(
            RELEASE / "registry" / "spo-online-16.0.27709.12000-equivalence.registry.json"
        )
        equivalence_profile = validator.load_json(
            RELEASE / "profile" / "spo-online-16.0.27709.12000-equivalence.profile.json"
        )
        equivalence_schema = validator.load_json(
            RELEASE / "schema" / "aspx-platform-registry-equivalence.schema.json"
        )
        equivalence_profile_schema = validator.load_json(
            RELEASE / "schema" / "aspx-platform-registry-equivalence-profile.schema.json"
        )
        certificate = validator.load_json(
            RELEASE / "certificates" / "27708-to-27709.cross-build-equivalence.json"
        )
        self.assertEqual(
            generator.EQUIVALENCE_ADMISSION_MODE,
            equivalence_registry["admission"]["mode"],
        )
        self.assertEqual(
            [], validator.schema_validation_errors(equivalence_registry, equivalence_schema)
        )
        self.assertEqual(
            [], validator.schema_validation_errors(
                equivalence_profile, equivalence_profile_schema
            )
        )
        self.assertEqual(
            [], equivalence.validate_certificate(certificate, RELEASE / "release-spec.json")
        )
        self.assertEqual(219, len(certificate["sourceClosure"]["members"]))
        self.assertEqual(219, len(certificate["targetClosure"]["members"]))
        self.assertIn(
            "otools/deploy/packages/microsoft.sharepoint.warehouse.template_14.xml",
            {member["path"] for member in certificate["targetClosure"]["members"]},
        )
        self.assertNotIn("admission", self.registry)

    def test_offline_cli_receipt_binds_managed_assemblies_and_fail_closed_stages(self) -> None:
        receipt = validator.load_json(RELEASE / "offline-assessment-compatibility.json")
        self.assertEqual("ccd516-offline-assessment-compatibility/v2", receipt["schema"])
        self.assertEqual(
            "3012555317d5a8ee981b9e103206f3f0680333d8",
            receipt["assessmentSource"]["commit"],
        )
        self.assertEqual(
            "f7b7ae7cea232a067734319ff9124e31ce00c921",
            receipt["assessmentSource"]["tree"],
        )
        self.assertEqual(
            "1f07296b186698c3cc9ca8580f00af36c0f3f4f5",
            receipt["pnpCoreSource"]["commit"],
        )
        self.assertEqual("8.0.425", receipt["build"]["actualSdk"])
        self.assertFalse(receipt["build"]["restoreStarted"])
        self.assertEqual(0, receipt["build"]["exitCode"])
        self.assertEqual("8.0.31", receipt["runtime"]["microsoftNetCoreApp"])
        self.assertEqual("8.0.31", receipt["runtime"]["microsoftAspNetCoreApp"])
        for name in [
            "microsoft365-assessment.exe",
            "microsoft365-assessment.dll",
            "PnP.Scanning.Core.dll",
            "PnP.Core.dll",
            "microsoft365-assessment.runtimeconfig.json",
        ]:
            self.assertRegex(receipt["binaries"][name], r"^[0-9a-f]{64}$")
        self.assertNotEqual(
            "028c1736a274de013d540b50d8681fda2855fbab7607cf656499c8694b2de956",
            receipt["binaries"]["microsoft365-assessment.exe"],
        )

        cases = {row["name"]: row for row in receipt["cases"]}
        self.assertEqual(
            {"exact-current", "prior-incompatible", "unknown"}, set(cases)
        )
        current = cases["exact-current"]
        self.assertEqual("16.0.27709.12000", current["platformBuild"])
        self.assertFalse(current["registryBindingRejected"])
        self.assertEqual("authentication.certificate_load", current["terminalStage"])
        for name in ["prior-incompatible", "unknown"]:
            self.assertTrue(cases[name]["registryBindingRejected"])
            self.assertEqual("registry_binding", cases[name]["terminalStage"])
            self.assertIn("registry_or_platform_binding", cases[name]["stdoutLines"])
        for case in cases.values():
            self.assertEqual(1, case["exitCode"])
            self.assertFalse(case["providerInvoked"])
            self.assertFalse(case["networkAcquisitionStarted"])
            self.assertFalse(case["pageChainStarted"])
            self.assertEqual([], case["outputFilesPresent"])
            self.assertRegex(case["stdoutSha256"], r"^[0-9a-f]{64}$")
            self.assertRegex(case["stderrSha256"], r"^[0-9a-f]{64}$")

        self.assertEqual(
            [
                "physical.sqlite",
                "physical.json",
                "reference.sqlite",
                "reference.json",
                "aggregate.json",
            ],
            receipt["expectedOutputFiles"],
        )
        self.assertIn("--platform-build", receipt["commandContract"]["caseSpecificArgv"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
