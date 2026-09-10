using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity.BehaviorInteraction;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Test.Migration.Ingredients.BehaviorInteraction
{
    [TestClass]
    public class BehaviorInteractionRuntimeReceiptProjectorTests
    {
        [TestMethod]
        public void ProjectsLosslessContentAddressedStateEvidenceAcceptedByExactV2Validator()
        {
            using var fixture = LoadFixture();
            var request = BuildRequest(fixture.RootElement);

            var projection = BehaviorInteractionRuntimeReceiptProjector.Project(request);

            RuntimeVerificationContractValidator.ValidateReceipt(
                request.Manifest,
                projection.Receipt,
                request.PlanDigest,
                request.TargetIdentity,
                request.ExecutionStartedAtUtc);
            Assert.AreEqual(RuntimeVerificationContractValidator.ReceiptSchemaV2, projection.Receipt.SchemaVersion);
            Assert.AreEqual(RuntimeVerificationStatus.Passed, projection.Receipt.Status);
            Assert.AreEqual(3, projection.StateEvidenceArtifacts.Count);

            var result = projection.Receipt.AssertionResults.Single();
            var evidence = new[]
            {
                result.InitialStateEvidence,
                result.ActionEvidence,
                result.FinalStateEvidence
            };
            Assert.IsTrue(evidence.All(value => IsDigest(value.ObservedStateDigestSha256)));
            Assert.IsTrue(evidence.All(value => IsDigest(value.EvidenceArtifactSha256)));
            Assert.AreEqual(3, evidence.Select(value => value.EvidenceArtifactSha256).Distinct().Count());

            var expectedRaw = fixture.RootElement.GetProperty("observations")
                .GetProperty("initial").GetProperty("rawStateJson").GetString();
            var initialArtifact = projection.StateEvidenceArtifacts.Single(value =>
                value.Reference.Sha256 == result.InitialStateEvidence.EvidenceArtifactSha256);
            var artifactBytes = MigrationArtifact.ReadAllBytes(
                initialArtifact.Reference,
                initialArtifact.ContentBase64);
            using var artifactDocument = JsonDocument.Parse(artifactBytes);
            Assert.AreEqual(
                expectedRaw,
                artifactDocument.RootElement.GetProperty("rawStateJson").GetString());
            Assert.AreEqual(
                result.InitialStateEvidence.ObservedStateDigestSha256,
                artifactDocument.RootElement.GetProperty("observedStateDigestSha256").GetString());
            Assert.AreEqual(
                request.OperationId,
                artifactDocument.RootElement.GetProperty("operationId").GetString());
            Assert.AreEqual(
                request.Producer.ImplementationCommit,
                artifactDocument.RootElement.GetProperty("producer")
                    .GetProperty("implementationCommit").GetString());
        }

        [TestMethod]
        public void MissingStateEvidenceArtifactFailsClosed()
        {
            using var fixture = LoadFixture();
            var request = BuildRequest(fixture.RootElement);
            var projection = BehaviorInteractionRuntimeReceiptProjector.Project(request);
            projection.StateEvidenceArtifacts.RemoveAt(2);

            var exception = Assert.ThrowsException<InvalidDataException>(() =>
                BehaviorInteractionRuntimeReceiptProjector.Validate(request, projection));

            StringAssert.Contains(exception.Message, "final state evidence artifact is missing");
        }

        [TestMethod]
        public void CorruptStateEvidenceArtifactFailsClosed()
        {
            using var fixture = LoadFixture();
            var request = BuildRequest(fixture.RootElement);
            var projection = BehaviorInteractionRuntimeReceiptProjector.Project(request);
            projection.StateEvidenceArtifacts[1].ContentBase64 =
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));

            Assert.ThrowsException<InvalidDataException>(() =>
                BehaviorInteractionRuntimeReceiptProjector.Validate(request, projection));
        }

        [DataTestMethod]
        [DataRow("receipt-plan")]
        [DataRow("artifact-operation")]
        [DataRow("artifact-producer")]
        public void WrongExecutionBindingFailsClosed(string mutation)
        {
            using var fixture = LoadFixture();
            var request = BuildRequest(fixture.RootElement);
            var projection = BehaviorInteractionRuntimeReceiptProjector.Project(request);

            switch (mutation)
            {
                case "receipt-plan":
                    projection.Receipt.AssertionResults.Single().ActionEvidence.PlanDigest =
                        new string('0', 64);
                    break;
                case "artifact-operation":
                    request.OperationId = "operation:foreign";
                    break;
                case "artifact-producer":
                    request.Producer.ImplementationCommit = new string('1', 40);
                    break;
                default:
                    Assert.Fail("Unknown mutation " + mutation);
                    break;
            }

            Assert.ThrowsException<InvalidDataException>(() =>
                BehaviorInteractionRuntimeReceiptProjector.Validate(request, projection));
        }

        [TestMethod]
        public void InvalidOrMissingStateJsonCannotProduceReceipt()
        {
            using var fixture = LoadFixture();
            var request = BuildRequest(fixture.RootElement);
            request.FinalState.RawStateJson = "{";

            Assert.ThrowsException<InvalidDataException>(() =>
                BehaviorInteractionRuntimeReceiptProjector.Project(request));

            request = BuildRequest(fixture.RootElement);
            request.Action.RawStateJson = null;
            Assert.ThrowsException<InvalidDataException>(() =>
                BehaviorInteractionRuntimeReceiptProjector.Project(request));
        }

        [TestMethod]
        public void FixturePreservesIndependentFailWithoutUpgradingItsVerdict()
        {
            using var fixture = LoadFixture();
            var adverse = fixture.RootElement.GetProperty("preservedAdverseEvidence");

            Assert.AreEqual("fail", adverse.GetProperty("typedVerdict").GetString());
            Assert.AreEqual(
                "EXPECTED_IN_PLACE_TRANSITION_NOT_OBSERVED",
                adverse.GetProperty("reasonCode").GetString());
            Assert.AreEqual(
                "71e4a95f10bf61e1c9bdc0cf54122dc26e3c48b51ada93366f649c8c81e4f875",
                adverse.GetProperty("receiptSha256").GetString());
            Assert.AreEqual(
                "preserved-negative-browser-observation-not-v2-valid",
                adverse.GetProperty("disposition").GetString());
        }

        private static BehaviorInteractionRuntimeReceiptRequest BuildRequest(JsonElement root)
        {
            var assertionId = root.GetProperty("assertionId").GetString();
            var actionId = root.GetProperty("actionId").GetString();
            var dynamicIngredientId =
                "ccd.ingredient.dynamic.region/v1:ff758937-6594-4e7d-be6a-e9f22d541889:00001:f869d623-017d-42ff-90e8-b5bf14fff9b3";
            var webPartIngredientId = actionId.Substring("action:".Length);
            var references = new List<RuntimeVerificationActionReference>
            {
                new RuntimeVerificationActionReference
                {
                    IngredientId = dynamicIngredientId,
                    ActionId = "action:" + dynamicIngredientId,
                    DependencyRole = "read-only-runtime-projection"
                },
                new RuntimeVerificationActionReference
                {
                    IngredientId = webPartIngredientId,
                    ActionId = actionId,
                    DependencyRole = "read-only-interaction-container"
                }
            };
            var assertion = new RuntimeVerificationAssertion
            {
                AssertionId = assertionId,
                AssertionOwnerLane = BehaviorInteractionSearchSubmitEvidenceNormalizer.Lane,
                Subtype = BehaviorInteractionSearchSubmitEvidenceNormalizer.Subtype,
                SemanticRole = BehaviorInteractionSearchSubmitEvidenceNormalizer.SemanticRole,
                SourcePredicateId = BehaviorInteractionSearchSubmitEvidenceNormalizer.SourcePredicateId,
                SourcePredicateVersion = BehaviorInteractionSearchSubmitEvidenceNormalizer.SourcePredicateVersion,
                SourcePageOrListItemIdentity = root.GetProperty("sourcePageOrListItemIdentity").GetString(),
                SourceVersionIdentity = root.GetProperty("sourceVersionIdentity").GetString(),
                SourceEvidenceDigestSha256 = root.GetProperty("sourceEvidenceDigestSha256").GetString(),
                StableAssertionKey = "search-submit",
                TargetProfileId = "cupcollect-classic-page/v1",
                FixtureContractVersion = root.GetProperty("schemaVersion").GetString(),
                AttachedActionReferences = references,
                Intent = new RuntimeVerificationAssertionIntent
                {
                    InitialState = new RuntimeVerificationStateExpectation
                    {
                        StateId = "search-input-ready",
                        Predicate = "input.enabled && input.value == '' && no-owned-submit-in-flight",
                        EvidenceKind = "dom-state-plus-network-baseline"
                    },
                    Action = new RuntimeVerificationActionIntent
                    {
                        ActionId = actionId,
                        Kind = "replace-text-and-submit-once",
                        Selector = "webpart-instance:e156d14d-d3b9-4b0e-8a54-6ed38a7180fe role:search-query-input",
                        Input = "ccd-interaction-canary",
                        TimeoutMilliseconds = 15000
                    },
                    ExpectedFinalState = new RuntimeVerificationStateExpectation
                    {
                        StateId = "in-place-search-transition-observed",
                        Predicate = "same-page && submitted-query == 'ccd-interaction-canary' && (result-region-state-changed || explicit-empty-result-state)",
                        EvidenceKind = "ordered-dom-network-navigation-state"
                    }
                }
            };
            var graph = new CanonicalPageIngredientGraph
            {
                SchemaVersion = CanonicalPageIngredientGraph.SchemaVersionV2,
                Nodes = new List<PageIngredientNode>
                {
                    Node(dynamicIngredientId, PageIngredientKind.Runtime, "dynamic.region"),
                    Node(webPartIngredientId, PageIngredientKind.WebPart, "webpart.instance")
                }
            };
            var actions = references.Select(value => new PageIngredientAction
            {
                ActionId = value.ActionId,
                IngredientId = value.IngredientId,
                Capability = IngredientCapability.Available,
                Disposition = IngredientDisposition.Preserve,
                TargetIdentity = root.GetProperty("targetIdentity").GetString()
            }).ToList();
            var observations = root.GetProperty("observations");
            var producer = root.GetProperty("producer");
            return new BehaviorInteractionRuntimeReceiptRequest
            {
                Manifest = new RuntimeVerificationManifest
                {
                    SchemaVersion = RuntimeVerificationContractValidator.ManifestSchemaV2,
                    Requirements = new List<RuntimeVerificationRequirement>(),
                    Assertions = new List<RuntimeVerificationAssertion> { assertion }
                },
                IngredientGraph = graph,
                IngredientActions = actions,
                AssertionId = assertionId,
                PlanDigest = root.GetProperty("planDigest").GetString(),
                TargetIdentity = root.GetProperty("targetIdentity").GetString(),
                OperationId = root.GetProperty("operationId").GetString(),
                Producer = new BehaviorInteractionRuntimeProducerBinding
                {
                    ProducerId = producer.GetProperty("producerId").GetString(),
                    ImplementationCommit = producer.GetProperty("implementationCommit").GetString(),
                    BinaryDigestSha256 = producer.GetProperty("binaryDigestSha256").GetString()
                },
                ExecutionStartedAtUtc = root.GetProperty("executionStartedAtUtc").GetDateTimeOffset(),
                CompletedAtUtc = root.GetProperty("completedAtUtc").GetDateTimeOffset(),
                InitialState = Observation(observations.GetProperty("initial")),
                Action = Observation(observations.GetProperty("action")),
                FinalState = Observation(observations.GetProperty("final")),
                Passed = true,
                Message = "Hermetic evidence proves the expected in-page transition."
            };
        }

        private static PageIngredientNode Node(
            string ingredientId,
            PageIngredientKind kind,
            string owner)
        {
            return new PageIngredientNode
            {
                Id = ingredientId,
                Kind = kind,
                HasContent = true,
                PrimaryOwnerLane = owner
            };
        }

        private static BehaviorInteractionRuntimeStateObservation Observation(JsonElement value)
        {
            return new BehaviorInteractionRuntimeStateObservation
            {
                ObservedAtUtc = value.GetProperty("observedAtUtc").GetDateTimeOffset(),
                RawStateJson = value.GetProperty("rawStateJson").GetString()
            };
        }

        private static bool IsDigest(string value)
        {
            return value != null
                && value.Length == 64
                && value.All(character =>
                    (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f'));
        }

        private static JsonDocument LoadFixture([CallerFilePath] string sourceFilePath = null)
        {
            var testDirectory = Path.GetDirectoryName(sourceFilePath);
            var fixturePath = Path.GetFullPath(Path.Combine(
                testDirectory,
                "..", "..", "..", "Resources", "Migration", "Ingredients", "BehaviorInteraction",
                "search-submit-runtime-state-evidence.fixture.v2.json"));
            return JsonDocument.Parse(File.ReadAllText(fixturePath));
        }
    }
}
