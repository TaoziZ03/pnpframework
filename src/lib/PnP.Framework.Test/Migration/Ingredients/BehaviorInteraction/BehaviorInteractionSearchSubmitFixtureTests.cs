using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PnP.Framework.Test.Migration.Ingredients.BehaviorInteraction
{
    [TestClass]
    public class BehaviorInteractionSearchSubmitFixtureTests
    {
        [TestMethod]
        public void FixtureBuildsValidV2AssertionWithoutPersistedBehaviorIngredient()
        {
            using var document = LoadFixture();
            var root = document.RootElement;
            var (manifest, graph, actions) = BuildRuntimeContract(root);

            Assert.AreEqual("runtime-verification", root.GetProperty("workItemType").GetString());
            Assert.AreEqual("behavior.interaction", root.GetProperty("lane").GetString());
            Assert.IsNull(graph.Nodes.SingleOrDefault(node =>
                string.Equals(node.Id, manifest.Assertions.Single().AssertionId, StringComparison.Ordinal)));
            Assert.IsNull(graph.Nodes.SingleOrDefault(node =>
                string.Equals(node.PrimaryOwnerLane, "behavior.interaction", StringComparison.Ordinal)));

            RuntimeVerificationContractValidator.ValidateManifest(manifest, graph, actions);
        }

        [TestMethod]
        public void FixtureKeepsObservationLocatorsSeparateFromCanonicalIdentity()
        {
            using var document = LoadFixture();
            var assertion = document.RootElement.GetProperty("assertion");
            var trigger = assertion.GetProperty("trigger");
            var target = assertion.GetProperty("target");

            Assert.IsFalse(trigger.GetProperty("locatorIsCanonicalIdentity").GetBoolean());
            Assert.IsFalse(target.GetProperty("locatorIsCanonicalIdentity").GetBoolean());
            StringAssert.StartsWith(
                assertion.GetProperty("stableAssertionId").GetString(),
                "assertion:ccd.ingredient.webpart.instance/");
            Assert.AreEqual(
                "e156d14d-d3b9-4b0e-8a54-6ed38a7180fe",
                trigger.GetProperty("canonicalOwnerInstanceId").GetString());
            Assert.AreEqual(
                "f869d623-017d-42ff-90e8-b5bf14fff9b3",
                target.GetProperty("canonicalOwnerInstanceId").GetString());
        }

        [TestMethod]
        public void FixtureDeclaresBoundedSafeActionAndAllTypedVerdicts()
        {
            using var document = LoadFixture();
            var assertion = document.RootElement.GetProperty("assertion");
            var action = assertion.GetProperty("action");
            var boundary = assertion.GetProperty("runtimeBoundary");
            var verdicts = assertion.GetProperty("typedVerdictPolicy");

            Assert.AreEqual("replace-text-and-submit-once", action.GetProperty("kind").GetString());
            Assert.AreEqual(15000, action.GetProperty("timeoutMilliseconds").GetInt32());
            Assert.AreEqual(2, action.GetProperty("maximumAttempts").GetInt32());
            Assert.IsTrue(boundary.GetProperty("allowed").EnumerateArray()
                .Any(value => value.GetString() == "same-origin GET search request"));
            Assert.IsTrue(boundary.GetProperty("forbidden").EnumerateArray()
                .Any(value => value.GetString() == "destructive form submission"));

            CollectionAssert.AreEquivalent(
                new[] { "pass", "conditional", "unsupported", "unknown", "fail" },
                verdicts.EnumerateObject().Select(property => property.Name).ToArray());
        }

        [TestMethod]
        public void FixtureBindsIndependentRawAndSemanticDigests()
        {
            using var document = LoadFixture();
            var binding = document.RootElement.GetProperty("sourceBinding");
            var raw = binding.GetProperty("freshRawWebPartResponseDigestSha256").GetString();
            var semantic = binding.GetProperty("freshNormalizedSemanticDigestSha256").GetString();

            AssertDigest(raw);
            AssertDigest(semantic);
            Assert.AreNotEqual(raw, semantic);
            Assert.IsTrue(document.RootElement.GetProperty("sourceConfiguration")
                .GetProperty("tryInplaceQuery").GetBoolean());
            Assert.IsFalse(document.RootElement.GetProperty("sourceConfiguration")
                .GetProperty("allowEmptySearch").GetBoolean());
        }

        [TestMethod]
        public void FixtureCarriesMappedResultScriptTopologyAndActiveLease()
        {
            using var document = LoadFixture();
            var topology = document.RootElement.GetProperty("resultScriptConsumerTopology");
            var source = topology.GetProperty("sourceProvider");
            var target = topology.GetProperty("targetProvider");
            var searchBox = topology.GetProperty("targetSearchBox");
            var mapping = topology.GetProperty("targetMapping");
            var lease = topology.GetProperty("lease");

            Assert.AreEqual("f869d623-017d-42ff-90e8-b5bf14fff9b3", source.GetProperty("canonicalProviderInstanceId").GetString());
            Assert.AreEqual("Microsoft.Office.Server.Search.WebControls.ResultScriptWebPart", target.GetProperty("providerType").GetString());
            Assert.AreEqual("Default", target.GetProperty("queryGroupName").GetString());
            Assert.IsTrue(target.GetProperty("updateAjaxNavigate").GetBoolean());
            Assert.AreEqual(
                target.GetProperty("canonicalProviderInstanceId").GetString(),
                mapping.GetProperty("targetProviderInstanceId").GetString());
            Assert.AreEqual(
                searchBox.GetProperty("canonicalSearchBoxInstanceId").GetString(),
                mapping.GetProperty("targetSearchBoxInstanceId").GetString());
            Assert.AreNotEqual(
                searchBox.GetProperty("canonicalSearchBoxInstanceId").GetString(),
                target.GetProperty("canonicalProviderInstanceId").GetString());
            Assert.AreEqual(
                target.GetProperty("pageIdentity").GetString(),
                mapping.GetProperty("targetPageIdentity").GetString());
            Assert.AreEqual(
                searchBox.GetProperty("pageVersion").GetString(),
                mapping.GetProperty("targetPageVersion").GetString());
            Assert.AreEqual("ccd347-search-inplace-cfa6f409-v1", lease.GetProperty("leaseId").GetString());
            Assert.AreEqual("active", lease.GetProperty("status").GetString());
            Assert.AreEqual(RootClaim(document), lease.GetProperty("claimId").GetString());
            Assert.AreEqual(
                topology.GetProperty("admittedPlanDigest").GetString(),
                lease.GetProperty("planDigest").GetString());
            Assert.IsTrue(
                lease.GetProperty("retainThroughUtc").GetDateTimeOffset()
                    >= topology.GetProperty("runtimeFinalEvidenceAtUtc").GetDateTimeOffset());
        }

        private static string RootClaim(JsonDocument document)
        {
            return document.RootElement.GetProperty("claimId").GetString();
        }

        [TestMethod]
        public void ForeignActionBindingFailsClosed()
        {
            using var document = LoadFixture();
            var (manifest, graph, actions) = BuildRuntimeContract(document.RootElement);
            manifest.Assertions.Single().Intent.Action.ActionId = "action:foreign";

            Assert.ThrowsException<InvalidDataException>(() =>
                RuntimeVerificationContractValidator.ValidateManifest(manifest, graph, actions));
        }

        private static (RuntimeVerificationManifest Manifest, CanonicalPageIngredientGraph Graph, IList<PageIngredientAction> Actions)
            BuildRuntimeContract(JsonElement root)
        {
            var value = root.GetProperty("assertion");
            var references = value.GetProperty("attachedActionReferences")
                .EnumerateArray()
                .Select(reference => new RuntimeVerificationActionReference
                {
                    IngredientId = reference.GetProperty("ingredientId").GetString(),
                    ActionId = reference.GetProperty("actionId").GetString(),
                    DependencyRole = reference.GetProperty("dependencyRole").GetString()
                })
                .ToList();
            var actionIntent = value.GetProperty("action");
            var initial = value.GetProperty("initialState");
            var final = value.GetProperty("expectedFinalState");
            var source = root.GetProperty("sourceBinding");
            var predicate = root.GetProperty("sourcePredicate");
            var assertion = new RuntimeVerificationAssertion
            {
                AssertionId = value.GetProperty("assertionId").GetString(),
                AssertionOwnerLane = value.GetProperty("assertionOwnerLane").GetString(),
                Subtype = root.GetProperty("subtype").GetString(),
                SemanticRole = root.GetProperty("semanticRole").GetString(),
                SourcePredicateId = predicate.GetProperty("id").GetString(),
                SourcePredicateVersion = predicate.GetProperty("version").GetString(),
                SourcePageOrListItemIdentity = source.GetProperty("pageOrListItemIdentity").GetString(),
                SourceVersionIdentity = source.GetProperty("sourceVersionIdentity").GetString(),
                SourceEvidenceDigestSha256 = source.GetProperty("sourceEvidenceDigestSha256").GetString(),
                StableAssertionKey = value.GetProperty("stableAssertionKey").GetString(),
                TargetProfileId = value.GetProperty("targetProfileId").GetString(),
                FixtureContractVersion = root.GetProperty("schemaVersion").GetString(),
                AttachedActionReferences = references,
                Intent = new RuntimeVerificationAssertionIntent
                {
                    InitialState = State(initial),
                    Action = new RuntimeVerificationActionIntent
                    {
                        ActionId = actionIntent.GetProperty("actionId").GetString(),
                        Kind = actionIntent.GetProperty("kind").GetString(),
                        Selector = actionIntent.GetProperty("selector").GetString(),
                        Input = actionIntent.GetProperty("input").GetString(),
                        TimeoutMilliseconds = actionIntent.GetProperty("timeoutMilliseconds").GetInt32()
                    },
                    ExpectedFinalState = State(final)
                }
            };
            var nodes = references.Select((reference, index) => new PageIngredientNode
            {
                Id = reference.IngredientId,
                Kind = index == 0 ? PageIngredientKind.Runtime : PageIngredientKind.WebPart,
                HasContent = true,
                PrimaryOwnerLane = index == 0 ? "dynamic.region" : "webpart.instance"
            }).ToList();
            var actions = references.Select(reference => new PageIngredientAction
            {
                ActionId = reference.ActionId,
                IngredientId = reference.IngredientId,
                Capability = IngredientCapability.Available,
                Disposition = IngredientDisposition.Preserve
            }).ToList();

            return (
                new RuntimeVerificationManifest
                {
                    SchemaVersion = RuntimeVerificationContractValidator.ManifestSchemaV2,
                    Requirements = new List<RuntimeVerificationRequirement>(),
                    Assertions = new List<RuntimeVerificationAssertion> { assertion }
                },
                new CanonicalPageIngredientGraph
                {
                    SchemaVersion = CanonicalPageIngredientGraph.SchemaVersionV2,
                    Nodes = nodes
                },
                actions);
        }

        private static RuntimeVerificationStateExpectation State(JsonElement value)
        {
            return new RuntimeVerificationStateExpectation
            {
                StateId = value.GetProperty("stateId").GetString(),
                Predicate = value.GetProperty("predicate").GetString(),
                EvidenceKind = value.GetProperty("evidenceKind").GetString()
            };
        }

        private static void AssertDigest(string value)
        {
            Assert.IsNotNull(value);
            Assert.AreEqual(64, value.Length);
            Assert.IsTrue(value.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f')));
        }

        private static JsonDocument LoadFixture([CallerFilePath] string sourceFilePath = null)
        {
            var testDirectory = Path.GetDirectoryName(sourceFilePath);
            var fixturePath = Path.GetFullPath(Path.Combine(
                testDirectory,
                "..", "..", "..", "Resources", "Migration", "Ingredients", "BehaviorInteraction",
                "search-submit.fixture.v1.json"));
            return JsonDocument.Parse(File.ReadAllText(fixturePath));
        }
    }
}
