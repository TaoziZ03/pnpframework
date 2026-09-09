using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class RuntimeVerificationAssertionContractTests
    {
        private const string DigestA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string DigestB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string DigestC = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        private const string IngredientId = "ccd.ingredient.webpart.instance/v1:source:search";
        private const string ActionId = "action:" + IngredientId;
        private const string PlanDigest = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
        private const string TargetIdentity = "https://a830edad9050849cupcollect.sharepoint.com/sites/target/Pages/Search.aspx";

        [TestMethod]
        public void LegacyManifestAndReceiptRemainReadableWithoutV2Fields()
        {
            var manifest = new RuntimeVerificationManifest
            {
                Requirements = new List<RuntimeVerificationRequirement>
                {
                    new RuntimeVerificationRequirement { Id = "page-reachability" }
                }
            };
            RuntimeVerificationContractValidator.ValidateManifest(manifest, null, null);

            var json = MigrationContractSerializer.SerializeCanonical(manifest);
            Assert.IsFalse(json.Contains("assertions"));

            RuntimeVerificationContractValidator.ValidateReceipt(
                manifest,
                new RuntimeVerificationReceipt
                {
                    PlanDigest = PlanDigest,
                    TargetIdentity = TargetIdentity,
                    CompletedAtUtc = DateTimeOffset.Parse("2026-09-09T10:02:00Z")
                },
                PlanDigest,
                TargetIdentity,
                DateTimeOffset.Parse("2026-09-09T10:00:00Z"));
        }

        [TestMethod]
        public void V2AssertionCarriesIntentAndCanonicalReferencesWithoutCreatingAnIngredientKind()
        {
            var manifest = Manifest();
            RuntimeVerificationContractValidator.ValidateManifest(manifest, Graph(), Actions());

            var json = MigrationContractSerializer.SerializeCanonical(manifest);
            StringAssert.Contains(json, "interaction.search-submit");
            StringAssert.Contains(json, ActionId);
            StringAssert.Contains(json, "expectedFinalState");
            Assert.IsFalse(json.Contains("pageIngredientKind"));
            Assert.IsFalse(json.Contains("primaryOwnerLane"));
            Assert.IsFalse(json.Contains("materializer"));
        }

        [TestMethod]
        public void V2AssertionRejectsMissingOrMismatchedAttachedAction()
        {
            var manifest = Manifest();
            Assert.ThrowsException<InvalidDataException>(() =>
                RuntimeVerificationContractValidator.ValidateManifest(
                    manifest,
                    Graph(),
                    new List<PageIngredientAction>()));

            manifest.Assertions[0].AttachedActionReferences[0].ActionId = "action:foreign";
            manifest.Assertions[0].Intent.Action.ActionId = "action:foreign";
            Assert.ThrowsException<InvalidDataException>(() =>
                RuntimeVerificationContractValidator.ValidateManifest(manifest, Graph(), Actions()));
        }

        [TestMethod]
        public void V2AssertionRejectsDuplicateAssertionOwnerIdentity()
        {
            var manifest = Manifest();
            var duplicate = Assertion();
            duplicate.AssertionId = "ccd.runtime-verification.interaction/v1:source:search-submit-duplicate";
            duplicate.AssertionOwnerLane = "foreign.owner";
            manifest.Assertions.Add(duplicate);

            Assert.ThrowsException<InvalidDataException>(() =>
                RuntimeVerificationContractValidator.ValidateManifest(manifest, Graph(), Actions()));
        }

        [TestMethod]
        public void V2ReceiptRequiresFreshBoundInitialActionAndFinalEvidence()
        {
            var manifest = Manifest();
            RuntimeVerificationContractValidator.ValidateManifest(manifest, Graph(), Actions());
            var receipt = Receipt();

            RuntimeVerificationContractValidator.ValidateReceipt(
                manifest,
                receipt,
                PlanDigest,
                TargetIdentity,
                DateTimeOffset.Parse("2026-09-09T10:00:00Z"));

            receipt.AssertionResults[0].FinalStateEvidence = null;
            Assert.ThrowsException<InvalidDataException>(() =>
                RuntimeVerificationContractValidator.ValidateReceipt(
                    manifest,
                    receipt,
                    PlanDigest,
                    TargetIdentity,
                    DateTimeOffset.Parse("2026-09-09T10:00:00Z")));
        }

        [TestMethod]
        public void V2ReceiptRejectsForeignPlanOnResultOrStateEvidence()
        {
            var manifest = Manifest();
            var receipt = Receipt();
            receipt.AssertionResults[0].PlanDigest = DigestA;
            Assert.ThrowsException<InvalidDataException>(() =>
                RuntimeVerificationContractValidator.ValidateReceipt(
                    manifest,
                    receipt,
                    PlanDigest,
                    TargetIdentity,
                    DateTimeOffset.Parse("2026-09-09T10:00:00Z")));

            receipt = Receipt();
            receipt.AssertionResults[0].ActionEvidence.PlanDigest = DigestA;
            Assert.ThrowsException<InvalidDataException>(() =>
                RuntimeVerificationContractValidator.ValidateReceipt(
                    manifest,
                    receipt,
                    PlanDigest,
                    TargetIdentity,
                    DateTimeOffset.Parse("2026-09-09T10:00:00Z")));
        }

        [TestMethod]
        public void V2ReceiptRejectsForeignOrDuplicateAssertionResults()
        {
            var manifest = Manifest();
            var receipt = Receipt();
            receipt.AssertionResults.Add(Receipt().AssertionResults[0]);

            Assert.ThrowsException<InvalidDataException>(() =>
                RuntimeVerificationContractValidator.ValidateReceipt(
                    manifest,
                    receipt,
                    PlanDigest,
                    TargetIdentity,
                    DateTimeOffset.Parse("2026-09-09T10:00:00Z")));
        }

        private static RuntimeVerificationManifest Manifest()
        {
            return new RuntimeVerificationManifest
            {
                SchemaVersion = RuntimeVerificationContractValidator.ManifestSchemaV2,
                Assertions = new List<RuntimeVerificationAssertion> { Assertion() }
            };
        }

        private static RuntimeVerificationAssertion Assertion()
        {
            return new RuntimeVerificationAssertion
            {
                AssertionId = "ccd.runtime-verification.interaction/v1:source:search-submit",
                AssertionOwnerLane = "behavior.interaction",
                Subtype = "interaction.search-submit",
                SemanticRole = "interaction-state-transition",
                SourcePredicateId = "interaction.search-box.try-in-place",
                SourcePredicateVersion = "1",
                SourcePageOrListItemIdentity = "source-page:ff758937-6594-4e7d-be6a-e9f22d541889:item-7",
                SourceVersionIdentity = "version:41",
                SourceEvidenceDigestSha256 = DigestA,
                StableAssertionKey = "search-submit",
                TargetProfileId = "cupcollect-classic-page/v1",
                FixtureContractVersion = "pnp-ingredient-fixture/v1",
                AttachedActionReferences = new List<RuntimeVerificationActionReference>
                {
                    new RuntimeVerificationActionReference
                    {
                        IngredientId = IngredientId,
                        ActionId = ActionId,
                        DependencyRole = "read-only-interaction-container"
                    }
                },
                Intent = new RuntimeVerificationAssertionIntent
                {
                    InitialState = new RuntimeVerificationStateExpectation
                    {
                        StateId = "search-ready",
                        Predicate = "search box is visible and enabled",
                        EvidenceKind = "dom-accessibility-snapshot"
                    },
                    Action = new RuntimeVerificationActionIntent
                    {
                        ActionId = ActionId,
                        Kind = "fill-and-submit",
                        Selector = "input[type=search]",
                        Input = "ccd",
                        TimeoutMilliseconds = 5000
                    },
                    ExpectedFinalState = new RuntimeVerificationStateExpectation
                    {
                        StateId = "results-updated",
                        Predicate = "result region reflects submitted query",
                        EvidenceKind = "dom-accessibility-network-snapshot"
                    }
                }
            };
        }

        private static CanonicalPageIngredientGraph Graph()
        {
            return new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode { Id = IngredientId, HasContent = true }
                }
            };
        }

        private static IList<PageIngredientAction> Actions()
        {
            return new List<PageIngredientAction>
            {
                new PageIngredientAction { IngredientId = IngredientId, ActionId = ActionId }
            };
        }

        private static RuntimeVerificationReceipt Receipt()
        {
            var initial = DateTimeOffset.Parse("2026-09-09T10:01:00Z");
            var action = initial.AddSeconds(1);
            var final = action.AddSeconds(1);
            return new RuntimeVerificationReceipt
            {
                SchemaVersion = RuntimeVerificationContractValidator.ReceiptSchemaV2,
                PlanDigest = PlanDigest,
                TargetIdentity = TargetIdentity,
                CompletedAtUtc = final.AddSeconds(1),
                Status = RuntimeVerificationStatus.Passed,
                AssertionResults = new List<RuntimeVerificationAssertionResult>
                {
                    new RuntimeVerificationAssertionResult
                    {
                        AssertionId = Assertion().AssertionId,
                        PlanDigest = PlanDigest,
                        TargetIdentity = TargetIdentity,
                        ObservedAtUtc = final,
                        Passed = true,
                        InitialStateEvidence = Evidence("initial", initial),
                        ActionEvidence = Evidence("action", action),
                        FinalStateEvidence = Evidence("final", final)
                    }
                }
            };
        }

        private static RuntimeVerificationStateEvidence Evidence(string stage, DateTimeOffset observedAtUtc)
        {
            return new RuntimeVerificationStateEvidence
            {
                Stage = stage,
                AssertionId = Assertion().AssertionId,
                ActionId = ActionId,
                PlanDigest = PlanDigest,
                TargetIdentity = TargetIdentity,
                ObservedAtUtc = observedAtUtc,
                ObservedStateDigestSha256 = DigestB,
                EvidenceArtifactSha256 = DigestC
            };
        }
    }
}
