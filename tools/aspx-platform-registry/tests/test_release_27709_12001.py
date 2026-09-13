#!/usr/bin/env python3

from __future__ import annotations

import copy
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
RELEASE = ROOT / "versioned" / "spo-online-16.0.27709.12001"
sys.path.insert(0, str(ROOT))

import generate_registry as generator  # noqa: E402
import validate_registry as validator  # noqa: E402


class ExactBuild2770912001ReleaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.spec = validator.load_json(RELEASE / "release-spec.json")
        generator.configure_release(cls.spec)
        validator.configure_validation_release(cls.spec)
        cls.authority = validator.load_json(
            RELEASE / "authority" / "spo-online-16.0.27709.12001.authority.json"
        )
        cls.registry = validator.load_json(
            RELEASE / "registry" / "spo-online-16.0.27709.12001.registry.json"
        )
        cls.profile = validator.load_json(
            RELEASE / "profile" / "spo-online-16.0.27709.12001.profile.json"
        )
        cls.acquisition_profile = validator.load_json(
            RELEASE
            / "profile"
            / "spo-online-16.0.27709.12001-acquisition-consumer.profile.json"
        )
        cls.registry_schema_path = (
            RELEASE / "schema" / "aspx-platform-registry.schema.json"
        )
        cls.registry_schema = validator.load_json(cls.registry_schema_path)
        cls.profile_schema = validator.load_json(
            RELEASE / "schema" / "aspx-platform-registry-profile.schema.json"
        )
        cls.acquisition_profile_schema = validator.load_json(
            RELEASE / "schema" / "aspx-acquisition-consumer-profile.schema.json"
        )
        cls.v1_fixtures = validator.load_json(
            RELEASE / "fixtures" / "contract-cases.json"
        )
        cls.v2_fixtures = validator.load_json(
            RELEASE / "fixtures" / "contract-cases-v2.json"
        )

    def test_exact_authority_registry_and_profiles_are_closed(self) -> None:
        schema_hash = validator.artifact_sha256(self.registry_schema_path)
        self.assertEqual(
            [], validator.schema_validation_errors(self.registry, self.registry_schema)
        )
        self.assertEqual(
            [], validator.schema_validation_errors(self.profile, self.profile_schema)
        )
        self.assertEqual(
            [],
            validator.schema_validation_errors(
                self.acquisition_profile, self.acquisition_profile_schema
            ),
        )
        self.assertEqual([], validator.validate_authority(self.authority))
        self.assertEqual(
            [], validator.validate_registry(self.registry, self.authority)
        )
        self.assertEqual(
            [],
            validator.validate_profile(
                self.profile, self.registry, self.authority, schema_hash
            ),
        )
        self.assertEqual(
            [],
            validator.validate_acquisition_consumer_profile(
                self.acquisition_profile, self.registry, self.profile
            ),
        )
        self.assertEqual(
            "45ad38aee279ef47f54dc04dfe8eb9fe4cfe5d01",
            self.authority["authoritySourceRef"],
        )
        self.assertEqual(
            "release/16.0.27709.12001", self.authority["authoritySourceTag"]
        )
        self.assertEqual("2026-09-12T02:14:11Z", self.authority["authorityCommitTime"])
        self.assertEqual(1161, self.registry["entryCount"])

    def test_build_binding_is_exact_and_never_becomes_a_range(self) -> None:
        self.assertEqual("16.0.27709.12001", self.registry["platformBuildMin"])
        self.assertEqual("16.0.27709.12001", self.registry["platformBuildMax"])
        self.assertEqual("16.0.27709.12001", self.profile["platformBuild"])
        schema_hash = validator.artifact_sha256(self.registry_schema_path)
        for build, reason in [
            ("unknown", "UNKNOWN_PLATFORM_BUILD"),
            ("16.0.27709.12000", "INCOMPATIBLE_PLATFORM_BUILD"),
            ("16.0.27709.12002", "INCOMPATIBLE_PLATFORM_BUILD"),
        ]:
            with self.subTest(build=build):
                request = copy.deepcopy(self.v1_fixtures["readerTemplate"])
                request["platformBuild"] = build
                actual = validator.evaluate_registry_request(
                    self.registry,
                    self.profile,
                    request,
                    self.authority,
                    self.registry_schema,
                    schema_hash,
                )
                self.assertEqual("Unknown", actual["verdict"])
                self.assertEqual(reason, actual["reasonCode"])

    def test_setup_virtual_and_acquisition_volumes_remain_separate(self) -> None:
        self.assertEqual(
            {"SetupLayoutApplicationPage", "VirtualMappedRequest"},
            {entry["surfaceKind"] for entry in self.registry["entries"]},
        )
        self.assertEqual(
            {
                "SetupArtifact.AspxApplicationPage",
                "VirtualHandler.SPLayoutsMappedFile",
            },
            {entry["handlerOrArtifactType"] for entry in self.registry["entries"]},
        )
        self.assertFalse(
            any(
                entry["downstreamDisposition"].startswith("LinkedPhysical")
                for entry in self.registry["entries"]
            )
        )
        self.assertEqual(
            "aspx-discovery-output/v2",
            self.profile["volumeCompatibility"]["physicalOutput"],
        )
        self.assertEqual(
            "aspx-reference-output/v1",
            self.profile["volumeCompatibility"]["referenceOutput"],
        )
        self.assertEqual([], validator.validate_registry(self.registry, self.authority))

    def test_current_assessment_v2_consumer_is_exactly_pinned(self) -> None:
        dispatches = {
            dispatch["name"]: dispatch
            for dispatch in self.acquisition_profile["dispatches"]
        }
        self.assertEqual(
            "pnp/assessment@a33e21eed513e470cfbbb4af4451cb1b37880d0e",
            dispatches["assessment-v2"]["productRef"],
        )
        self.assertEqual(
            "pnp/assessment@3012555317d5a8ee981b9e103206f3f0680333d8",
            dispatches["assessment-v1"]["productRef"],
        )
        self.assertEqual(
            "07cfec44626f8617e63d34e270a4fc7f3baf761c",
            self.v2_fixtures["fixtureProvenance"]["assessmentConsumerTree"],
        )
        aggregate = validator.load_json(
            RELEASE / "fixtures" / "volumes" / "aggregate-output-v2.json"
        )
        self.assertEqual(
            "1f07296b186698c3cc9ca8580f00af36c0f3f4f5",
            aggregate["sdkRef"],
        )

    def test_legacy_and_current_adverse_matrices_pass_deterministically(self) -> None:
        schema_hash = validator.artifact_sha256(self.registry_schema_path)
        v1_receipt = validator.evaluate_fixture_suite(
            self.v1_fixtures,
            self.registry,
            self.authority,
            self.profile,
            self.registry_schema,
            schema_hash,
            RELEASE / "fixtures",
        )
        v2_receipt = validator.evaluate_fixture_suite(
            self.v2_fixtures,
            self.registry,
            self.authority,
            self.profile,
            self.registry_schema,
            schema_hash,
            RELEASE / "fixtures",
            self.acquisition_profile,
        )
        self.assertEqual((58, 58), (v1_receipt["caseCount"], v1_receipt["passCount"]))
        self.assertEqual((23, 23), (v2_receipt["caseCount"], v2_receipt["passCount"]))
        self.assertEqual(
            v2_receipt,
            validator.evaluate_fixture_suite(
                self.v2_fixtures,
                self.registry,
                self.authority,
                self.profile,
                self.registry_schema,
                schema_hash,
                RELEASE / "fixtures",
                self.acquisition_profile,
            ),
        )
        reasons = {
            result["id"]: result["actualReasonCode"]
            for result in v2_receipt["results"]
        }
        self.assertEqual("PRODUCER_REF_UNSUPPORTED", reasons["V2-PRODUCER-V1-AS-V2"])
        self.assertEqual(
            "PRODUCER_REF_UNSUPPORTED", reasons["V2-PRODUCER-PREVIOUS-V2"]
        )
        self.assertEqual("TERMINAL_VOLUME_MISSING", reasons["V2-TERMINAL-MISSING"])
        self.assertEqual(
            "PAGINATION_SENSITIVE_INPUT_REJECTED", reasons["V2-AUTHORIZATION"]
        )

    def test_new_release_is_not_derived_from_the_previous_registry(self) -> None:
        prior = validator.load_json(
            ROOT
            / "versioned"
            / "spo-online-16.0.27709.12000"
            / "authority"
            / "spo-online-16.0.27709.12000.authority.json"
        )
        self.assertNotEqual(
            prior["authoritySourceRef"], self.authority["authoritySourceRef"]
        )
        self.assertNotEqual(
            prior["authorityArtifactHash"], self.authority["authorityArtifactHash"]
        )
        virtual_sources = self.authority["sourceArtifacts"]["virtualHandlerSources"]
        self.assertEqual(2, len(virtual_sources))
        for source in virtual_sources:
            self.assertRegex(source["blobId"], r"^[0-9a-f]{40}$")
            self.assertTrue(source["symbols"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
