using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Assessment.Maturity;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Test.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PnP.Framework.Test.Migration.Pages.Assessment
{
    // All observations are synthetic contract fixtures. No test supplies live
    // tenant evidence, implements a lane producer, or grants target admission.
    [TestClass]
    public class RequiredWebPartSharedContractTests
    {
        // Captured using the unmodified D assembly, built with SDK 10.0.400:
        // commit dfe052288c7bd5b28ae4132a3306eafa821468ab;
        // PageIngredientNode blob eae458aaa4309836c2a19d75459f190d63231b59.
        private const string LegacyEmptyCanonical = "{\"id\":null,\"kind\":0,\"label\":null,\"hasContent\":false,\"ownership\":\"Unknown\",\"sourceAuthority\":null,\"evidenceDigest\":null,\"runtimeRequirement\":null,\"evidenceReferences\":[]}";
        private const string LegacyPopulatedCanonical = "{\"id\":\"webpart:legacy-fixture\",\"kind\":\"WebPart\",\"label\":\"Legacy <WebPart>\",\"hasContent\":true,\"ownership\":\"SourceOwned\",\"sourceAuthority\":\"MSIT-readonly\",\"evidenceDigest\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"runtimeRequirement\":\"persisted-instance\",\"evidenceReferences\":[\"source#1\",\"source#2\"]}";
        private const string LegacyPlanCanonical = "{\"sourceSnapshotDigest\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\",\"sourceWebUrl\":null,\"sourcePageServerRelativeUrl\":null,\"originalIdentifier\":null,\"targetWebUrl\":null,\"targetWebServerRelativeUrl\":null,\"preferredTargetPageServerRelativeUrl\":null,\"targetPageServerRelativeUrl\":null,\"targetPathCollisionResolved\":false,\"targetPathResolutionReason\":null,\"pageLayoutName\":null,\"operation\":\"CreatePage\",\"targetLifecycle\":\"Draft\",\"lifecycleReason\":null,\"createOnly\":true,\"planningPolicy\":null,\"targetProbe\":null,\"layoutMaterialization\":null,\"layoutTargetProbe\":null,\"layoutAdmission\":null,\"fieldActions\":[],\"taxonomyRelationshipActions\":[],\"dependencyActions\":[],\"topology\":null,\"topologyTargetAnalysis\":null,\"listMigration\":null,\"webPartActions\":[],\"replacements\":[],\"expectedPublishingPageContentSha256\":null,\"storageAssertions\":[],\"runtimeVerification\":{\"schemaVersion\":\"pnp-migration-runtime-verification/v1\",\"requirements\":[]},\"ingredientGraph\":{\"schemaVersion\":\"pnp-page-ingredient-graph/v1\",\"nodes\":[{\"id\":\"webpart:legacy-fixture\",\"kind\":\"WebPart\",\"label\":\"Legacy <WebPart>\",\"hasContent\":true,\"ownership\":\"SourceOwned\",\"sourceAuthority\":\"MSIT-readonly\",\"evidenceDigest\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"runtimeRequirement\":\"persisted-instance\",\"evidenceReferences\":[\"source#1\",\"source#2\"]},{\"id\":null,\"kind\":0,\"label\":null,\"hasContent\":false,\"ownership\":\"Unknown\",\"sourceAuthority\":null,\"evidenceDigest\":null,\"runtimeRequirement\":null,\"evidenceReferences\":[]}],\"edges\":[]},\"ingredientActions\":[{\"actionId\":\"action:legacy-webpart\",\"ingredientId\":\"webpart:legacy-fixture\",\"capability\":\"Available\",\"disposition\":\"Preserve\",\"realization\":null,\"targetIdentity\":\"cupcollect:/sites/ccd/pages/legacy.aspx\",\"policyId\":\"webpart.preserve/v1\",\"policyVersion\":\"v1\",\"reason\":null,\"releasedDependencyIngredientIds\":[],\"verificationAssertions\":[]}],\"migrationOutcome\":\"Unknown\",\"ingredientIssues\":[],\"executionFrontier\":{\"schemaVersion\":\"pnp-page-ingredient-execution-frontier/v1\",\"decisions\":[]},\"blockers\":[],\"warnings\":[],\"isExecutable\":false}";
        private const string LegacyEmptyDigest = "33cc528b84a80159ce22fcbe8a0edcfe0bf08ffc91bdc74c41f25ed706da5774";
        private const string LegacyPopulatedDigest = "2b4af33839cc29b0f89e7f5ca72f1bb8898d756bfc494dc5304ea176d8f9f71f";
        private const string LegacyPlanDigest = "7b47851fcbb170271661dd1871fdc29d8a20e634255f07b9a64d6490585b98c6";
        private const string Subtype = "classic.webpart.instance";
        private const string Role = "persisted-instance";
        private const string PredicateId = "fixture.webpart.export/v1";
        private const string Lane = "webpart.instance";

        [TestMethod]
        public void SharedVersionsAndMinimalRegistrySurfaceAreFrozen()
        {
            Assert.AreEqual("pnp-ingredient-maturity-assessment/v1", IngredientMaturityContract.SchemaVersion);
            Assert.AreEqual("pnp-ingredient-maturity-evaluator/v2", IngredientMaturityContract.EvaluatorVersion);
            Assert.AreEqual("v2", IngredientMaturityEvidenceValidator.ValidatorVersion);
            Assert.AreEqual("pnp-page-ingredient-primary-owner-registry/v1", PublishingPageIngredientPrimaryOwnerRegistry.SchemaVersion);
            var registry = typeof(PublishingPageIngredientPrimaryOwnerRegistry);
            CollectionAssert.AreEquivalent(new[] { "Entries" }, registry.GetProperties().Select(value => value.Name).ToArray());
            CollectionAssert.AreEquivalent(new[] { "Resolve" }, registry
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(value => !value.IsSpecialName).Select(value => value.Name).ToArray());
            Assert.AreEqual(1, registry.GetConstructors().Length);
            foreach (var name in new[] { "Default", "CreateDefault", "BindBuiltInNodes", "Classify", "SourceIdentity", "SourceVersionIdentity" })
            {
                Assert.AreEqual(0, registry.GetMember(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Length, name);
            }
            CollectionAssert.AreEquivalent(new[] { "Id", "Kind", "Subtype", "SemanticRole", "SourcePredicateId", "PrimaryOwnerLane" },
                typeof(PageIngredientPrimaryOwnerDescriptor).GetProperties().Select(value => value.Name).ToArray());
            Assert.IsTrue(typeof(PageIngredientPrimaryOwnerDescriptor).GetProperties().All(value => !value.CanWrite));
        }

        [TestMethod]
        public void RegistryFreezesAndSortsEntriesAndCopiesThePredicateMap()
        {
            var fixture = new Fixture();
            var entries = new List<PageIngredientPrimaryOwnerDescriptor>
            {
                Descriptor("z", "classic.other.instance"), Descriptor("a")
            };
            var predicates = Predicates(_ => true);
            var registry = new PublishingPageIngredientPrimaryOwnerRegistry(entries, predicates);
            entries.Clear();
            predicates[PredicateId] = _ => false;

            CollectionAssert.AreEqual(new[] { "a", "z" }, registry.Entries.Select(value => value.Id).ToArray());
            Assert.AreEqual("a", registry.Resolve(fixture.Snapshot, fixture.Node).Id);
            Assert.ThrowsException<NotSupportedException>(() =>
                ((IList<PageIngredientPrimaryOwnerDescriptor>)registry.Entries).Add(Descriptor("extra")));
        }

        [DataTestMethod]
        [DataRow("null-entries")]
        [DataRow("null-entry")]
        [DataRow("duplicate-id")]
        [DataRow("missing-predicate")]
        [DataRow("null-predicate")]
        [DataRow("null-predicates")]
        public void RegistryRejectsInvalidConstruction(string mutation)
        {
            IEnumerable<PageIngredientPrimaryOwnerDescriptor> entries = new[] { Descriptor("one") };
            IReadOnlyDictionary<string, Func<IngredientOwnershipSourceContext, bool>> predicates = Predicates(_ => true);
            switch (mutation)
            {
                case "null-entries":
                    Assert.ThrowsException<ArgumentNullException>(() => new PublishingPageIngredientPrimaryOwnerRegistry(null, predicates));
                    return;
                case "null-entry": entries = new PageIngredientPrimaryOwnerDescriptor[] { null }; break;
                case "duplicate-id": entries = new[] { Descriptor("same"), Descriptor("same", "classic.other.instance") }; break;
                case "missing-predicate": predicates = new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>(); break;
                case "null-predicate": predicates = Predicates(null); break;
                case "null-predicates":
                    Assert.ThrowsException<ArgumentNullException>(() => new PublishingPageIngredientPrimaryOwnerRegistry(entries, null));
                    return;
                default: Assert.Fail("Unknown construction mutation."); break;
            }
            Assert.ThrowsException<ArgumentException>(() => new PublishingPageIngredientPrimaryOwnerRegistry(entries, predicates));
        }

        [DataTestMethod]
        [DataRow("id")]
        [DataRow("subtype")]
        [DataRow("role")]
        [DataRow("predicate")]
        [DataRow("lane")]
        public void DescriptorRejectsMissingTupleFields(string missing)
        {
            Assert.ThrowsException<ArgumentException>(() => new PageIngredientPrimaryOwnerDescriptor(
                missing == "id" ? " " : "fixture-owner",
                PageIngredientKind.WebPart,
                missing == "subtype" ? null : Subtype,
                missing == "role" ? " " : Role,
                missing == "predicate" ? null : PredicateId,
                missing == "lane" ? " " : Lane));
        }

        [DataTestMethod]
        [DataRow(Subtype)]
        [DataRow("classic.webpart.*")]
        public void ExactlyOneExactOrWildcardMatchResolvesTheBoundOwner(string subtype)
        {
            var fixture = new Fixture();
            IngredientOwnershipSourceContext seen = null;
            var registry = new PublishingPageIngredientPrimaryOwnerRegistry(new[] { Descriptor("one", subtype) },
                Predicates(context => { seen = context; return true; }));
            var owner = registry.Resolve(fixture.Snapshot, fixture.Node);

            Assert.AreEqual(Lane, owner.PrimaryOwnerLane);
            Assert.AreSame(fixture.Snapshot, seen.Snapshot);
            Assert.AreSame(fixture.Node, seen.Node);
            Assert.IsTrue(seen.HasBoundSourceIdentity);
        }

        [DataTestMethod]
        [DataRow("false-predicate")]
        [DataRow("empty-catalog")]
        [DataRow("same-tuple")]
        [DataRow("overlapping-wildcard")]
        public void ZeroOrMultipleMatchesFailClosed(string mutation)
        {
            var fixture = new Fixture();
            var entries = new List<PageIngredientPrimaryOwnerDescriptor> { Descriptor("one") };
            if (mutation == "empty-catalog") entries.Clear();
            if (mutation == "same-tuple") entries.Add(Descriptor("two"));
            if (mutation == "overlapping-wildcard") entries.Add(Descriptor("two", "classic.webpart.*"));
            var registry = new PublishingPageIngredientPrimaryOwnerRegistry(entries, Predicates(_ => mutation != "false-predicate"));
            Assert.ThrowsException<InvalidDataException>(() => registry.Resolve(fixture.Snapshot, fixture.Node));
        }

        [DataTestMethod]
        [DataRow("kind")]
        [DataRow("undefined-kind")]
        [DataRow("kind-id")]
        [DataRow("subtype")]
        [DataRow("subtype-case")]
        [DataRow("role")]
        [DataRow("predicate")]
        [DataRow("source-identity")]
        [DataRow("source-version")]
        [DataRow("wrong-owner")]
        [DataRow("missing-owner")]
        public void RegistryRejectsTupleUnboundSourceAndOwnerMutations(string mutation)
        {
            var fixture = new Fixture();
            // A permissive callback must not bypass the registry's structural checks.
            var registry = new PublishingPageIngredientPrimaryOwnerRegistry(new[] { Descriptor("one") }, Predicates(_ => true));
            var node = fixture.Node;
            switch (mutation)
            {
                case "kind": node.Kind = PageIngredientKind.Asset; break;
                case "undefined-kind": node.Kind = (PageIngredientKind)999; break;
                case "kind-id": node.KindId = "unresolved.kind/v1"; break;
                case "subtype": node.Subtype = "classic.other.instance"; break;
                case "subtype-case": node.Subtype = Subtype.ToUpperInvariant(); break;
                case "role": node.SemanticRole = "runtime-projection"; break;
                case "predicate": node.SourcePredicateId = "foreign-predicate"; break;
                case "source-identity": node.SourcePageOrListItemIdentity = " "; break;
                case "source-version": node.SourceVersionIdentity = null; break;
                case "wrong-owner": node.PrimaryOwnerLane = "dynamic.region"; break;
                case "missing-owner": node.PrimaryOwnerLane = null; break;
                default: Assert.Fail("Unknown registry mutation."); break;
            }
            Assert.ThrowsException<InvalidDataException>(() => registry.Resolve(fixture.Snapshot, node));
        }

        [TestMethod]
        public void NullNodeAndForeignSnapshotFailClosedWithoutAClaim()
        {
            var fixture = new Fixture();
            Assert.ThrowsException<InvalidDataException>(() => fixture.Registry.Resolve(fixture.Snapshot, null));
            Assert.ThrowsException<InvalidDataException>(() => fixture.Registry.Resolve(null, fixture.Node));
            Assert.ThrowsException<InvalidDataException>(() => fixture.Registry.Resolve(new PublishingPageCaptureBundle(), fixture.Node));
            Assert.IsTrue(IngredientMaturityEvidenceValidator.ValidateM0(fixture.Context, null,
                fixture.Snapshot, fixture.Registry, new[] { "fixture:m0" }).All(value => !value.Passed));
        }

        [DataTestMethod]
        [DataRow("classic.webpart")]
        [DataRow("classic.webparts.instance")]
        [DataRow("Classic.webpart.instance")]
        public void WildcardMatchDoesNotAcceptADifferentNamespace(string subtype)
        {
            var fixture = new Fixture();
            fixture.Node.Subtype = subtype;
            var registry = new PublishingPageIngredientPrimaryOwnerRegistry(new[] { Descriptor("one", "classic.webpart.*") }, Predicates(_ => true));
            Assert.ThrowsException<InvalidDataException>(() => registry.Resolve(fixture.Snapshot, fixture.Node));
        }

        [DataTestMethod]
        [DataRow("id")]
        [DataRow("kind")]
        [DataRow("kind-id")]
        [DataRow("subtype")]
        [DataRow("role")]
        [DataRow("predicate")]
        [DataRow("source-identity")]
        [DataRow("source-version")]
        [DataRow("owner")]
        [DataRow("artifact-digest")]
        public void M0RejectsEveryRoundTrippedTupleSourceOrOwnerTamper(string mutation)
        {
            var fixture = new Fixture();
            Assert.AreEqual(IngredientMaturityLevel.M1, fixture.Evaluate().AttainedMaturity);
            var node = RoundTrip(fixture.Node);
            switch (mutation)
            {
                case "id": node.Id = "webpart:foreign-instance"; break;
                case "kind": node.Kind = PageIngredientKind.Reference; break;
                case "kind-id": node.KindId = "webpart.instance"; break;
                case "subtype": node.Subtype = "classic.webpart.other"; break;
                case "role": node.SemanticRole = "derived-reference"; break;
                case "predicate": node.SourcePredicateId = "another.export"; break;
                case "source-identity": node.SourcePageOrListItemIdentity = "foreign-page:item-12"; break;
                case "source-version": node.SourceVersionIdentity = "version:2.0"; break;
                case "owner": node.PrimaryOwnerLane = "resource.script"; break;
                case "artifact-digest": node.EvidenceDigest = new string('f', 64); break;
                default: Assert.Fail("Unknown M0 mutation."); break;
            }
            var assessment = fixture.Evaluate(RoundTrip(node));
            Assert.IsNull(assessment.AttainedMaturity, mutation);
            Assert.IsFalse(assessment.Levels.Single(value => value.Level == IngredientMaturityLevel.M0).Passed, mutation);
            IngredientMaturityEvaluator.ValidateAssessment(assessment);
        }

        [TestMethod]
        public void ContextBoundContributorTemplateUsesValidatorsAndStopsAtItsEvidenceCeiling()
        {
            var fixture = new Fixture();
            var catalog = fixture.Catalog(fixture.Node);
            var assessment = IngredientMaturityEvaluator.Evaluate(fixture.Context, catalog);
            Assert.AreEqual(IngredientMaturityLevel.M1, assessment.AttainedMaturity);
            Assert.AreEqual(IngredientTechnicalStatus.Conditional, assessment.TechnicalOutcome.Status);
            Assert.AreEqual(IngredientMaturityGateCatalog.RawArtifactIntegrity, assessment.MissingPromotionGate);
            IngredientMaturityEvaluator.ValidateAssessment(assessment, fixture.Context, catalog);
        }

        [TestMethod]
        public void LegacyM1MethodGroupRemainsSourceCompatibleButAllGatesFail()
        {
            var fixture = new Fixture();
            Func<IngredientLiveEvidence, IReadOnlyList<IngredientMaturityGateReceipt>> legacy = IngredientMaturityEvidenceValidator.ValidateM1;
            Assert.IsTrue(legacy(fixture.Live).All(value => !value.Passed));
            Assert.IsTrue(IngredientMaturityEvidenceValidator.ValidateM1(fixture.Context, fixture.Live).All(value => value.Passed));
        }

        [DataTestMethod]
        [DataRow("null")]
        [DataRow("missing-identity")]
        [DataRow("missing-source")]
        [DataRow("missing-target")]
        [DataRow("missing-producer")]
        [DataRow("missing-outcome")]
        [DataRow("claim")]
        [DataRow("ingredient")]
        [DataRow("source-page")]
        [DataRow("source-version")]
        [DataRow("source-artifact-digest")]
        [DataRow("source-snapshot-digest")]
        [DataRow("target-profile")]
        [DataRow("target-identity")]
        [DataRow("time-window")]
        [DataRow("non-utc-window")]
        public void M1RejectsMissingOrForeignConsumerContext(string mutation)
        {
            var fixture = new Fixture();
            var context = RoundTrip(fixture.Context);
            switch (mutation)
            {
                case "null": context = null; break;
                case "missing-identity": context.Identity = null; break;
                case "missing-source": context.Source = null; break;
                case "missing-target": context.Target = null; break;
                case "missing-producer": context.Producer = null; break;
                case "missing-outcome": context.TechnicalOutcome = null; break;
                case "claim": context.Identity.ClaimId = new string('f', 64); break;
                case "ingredient": context.Identity.IngredientId = "webpart:other"; break;
                case "source-page": context.Source.PageOrListItemIdentity = "another-page"; break;
                case "source-version": context.Source.SourceVersion = "version:2.0"; break;
                case "source-artifact-digest": context.Source.SourceArtifactDigest = new string('f', 64); break;
                case "source-snapshot-digest": context.Source.SourceSnapshotDigest = new string('f', 64); break;
                case "target-profile": context.Target.TargetProfile = "another-profile"; break;
                case "target-identity": context.Target.TargetIdentity = "cupcollect:/other.aspx"; break;
                case "time-window": context.ObservationWindowStartUtc = context.ObservationWindowEndUtc.Value.AddMinutes(1); break;
                case "non-utc-window": context.ObservationWindowStartUtc = context.ObservationWindowStartUtc.Value.ToOffset(TimeSpan.FromHours(1)); break;
                default: Assert.Fail("Unknown consumer-context mutation."); break;
            }
            Assert.IsTrue(IngredientMaturityEvidenceValidator.ValidateM1(context, fixture.Live).All(value => !value.Passed), mutation);
        }

        [TestMethod]
        public void LegacyNodesKeepExactCanonicalBytesAndDigestsIncludingKindZero()
        {
            var empty = new PageIngredientNode();
            var populated = LegacyNode();
            AssertCanonicalBytes(LegacyEmptyCanonical, MigrationContractSerializer.SerializeCanonical(empty));
            AssertCanonicalBytes(LegacyPopulatedCanonical, MigrationContractSerializer.SerializeCanonical(populated));
            Assert.AreEqual(LegacyEmptyDigest, MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(empty)));
            Assert.AreEqual(LegacyPopulatedDigest, MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(populated)));
            using (var json = JsonDocument.Parse(MigrationContractSerializer.SerializeCanonical(empty)))
            {
                Assert.AreEqual(0, json.RootElement.GetProperty("kind").GetInt32());
            }
            Assert.IsNull(typeof(PageIngredientNode).GetProperty(nameof(PageIngredientNode.Kind)).GetCustomAttribute<JsonIgnoreAttribute>());
            AssertCanonicalBytes(LegacyEmptyCanonical, MigrationContractSerializer.SerializeCanonical(RoundTrip(empty)));
            AssertCanonicalBytes(LegacyPopulatedCanonical, MigrationContractSerializer.SerializeCanonical(RoundTrip(populated)));
        }

        [DataTestMethod]
        [DataRow(nameof(PageIngredientNode.KindId), "kindId")]
        [DataRow(nameof(PageIngredientNode.Subtype), "subtype")]
        [DataRow(nameof(PageIngredientNode.SemanticRole), "semanticRole")]
        [DataRow(nameof(PageIngredientNode.SourcePredicateId), "sourcePredicateId")]
        [DataRow(nameof(PageIngredientNode.SourcePageOrListItemIdentity), "sourcePageOrListItemIdentity")]
        [DataRow(nameof(PageIngredientNode.SourceVersionIdentity), "sourceVersionIdentity")]
        [DataRow(nameof(PageIngredientNode.PrimaryOwnerLane), "primaryOwnerLane")]
        public void EachAddedStringFieldIsOmittedOnlyWhenNullAndRoundTrips(string propertyName, string jsonName)
        {
            var property = typeof(PageIngredientNode).GetProperty(propertyName);
            Assert.AreEqual(typeof(string), property.PropertyType);
            Assert.AreEqual(JsonIgnoreCondition.WhenWritingNull, property.GetCustomAttribute<JsonIgnoreAttribute>().Condition);
            foreach (var value in new[] { "fixture-value", string.Empty })
            {
                var node = new PageIngredientNode();
                property.SetValue(node, value);
                var canonical = MigrationContractSerializer.SerializeCanonical(node);
                using (var json = JsonDocument.Parse(canonical))
                {
                    Assert.AreEqual(value, json.RootElement.GetProperty(jsonName).GetString());
                    Assert.AreEqual(10, json.RootElement.EnumerateObject().Count());
                    Assert.AreEqual(0, json.RootElement.GetProperty("kind").GetInt32());
                }
                Assert.AreEqual(value, property.GetValue(RoundTrip(node)));
            }
        }

        [TestMethod]
        public void AllSevenNonNullFieldsRoundTripWithoutImplyingStringKindAdmission()
        {
            var fixture = new Fixture();
            fixture.Node.KindId = "future.fixture.kind/v1";
            var canonical = MigrationContractSerializer.SerializeCanonical(fixture.Node);
            var read = RoundTrip(fixture.Node);
            AssertCanonicalBytes(canonical, MigrationContractSerializer.SerializeCanonical(read));
            foreach (var name in new[] { "KindId", "Subtype", "SemanticRole", "SourcePredicateId", "SourcePageOrListItemIdentity", "SourceVersionIdentity", "PrimaryOwnerLane" })
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace((string)typeof(PageIngredientNode).GetProperty(name).GetValue(read)), name);
            }
            // Retaining additive wire data is not permission to interpret a second kind system.
            Assert.IsNull(fixture.Evaluate(read).AttainedMaturity);
        }

        [TestMethod]
        public void LegacyPlanCanonicalBytesAndDigestRemainExactlyAtD()
        {
            var node = LegacyNode();
            var plan = new PublishingPageMigrationPlan
            {
                SourceSnapshotDigest = new string('c', 64),
                IngredientGraph = new CanonicalPageIngredientGraph
                {
                    Nodes = new List<PageIngredientNode> { node, new PageIngredientNode() }
                },
                IngredientActions = new List<PageIngredientAction>
                {
                    new PageIngredientAction
                    {
                        ActionId = "action:legacy-webpart",
                        IngredientId = node.Id,
                        Capability = IngredientCapability.Available,
                        Disposition = IngredientDisposition.Preserve,
                        TargetIdentity = "cupcollect:/sites/ccd/pages/legacy.aspx",
                        PolicyId = "webpart.preserve/v1",
                        PolicyVersion = "v1"
                    }
                }
            };
            AssertCanonicalBytes(LegacyPlanCanonical, PublishingPagePackageSerializer.SerializeCanonical(plan));
            Assert.AreEqual(LegacyPlanDigest, PublishingPageDigest.ComputePlanDigest(plan));
            var read = PublishingPagePackageSerializer.Deserialize<PublishingPageMigrationPlan>(LegacyPlanCanonical);
            AssertCanonicalBytes(LegacyPlanCanonical, PublishingPagePackageSerializer.SerializeCanonical(read));
            Assert.AreEqual(LegacyPlanDigest, PublishingPageDigest.ComputePlanDigest(read));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ContextBoundM4ReusesDNativeAdmissionAndRuntimeArtifactValidators(bool screenshot)
        {
            var fixture = new RuntimeFixture(screenshot);
            var evidence = fixture.Evidence;
            Assert.AreNotEqual(evidence.AdmittedPlanDigest, evidence.AdmittedExecutionPlanDigestSha256);
            Assert.AreEqual(evidence.AdmittedExecutionPlanDigestSha256,
                AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(evidence.AdmittedExecutionPlan,
                    evidence.AdmittedPlanDigest, evidence.ActionSignature.TargetIdentity));
            PublishingPageImportReceiptValidator.ValidateAdmittedExecution(evidence.ImportReceipt,
                evidence.AdmittedExecutionPlan, evidence.AdmittedExecutionPlanDigestSha256);
            Assert.AreEqual(MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(evidence.ImportReceipt)),
                evidence.RuntimeReceipt.ImportReceiptDigestSha256);
            RuntimeVerificationReceiptValidator.ValidateEvidence(evidence.RuntimeReceipt, evidence.Plan.RuntimeVerification,
                fixture.Context.Producer.ImplementationCommit, fixture.Store);
            Assert.IsTrue(fixture.Validate().All(value => value.Passed));
        }

        [TestMethod]
        public void LegacyM4MethodGroupIsCompatibleButCannotPassRequiredRuntime()
        {
            var fixture = new RuntimeFixture();
            Func<IngredientOperationalEvidence, IReadOnlyList<IngredientMaturityGateReceipt>> legacy = IngredientMaturityEvidenceValidator.ValidateM4;
            var receipts = legacy(fixture.Evidence);
            Assert.AreEqual(5, receipts.Count(value => value.Passed));
            AssertRuntimeFailure(receipts);
            Assert.IsTrue(fixture.Validate().All(value => value.Passed));
        }

        [DataTestMethod]
        [DataRow(RuntimeVerificationStatus.NotRequired)]
        [DataRow(RuntimeVerificationStatus.Passed)]
        public void NonRequiredRuntimeRetainsTheLegacyM4Path(RuntimeVerificationStatus status)
        {
            var fixture = new RuntimeFixture();
            var evidence = fixture.Evidence;
            evidence.RuntimeRequired = false;
            evidence.RuntimeReceipt = null;
            evidence.RuntimeArtifactStore = null;
            evidence.AdmittedExecutionPlan = null;
            evidence.AdmittedExecutionPlanDigestSha256 = null;
            evidence.ImportReceipt.RuntimeVerificationStatus = status;
            Assert.IsTrue(IngredientMaturityEvidenceValidator.ValidateM4(evidence).All(value => value.Passed));
            Assert.IsTrue(IngredientMaturityEvidenceValidator.ValidateM4(null, evidence).All(value => value.Passed));
        }

        [DataTestMethod]
        [DataRow("null")]
        [DataRow("missing-producer")]
        [DataRow("foreign-implementation")]
        [DataRow("malformed-implementation")]
        [DataRow("missing-identity")]
        [DataRow("missing-source")]
        [DataRow("missing-target")]
        [DataRow("missing-outcome")]
        public void RequiredRuntimeRejectsMissingOrForeignConsumerContext(string mutation)
        {
            var fixture = new RuntimeFixture();
            var context = RoundTrip(fixture.Context);
            switch (mutation)
            {
                case "null": context = null; break;
                case "missing-producer": context.Producer = null; break;
                case "foreign-implementation": context.Producer.ImplementationCommit = new string('f', 40); break;
                case "malformed-implementation": context.Producer.ImplementationCommit = "short-ref"; break;
                case "missing-identity": context.Identity = null; break;
                case "missing-source": context.Source = null; break;
                case "missing-target": context.Target = null; break;
                case "missing-outcome": context.TechnicalOutcome = null; break;
                default: Assert.Fail("Unknown context mutation."); break;
            }
            AssertRuntimeFailure(IngredientMaturityEvidenceValidator.ValidateM4(context, fixture.Evidence));
        }

        [DataTestMethod]
        [DataRow("missing-runtime")]
        [DataRow("missing-plan")]
        [DataRow("missing-signature")]
        [DataRow("changed-plan")]
        [DataRow("page-digest")]
        [DataRow("missing-admission")]
        [DataRow("missing-admission-digest")]
        [DataRow("admission-digest")]
        [DataRow("admission-page-plan")]
        [DataRow("admission-target")]
        [DataRow("admission-schema")]
        [DataRow("admission-source")]
        [DataRow("admission-operations")]
        [DataRow("duplicate-operations")]
        [DataRow("missing-import")]
        [DataRow("import-admission")]
        [DataRow("import-source")]
        [DataRow("import-operations")]
        [DataRow("import-no-native-steps")]
        [DataRow("import-not-native")]
        [DataRow("import-partial")]
        [DataRow("runtime-plan")]
        [DataRow("runtime-admission")]
        [DataRow("runtime-operation")]
        [DataRow("runtime-operations-null")]
        [DataRow("runtime-mutation-operation")]
        [DataRow("runtime-readback-operation")]
        [DataRow("runtime-runtime-operation")]
        [DataRow("runtime-cleanup-operation")]
        [DataRow("runtime-source-null")]
        [DataRow("runtime-source-identity")]
        [DataRow("runtime-source-version")]
        [DataRow("runtime-source-etag")]
        [DataRow("runtime-source-label")]
        [DataRow("runtime-source-observed")]
        [DataRow("runtime-source-modified")]
        [DataRow("runtime-import-digest")]
        [DataRow("import-resealed-after-runtime")]
        [DataRow("runtime-target")]
        [DataRow("runtime-schema")]
        [DataRow("manifest-schema")]
        [DataRow("missing-manifest")]
        [DataRow("manifest-digest")]
        public void RequiredRuntimeRejectsBrokenAdmissionImportAndRuntimeJoins(string mutation)
        {
            var fixture = new RuntimeFixture();
            var e = fixture.Evidence;
            var r = e.RuntimeReceipt;
            var foreign = new string('f', 64);
            var operation = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
            switch (mutation)
            {
                case "missing-runtime": e.RuntimeReceipt = null; break;
                case "missing-plan": e.Plan = null; break;
                case "missing-signature": e.ActionSignature = null; break;
                case "changed-plan": e.Plan.SourceSnapshotDigest = foreign; break;
                case "page-digest": e.AdmittedPlanDigest = foreign; break;
                case "missing-admission": e.AdmittedExecutionPlan = null; break;
                case "missing-admission-digest": e.AdmittedExecutionPlanDigestSha256 = null; break;
                case "admission-digest": e.AdmittedExecutionPlanDigestSha256 = foreign; break;
                case "admission-page-plan": e.AdmittedExecutionPlan.PlanDigest = foreign; break;
                case "admission-target": e.AdmittedExecutionPlan.TargetIdentity += "/foreign"; break;
                case "admission-schema": e.AdmittedExecutionPlan.SchemaVersion = "unsupported/v2"; break;
                case "admission-source": e.AdmittedExecutionPlan.SourceVersion = null; break;
                case "admission-operations": e.AdmittedExecutionPlan.Operations = null; break;
                case "duplicate-operations": e.AdmittedExecutionPlan.Operations.RuntimeOperationId = e.AdmittedExecutionPlan.Operations.MutationOperationId; break;
                case "missing-import": e.ImportReceipt = null; break;
                case "import-admission": e.ImportReceipt.AdmittedPlanDigestSha256 = foreign; break;
                case "import-source": e.ImportReceipt.SourceVersion.VersionDigestSha256 = foreign; break;
                case "import-operations": e.ImportReceipt.Operations.ReadbackOperationId = operation; break;
                case "import-no-native-steps": e.ImportReceipt.Steps.Clear(); break;
                case "import-not-native": e.ImportReceipt.MutationStarted = false; break;
                case "import-partial": e.ImportReceipt.PartialExecution = true; break;
                case "runtime-plan": r.PlanDigest = foreign; break;
                case "runtime-admission": r.AdmittedPlanDigestSha256 = foreign; break;
                case "runtime-operation": r.OperationId = operation; break;
                case "runtime-operations-null": r.Operations = null; break;
                case "runtime-mutation-operation": r.Operations.MutationOperationId = operation; break;
                case "runtime-readback-operation": r.Operations.ReadbackOperationId = operation; break;
                case "runtime-runtime-operation": r.Operations.RuntimeOperationId = operation; break;
                case "runtime-cleanup-operation": r.Operations.CleanupOperationId = operation; break;
                case "runtime-source-null": r.SourceVersion = null; break;
                case "runtime-source-identity": r.SourceVersion.IdentityDigestSha256 = foreign; break;
                case "runtime-source-version": r.SourceVersion.VersionDigestSha256 = foreign; break;
                case "runtime-source-etag": r.SourceVersion.ETag = "foreign-etag"; break;
                case "runtime-source-label": r.SourceVersion.VersionLabel = "foreign-label"; break;
                case "runtime-source-observed": r.SourceVersion.ObservedAtUtc = r.SourceVersion.ObservedAtUtc.AddSeconds(-1); break;
                case "runtime-source-modified": r.SourceVersion.LastModifiedUtc = r.SourceVersion.LastModifiedUtc.Value.AddSeconds(-1); break;
                case "runtime-import-digest": r.ImportReceiptDigestSha256 = foreign; break;
                case "import-resealed-after-runtime": e.ImportReceipt.Warnings.Add("different-import-receipt"); break;
                case "runtime-target": r.TargetIdentity += "/foreign"; break;
                case "runtime-schema": r.SchemaVersion = "unsupported/v2"; break;
                case "manifest-schema": e.Plan.RuntimeVerification.SchemaVersion = "unsupported/v2"; fixture.RefreshDigests(); break;
                case "missing-manifest": e.Plan.RuntimeVerification = null; break;
                case "manifest-digest": r.RequirementsManifestDigestSha256 = foreign; break;
                default: Assert.Fail("Unknown cross-binding mutation."); break;
            }
            AssertRuntimeFailure(fixture.Validate());
        }

        [DataTestMethod]
        [DataRow("missing-store")]
        [DataRow("store-io-error")]
        [DataRow("store-access-denied")]
        [DataRow("missing-html")]
        [DataRow("corrupt-html")]
        [DataRow("missing-dom")]
        [DataRow("corrupt-dom")]
        [DataRow("missing-screenshot")]
        [DataRow("corrupt-screenshot")]
        [DataRow("unsafe-html")]
        [DataRow("unsafe-dom")]
        [DataRow("unsafe-screenshot")]
        [DataRow("partial-screenshot")]
        [DataRow("html-length")]
        [DataRow("foreign-implementation")]
        [DataRow("foreign-result-implementation")]
        [DataRow("missing-browser")]
        [DataRow("old-browser")]
        [DataRow("foreign-result-browser")]
        [DataRow("http-denied")]
        [DataRow("http-foreign-url")]
        [DataRow("missing-http")]
        [DataRow("missing-cache")]
        [DataRow("cache-mode")]
        [DataRow("disk-cache")]
        [DataRow("service-worker")]
        public void RequiredRuntimeKeepsDNativeArtifactBrowserAndHttpRejections(string mutation)
        {
            var fixture = new RuntimeFixture(screenshot: true);
            var e = fixture.Evidence;
            var r = e.RuntimeReceipt.Results[0];
            switch (mutation)
            {
                case "missing-store": e.RuntimeArtifactStore = null; break;
                case "store-io-error": e.RuntimeArtifactStore = new UnreadableArtifactStore(new IOException("fixture:unreadable")); break;
                case "store-access-denied": e.RuntimeArtifactStore = new UnreadableArtifactStore(new UnauthorizedAccessException("fixture:denied")); break;
                case "missing-html": fixture.Store.Remove(r.EvidenceArtifactSha256); break;
                case "corrupt-html": r.EvidenceArtifactLength = CorruptAndMeasure(fixture.Store, r.EvidenceArtifactSha256); break;
                case "missing-dom": fixture.Store.Remove(r.DomProbeArtifactSha256); break;
                case "corrupt-dom": r.DomProbeArtifactLength = CorruptAndMeasure(fixture.Store, r.DomProbeArtifactSha256); break;
                case "missing-screenshot": fixture.Store.Remove(r.ScreenshotArtifactSha256); break;
                case "corrupt-screenshot": r.ScreenshotArtifactLength = CorruptAndMeasure(fixture.Store, r.ScreenshotArtifactSha256); break;
                case "unsafe-html": r.EvidenceArtifactLocator = "../outside.html"; break;
                case "unsafe-dom": r.DomProbeArtifactLocator = "../outside.json"; break;
                case "unsafe-screenshot": r.ScreenshotArtifactLocator = "../outside.png"; break;
                case "partial-screenshot": r.ScreenshotArtifactLength = null; break;
                case "html-length": r.EvidenceArtifactLength++; break;
                case "foreign-implementation": e.RuntimeReceipt.ImplementationRef = new string('f', 40); break;
                case "foreign-result-implementation": r.ImplementationRef = new string('f', 40); break;
                case "missing-browser": e.RuntimeReceipt.BrowserContext = null; break;
                case "old-browser": e.RuntimeReceipt.BrowserContext.FreshContext = false; break;
                case "foreign-result-browser": r.BrowserContextId = "foreign-context"; break;
                case "http-denied": r.Http.StatusCode = 403; break;
                case "http-foreign-url": r.Http.FinalUrl += "/foreign"; break;
                case "missing-http": r.Http = null; break;
                case "missing-cache": r.Cache = null; break;
                case "cache-mode": r.Cache.RequestMode = "default"; break;
                case "disk-cache": r.Cache.FromDiskCache = true; break;
                case "service-worker": r.Cache.FromServiceWorker = true; break;
                default: Assert.Fail("Unknown native runtime evidence mutation."); break;
            }
            AssertRuntimeFailure(fixture.Validate());
        }

        [DataTestMethod]
        [DataRow("execution-default")]
        [DataRow("execution-offset")]
        [DataRow("runtime-stale")]
        [DataRow("runtime-offset")]
        [DataRow("runtime-before-navigation")]
        [DataRow("import-offset")]
        [DataRow("import-before-execution")]
        [DataRow("import-step-offset")]
        [DataRow("journal-offset")]
        [DataRow("verification-offset")]
        [DataRow("browser-stale")]
        [DataRow("browser-offset")]
        [DataRow("navigation-before-import")]
        [DataRow("navigation-offset")]
        [DataRow("http-offset")]
        [DataRow("http-before-navigation")]
        [DataRow("http-after-completion")]
        [DataRow("source-offset")]
        [DataRow("source-after-execution")]
        [DataRow("source-modified-offset")]
        [DataRow("source-modified-after-observation")]
        public void RequiredRuntimeRejectsResealedStaleAndNonUtcTimelines(string mutation)
        {
            var fixture = new RuntimeFixture();
            var e = fixture.Evidence;
            var r = e.RuntimeReceipt;
            var offset = TimeSpan.FromHours(1);
            switch (mutation)
            {
                case "execution-default": e.ExecutionStartedAtUtc = default; break;
                case "execution-offset": e.ExecutionStartedAtUtc = e.ExecutionStartedAtUtc.ToOffset(offset); break;
                case "runtime-stale": r.CompletedAtUtc = e.ExecutionStartedAtUtc.AddSeconds(-1); break;
                case "runtime-offset": r.CompletedAtUtc = r.CompletedAtUtc.ToOffset(offset); break;
                case "runtime-before-navigation": r.CompletedAtUtc = r.BrowserContext.FirstNavigationAtUtc.AddSeconds(-1); break;
                case "import-offset": e.ImportReceipt.StartedAtUtc = e.ImportReceipt.StartedAtUtc.ToOffset(offset); break;
                case "import-before-execution": e.ImportReceipt.StartedAtUtc = e.ExecutionStartedAtUtc.AddSeconds(-1); break;
                case "import-step-offset": e.ImportReceipt.Steps[0].CompletedAtUtc = e.ImportReceipt.Steps[0].CompletedAtUtc.ToOffset(offset); break;
                case "journal-offset": e.JournalState.RecordedAtUtc = e.JournalState.RecordedAtUtc.ToOffset(offset); break;
                case "verification-offset": e.VerificationReceipt.VerifiedAtUtc = e.VerificationReceipt.VerifiedAtUtc.ToOffset(offset); break;
                case "browser-stale": r.BrowserContext.CreatedAtUtc = e.ExecutionStartedAtUtc.AddSeconds(-1); break;
                case "browser-offset": r.BrowserContext.CreatedAtUtc = r.BrowserContext.CreatedAtUtc.ToOffset(offset); break;
                case "navigation-before-import": r.BrowserContext.FirstNavigationAtUtc = e.ImportReceipt.CompletedAtUtc.AddSeconds(-1); break;
                case "navigation-offset": r.BrowserContext.FirstNavigationAtUtc = r.BrowserContext.FirstNavigationAtUtc.ToOffset(offset); break;
                case "http-offset": r.Results[0].Http.CapturedAtUtc = r.Results[0].Http.CapturedAtUtc.ToOffset(offset); break;
                case "http-before-navigation": r.Results[0].Http.CapturedAtUtc = r.BrowserContext.FirstNavigationAtUtc.AddSeconds(-1); break;
                case "http-after-completion": r.Results[0].Http.CapturedAtUtc = r.CompletedAtUtc.AddSeconds(1); break;
                case "source-offset": e.AdmittedExecutionPlan.SourceVersion.ObservedAtUtc = e.AdmittedExecutionPlan.SourceVersion.ObservedAtUtc.ToOffset(offset); break;
                case "source-after-execution": e.AdmittedExecutionPlan.SourceVersion.ObservedAtUtc = e.ExecutionStartedAtUtc.AddSeconds(1); break;
                case "source-modified-offset": e.AdmittedExecutionPlan.SourceVersion.LastModifiedUtc = e.AdmittedExecutionPlan.SourceVersion.LastModifiedUtc.Value.ToOffset(offset); break;
                case "source-modified-after-observation": e.AdmittedExecutionPlan.SourceVersion.LastModifiedUtc = e.AdmittedExecutionPlan.SourceVersion.ObservedAtUtc.AddSeconds(1); break;
                default: Assert.Fail("Unknown timeline mutation."); break;
            }
            e.ImportReceipt.SourceVersion = RoundTrip(e.AdmittedExecutionPlan.SourceVersion);
            r.SourceVersion = RoundTrip(e.AdmittedExecutionPlan.SourceVersion);
            // Re-seal all digest bindings so the negative tests the timestamps,
            // including equivalent instants with non-UTC offsets, not a stale hash.
            fixture.RefreshDigests();
            AssertRuntimeFailure(fixture.Validate());
        }

        [DataTestMethod]
        [DataRow("missing-results")]
        [DataRow("missing-required-result")]
        [DataRow("null-result")]
        [DataRow("empty-result-id")]
        [DataRow("duplicate-result")]
        [DataRow("foreign-result")]
        [DataRow("null-requirements")]
        [DataRow("null-requirement")]
        [DataRow("empty-requirement-id")]
        [DataRow("duplicate-requirement")]
        [DataRow("no-required-passed")]
        [DataRow("no-required-not-required")]
        [DataRow("failed-result-passed-status")]
        [DataRow("passed-result-failed-status")]
        [DataRow("failed-result-failed-status")]
        [DataRow("pending-status")]
        [DataRow("not-required-status")]
        public void RequiredRuntimeRejectsResealedCoverageAndStatusContradictions(string mutation)
        {
            var fixture = new RuntimeFixture();
            var e = fixture.Evidence;
            var r = e.RuntimeReceipt;
            var manifest = e.Plan.RuntimeVerification;
            switch (mutation)
            {
                case "missing-results": r.Results = null; break;
                case "missing-required-result": r.Results.Clear(); break;
                case "null-result": r.Results.Add(null); break;
                case "empty-result-id": r.Results[0].RequirementId = " "; break;
                case "duplicate-result": r.Results.Add(RoundTrip(r.Results[0])); break;
                case "foreign-result": var foreign = RoundTrip(r.Results[0]); foreign.RequirementId = "foreign"; r.Results.Add(foreign); break;
                case "null-requirements": manifest.Requirements = null; break;
                case "null-requirement": manifest.Requirements.Add(null); break;
                case "empty-requirement-id": manifest.Requirements[0].Id = " "; break;
                case "duplicate-requirement": manifest.Requirements.Add(RoundTrip(manifest.Requirements[0])); break;
                case "no-required-passed": manifest.Requirements[0].Required = false; break;
                case "no-required-not-required": manifest.Requirements[0].Required = false; r.Status = RuntimeVerificationStatus.NotRequired; break;
                case "failed-result-passed-status": r.Results[0].Passed = false; break;
                case "passed-result-failed-status": r.Status = RuntimeVerificationStatus.Failed; break;
                case "failed-result-failed-status": r.Results[0].Passed = false; r.Status = RuntimeVerificationStatus.Failed; break;
                case "pending-status": r.Status = RuntimeVerificationStatus.Pending; break;
                case "not-required-status": r.Status = RuntimeVerificationStatus.NotRequired; break;
                default: Assert.Fail("Unknown coverage mutation."); break;
            }
            fixture.RefreshDigests();
            AssertRuntimeFailure(fixture.Validate());
        }

        [TestMethod]
        public void OptionalFailedRuntimeResultDoesNotOverridePassingRequiredCoverage()
        {
            var fixture = new RuntimeFixture();
            var e = fixture.Evidence;
            e.Plan.RuntimeVerification.Requirements.Add(new RuntimeVerificationRequirement
            {
                Id = "fixture:optional", Required = false, Kind = RuntimeVerificationRequirementKind.VisualEquality
            });
            var optional = RoundTrip(e.RuntimeReceipt.Results[0]);
            optional.RequirementId = "fixture:optional";
            optional.Passed = false;
            e.RuntimeReceipt.Results.Add(optional);
            fixture.RefreshDigests();
            Assert.IsTrue(fixture.Validate().All(value => value.Passed));
        }

        [DataTestMethod]
        [DataRow("cleanup")]
        [DataRow("retry")]
        public void RequiredRuntimeDoesNotReleaseCleanupOrRetryGates(string mutation)
        {
            var fixture = new RuntimeFixture();
            if (mutation == "cleanup") fixture.Evidence.CleanupPassed = false;
            else fixture.Evidence.RetryPassed = false;
            AssertRuntimeFailure(fixture.Validate());
        }

        private static long CorruptAndMeasure(MemoryArtifactStore store, string digest)
        {
            store.Corrupt(digest);
            using (var bytes = store.OpenRead(digest))
            {
                // Keep the declared length correct: only exact byte hashing can
                // reject this counterexample, not a missing/length-only check.
                return bytes.Length;
            }
        }

        private sealed class UnreadableArtifactStore : IMigrationArtifactStore
        {
            private readonly Exception failure;
            public UnreadableArtifactStore(Exception failure) { this.failure = failure; }
            public bool Contains(string sha256) => throw failure;
            public Stream OpenRead(string sha256) => throw failure;
            public ArtifactReference Put(Stream content, string mediaType = null, string originalName = null) => throw failure;
        }

        private static void AssertRuntimeFailure(IReadOnlyList<IngredientMaturityGateReceipt> receipts)
        {
            var gate = receipts.Single(value => value.GateId == IngredientMaturityGateCatalog.RuntimeCleanupRetry);
            Assert.IsFalse(gate.Passed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(gate.FailureReason));
        }

        // This is a synthetic shared-contract fixture, not a lane producer or a
        // runtime verdict. Reuse the existing test artifact store and D factory.
        private sealed class RuntimeFixture
        {
            public RuntimeFixture(bool screenshot = false)
            {
                var fixture = new Fixture();
                Context = fixture.Context;
                var now = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
                var target = Context.Target.TargetIdentity;
                var action = new PageIngredientAction
                {
                    ActionId = "action:fixture-webpart", IngredientId = fixture.Node.Id,
                    Capability = IngredientCapability.Available, Disposition = IngredientDisposition.Preserve,
                    TargetIdentity = target, PolicyId = "fixture.preserve/v1", PolicyVersion = "v1"
                };
                var plan = new PublishingPageMigrationPlan
                {
                    SourceSnapshotDigest = Context.Source.SourceSnapshotDigest,
                    IngredientGraph = new CanonicalPageIngredientGraph { Nodes = new List<PageIngredientNode> { fixture.Node } },
                    IngredientActions = new List<PageIngredientAction> { action },
                    RuntimeVerification = new RuntimeVerificationManifest
                    {
                        Requirements = new List<RuntimeVerificationRequirement>
                        {
                            new RuntimeVerificationRequirement { Id = "fixture:required-webpart", Kind = RuntimeVerificationRequirementKind.AuthoredDomEquality }
                        }
                    }
                };
                var planDigest = PublishingPageDigest.ComputePlanDigest(plan);
                var signature = MigrationActionSignature.Create(action.ActionId, "preserve-webpart",
                    fixture.Node.EvidenceDigest, MigrationActionSignature.EmptySelectionReceiptDigest, target,
                    MigrationDigest.ComputeSha256("fixture:semantic-webpart"));
                var admission = new AdmittedReproExecutionPlan
                {
                    PlanDigest = planDigest, TargetIdentity = target,
                    SourceVersion = new CurrentSourceVersionIdentity
                    {
                        IdentityDigestSha256 = MigrationDigest.ComputeSha256(Context.Source.PageOrListItemIdentity),
                        VersionDigestSha256 = MigrationDigest.ComputeSha256(Context.Source.SourceVersion),
                        ETag = "fixture-etag", VersionLabel = "1.0", LastModifiedUtc = now.AddSeconds(-2), ObservedAtUtc = now.AddSeconds(-1)
                    },
                    Operations = new ReproOperationIds
                    {
                        MutationOperationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        ReadbackOperationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        RuntimeOperationId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        CleanupOperationId = Guid.Parse("44444444-4444-4444-4444-444444444444")
                    }
                };
                var admissionDigest = AdmittedReproExecutionPlanValidator.ValidateAndComputeDigest(admission, planDigest, target);
                var import = new PublishingPageImportReceipt
                {
                    OperationId = admission.Operations.MutationOperationId, ApprovedPlanDigest = planDigest,
                    AdmittedPlanDigestSha256 = admissionDigest, Operations = RoundTrip(admission.Operations), SourceVersion = RoundTrip(admission.SourceVersion),
                    StartedAtUtc = now, CompletedAtUtc = now.AddSeconds(2), MutationStarted = true,
                    ExecutionStatus = MigrationExecutionStatus.Succeeded, OwnershipMatched = true,
                    FreshReadbackPassed = true, StorageVerificationStatus = StorageVerificationStatus.Passed,
                    RuntimeVerificationStatus = RuntimeVerificationStatus.Pending,
                    VerifiedIngredientIds = new List<string> { fixture.Node.Id },
                    Steps = new List<MigrationMutationReceipt>
                    {
                        new MigrationMutationReceipt
                        {
                            Sequence = 0, OperationId = admission.Operations.MutationOperationId, PlanDigest = planDigest,
                            ActionId = signature.ActionId, ActionSignature = signature.Signature, Outcome = MutationOutcome.Applied, CompletedAtUtc = now.AddSeconds(1)
                        }
                    }
                };
                Store = new MemoryArtifactStore();
                var html = Store.PutText("<html><body>synthetic contract fixture</body></html>", "text/html");
                var dom = Store.PutText("{\"fixture\":\"shared-required-runtime\"}", "application/json");
                var browser = new RuntimeBrowserContextIdentity
                {
                    FreshContext = true, IsIncognito = true, BrowserProduct = "fixture-browser", BrowserVersion = "1.0", ProtocolVersion = "1.3",
                    ProfileIdentitySha256 = MigrationDigest.ComputeSha256("fixture:profile"), BrowserContextId = "fixture:context", TargetId = "fixture:target",
                    CreatedAtUtc = now.AddSeconds(3), FirstNavigationAtUtc = now.AddSeconds(4)
                };
                var result = new RuntimeVerificationResult
                {
                    RequirementId = "fixture:required-webpart", Passed = true, ImplementationRef = Context.Producer.ImplementationCommit,
                    BrowserContextId = browser.BrowserContextId, EvidenceArtifactSha256 = html.Sha256, EvidenceArtifactLength = html.Length,
                    EvidenceArtifactLocator = "runtime/fixture.html", DomProbeArtifactSha256 = dom.Sha256, DomProbeArtifactLength = dom.Length,
                    DomProbeArtifactLocator = "runtime/fixture-dom.json",
                    Http = new RuntimeHttpEvidence
                    {
                        RequestedUrl = target, FinalUrl = target, Method = "GET", StatusCode = 200, ContentType = "text/html",
                        ResponseHeadersDigestSha256 = MigrationDigest.ComputeSha256("fixture:headers"), CapturedAtUtc = now.AddSeconds(5)
                    },
                    Cache = new RuntimeCacheEvidence { RequestMode = "no-store", CacheDisabled = true, RequestCacheControl = "no-store", RequestPragma = "no-cache" }
                };
                if (screenshot)
                {
                    var capture = Store.PutBytes(new byte[] { 137, 80, 78, 71, 1, 2, 3, 4 }, "image/png");
                    result.ScreenshotArtifactSha256 = capture.Sha256;
                    result.ScreenshotArtifactLength = capture.Length;
                    result.ScreenshotArtifactLocator = "runtime/fixture.png";
                }
                var runtime = RuntimeVerificationReceiptFactory.Create(admission, admissionDigest,
                    MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(import)), target,
                    plan.RuntimeVerification, Context.Producer.ImplementationCommit, browser, new[] { result }, now.AddSeconds(6));
                Evidence = new IngredientOperationalEvidence
                {
                    Plan = plan, AdmittedPlanDigest = planDigest, AdmissionPassed = true, IngredientId = fixture.Node.Id, ActionSignature = signature,
                    AdmittedExecutionPlan = admission, AdmittedExecutionPlanDigestSha256 = admissionDigest, RuntimeArtifactStore = Store,
                    ImportReceipt = import, RuntimeReceipt = RoundTrip(runtime), RuntimeRequired = true, ExecutionStartedAtUtc = now,
                    CleanupRequired = true, CleanupPassed = true, RetryEvidenceRequired = true, RetryPassed = true,
                    JournalState = new MigrationExecutionStateReceipt
                    {
                        OperationId = import.OperationId, PlanDigest = planDigest, Status = MigrationExecutionStatus.Succeeded, RecordedAtUtc = now.AddSeconds(2)
                    },
                    VerificationReceipt = new MigrationMutationVerificationReceipt
                    {
                        OperationId = import.OperationId, PlanDigest = planDigest, ActionId = signature.ActionId, ActionSignature = signature.Signature,
                        FreshReadbackPassed = true, Ownership = MigrationTargetOwnership.MigrationOwned, ProvenanceMatched = true, VerifiedAtUtc = now.AddSeconds(2)
                    },
                    PlanAdmissionEvidenceReferences = new List<string> { "fixture:admission" }, ActionEvidenceReferences = new List<string> { "fixture:action" },
                    ReceiptEvidenceReferences = new List<string> { "fixture:native-import" }, FreshReadbackEvidenceReferences = new List<string> { "fixture:readback" },
                    RuntimeCleanupRetryEvidenceReferences = new List<string> { "fixture:runtime-cleanup-retry" }
                };
            }

            public IngredientMaturityEvaluationContext Context { get; }
            public IngredientOperationalEvidence Evidence { get; }
            public MemoryArtifactStore Store { get; }

            public IReadOnlyList<IngredientMaturityGateReceipt> Validate() => IngredientMaturityEvidenceValidator.ValidateM4(Context, Evidence);

            public void RefreshDigests()
            {
                var e = Evidence;
                e.AdmittedPlanDigest = PublishingPageDigest.ComputePlanDigest(e.Plan);
                e.AdmittedExecutionPlan.PlanDigest = e.AdmittedPlanDigest;
                e.AdmittedExecutionPlanDigestSha256 = MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(e.AdmittedExecutionPlan));
                e.ImportReceipt.ApprovedPlanDigest = e.AdmittedPlanDigest;
                e.ImportReceipt.AdmittedPlanDigestSha256 = e.AdmittedExecutionPlanDigestSha256;
                foreach (var step in e.ImportReceipt.Steps) step.PlanDigest = e.AdmittedPlanDigest;
                e.JournalState.PlanDigest = e.AdmittedPlanDigest;
                e.VerificationReceipt.PlanDigest = e.AdmittedPlanDigest;
                e.RuntimeReceipt.PlanDigest = e.AdmittedPlanDigest;
                e.RuntimeReceipt.AdmittedPlanDigestSha256 = e.AdmittedExecutionPlanDigestSha256;
                e.RuntimeReceipt.ImportReceiptDigestSha256 = MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(e.ImportReceipt));
                e.RuntimeReceipt.RequirementsManifestDigestSha256 = MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(e.Plan.RuntimeVerification));
            }
        }

        private static PageIngredientPrimaryOwnerDescriptor Descriptor(string id, string subtype = Subtype)
        {
            return new PageIngredientPrimaryOwnerDescriptor(id, PageIngredientKind.WebPart, subtype, Role, PredicateId, Lane);
        }

        private static Dictionary<string, Func<IngredientOwnershipSourceContext, bool>> Predicates(Func<IngredientOwnershipSourceContext, bool> predicate)
        {
            return new Dictionary<string, Func<IngredientOwnershipSourceContext, bool>>(StringComparer.Ordinal) { [PredicateId] = predicate };
        }

        private static T RoundTrip<T>(T value)
        {
            return MigrationContractSerializer.Deserialize<T>(MigrationContractSerializer.SerializeCanonical(value));
        }

        private static void AssertCanonicalBytes(string expected, string actual)
        {
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
        }

        private static PageIngredientNode LegacyNode()
        {
            return new PageIngredientNode
            {
                Id = "webpart:legacy-fixture",
                Kind = PageIngredientKind.WebPart,
                Label = "Legacy <WebPart>",
                HasContent = true,
                Ownership = PageIngredientOwnership.SourceOwned,
                SourceAuthority = "MSIT-readonly",
                EvidenceDigest = new string('b', 64),
                RuntimeRequirement = "persisted-instance",
                EvidenceReferences = new List<string> { "source#1", "source#2" }
            };
        }

        // Copyable contributor seam: receive independently acquired domain evidence
        // and the shared registry; call validators with the consumer's context;
        // return receipts, never a maturity level. Later gates need explicit same-
        // claim joins in the lane before its existing PnP domain validators run.
        // This is a test template, not a second product WebPart contributor/registry.
        private sealed class ContextBoundContributorTemplate : IIngredientMaturityContributor
        {
            private readonly PageIngredientNode node;
            private readonly PublishingPageCaptureBundle snapshot;
            private readonly PublishingPageIngredientPrimaryOwnerRegistry owners;
            private readonly IngredientLiveEvidence live;

            public ContextBoundContributorTemplate(PageIngredientNode node, PublishingPageCaptureBundle snapshot,
                PublishingPageIngredientPrimaryOwnerRegistry owners, IngredientLiveEvidence live)
            {
                this.node = node;
                this.snapshot = snapshot;
                this.owners = owners;
                this.live = live;
            }

            public string ContributorId => "test.context-bound-contributor/v2";

            public string Lane => RequiredWebPartSharedContractTests.Lane;

            public IngredientMaturityContribution Contribute(IngredientMaturityEvaluationContext context)
            {
                return new IngredientMaturityContribution
                {
                    ContributorId = ContributorId,
                    ClaimId = context.Identity.ClaimId,
                    Lane = Lane,
                    IngredientId = context.Identity.IngredientId,
                    GateReceipts = IngredientMaturityEvidenceValidator.ValidateM0(context, node, snapshot, owners, new[] { "fixture:m0" })
                        .Concat(IngredientMaturityEvidenceValidator.ValidateM1(context, live)).ToList()
                };
            }
        }

        private sealed class Fixture
        {
            public Fixture()
            {
                var instance = Guid.Parse("11111111-1111-1111-1111-111111111111");
                var time = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
                var xml = "<webPart fixture=\"required-shared-contract\" />";
                var artifactDigest = MigrationDigest.ComputeSha256(xml);
                Context = new IngredientMaturityEvaluationContext
                {
                    Identity = new IngredientMaturityIdentity
                    {
                        ClaimId = new string('a', 64), Lane = Lane, IngredientId = "webpart:" + instance.ToString("D"),
                        Kind = PageIngredientKind.WebPart, Subtype = Subtype, SemanticRole = Role, SourcePredicateId = PredicateId
                    },
                    Source = new IngredientMaturitySourceBinding
                    {
                        PageOrListItemIdentity = "fixture:page:item-12", SourceVersion = "version:1.0",
                        SourceArtifactDigest = artifactDigest, SourceSnapshotDigest = new string('c', 64)
                    },
                    Target = new IngredientMaturityTargetBinding { TargetProfile = "CUPCollect", TargetIdentity = "cupcollect:/fixture.aspx" },
                    Producer = new IngredientMaturityProducerBinding
                    {
                        ProducerId = "fixture:pnp-framework", ProducerVersion = "v1", ImplementationCommit = new string('d', 40)
                    },
                    TargetMaturity = IngredientMaturityLevel.M5,
                    TechnicalOutcome = new IngredientTechnicalOutcome
                    {
                        Status = IngredientTechnicalStatus.Conditional, PnPMigrationOutcome = PageMigrationOutcome.ExecutableWithLoss,
                        ReasonCode = "synthetic-contract-control"
                    },
                    ObservationWindowStartUtc = time.AddMinutes(-2), ObservationWindowEndUtc = time.AddMinutes(1)
                };
                Node = new PageIngredientNode
                {
                    Id = "webpart:" + instance.ToString("D"), Kind = PageIngredientKind.WebPart,
                    Subtype = Subtype, SemanticRole = Role, SourcePredicateId = PredicateId, PrimaryOwnerLane = Lane,
                    SourcePageOrListItemIdentity = "fixture:page:item-12", SourceVersionIdentity = "version:1.0",
                    EvidenceDigest = artifactDigest, HasContent = true, Ownership = PageIngredientOwnership.SourceOwned
                };
                Snapshot = new PublishingPageCaptureBundle
                {
                    WebParts = new List<ClassicWebPartSnapshot>
                    {
                        new ClassicWebPartSnapshot { Id = instance, ExportXml = xml, ExportSha256 = artifactDigest }
                    }
                };
                Registry = new PublishingPageIngredientPrimaryOwnerRegistry(new[] { Descriptor("fixture.owner") },
                    Predicates(source => source.HasBoundSourceIdentity && source.Snapshot?.WebParts != null
                        && source.Snapshot.WebParts.Count(part => part != null
                            && string.Equals("webpart:" + part.Id.ToString("D"), source.Node.Id, StringComparison.Ordinal)
                            && string.Equals(part.ExportSha256, source.Node.EvidenceDigest, StringComparison.Ordinal)
                            && string.Equals(MigrationDigest.ComputeSha256(part.ExportXml), part.ExportSha256, StringComparison.Ordinal)) == 1));
                Live = new IngredientLiveEvidence
                {
                    SourceAuthenticated = true, TargetFreshReadback = true, ReadbackStartedAtUtc = time.AddMinutes(-1),
                    SourceEvidenceReferences = new List<string> { "fixture:source" },
                    TargetEvidenceReferences = new List<string> { "fixture:target" },
                    Observations = new List<IngredientValueObservation>
                    {
                        Observation(IngredientObservationOrigin.AuthenticatedSource, "fixture:source", time.AddMinutes(-2)),
                        Observation(IngredientObservationOrigin.CupCollectFreshReadback, "fixture:target", time)
                    }
                };
            }

            public IngredientMaturityEvaluationContext Context { get; }
            public PageIngredientNode Node { get; }
            public PublishingPageCaptureBundle Snapshot { get; }
            public PublishingPageIngredientPrimaryOwnerRegistry Registry { get; }
            public IngredientLiveEvidence Live { get; }

            public IngredientMaturityContributorCatalog Catalog(PageIngredientNode node)
            {
                return new IngredientMaturityContributorCatalog(new[] { new ContextBoundContributorTemplate(node, Snapshot, Registry, Live) });
            }

            public IngredientMaturityAssessment Evaluate(PageIngredientNode node = null)
            {
                return IngredientMaturityEvaluator.Evaluate(Context, Catalog(node ?? Node));
            }

            private IngredientValueObservation Observation(IngredientObservationOrigin origin, string reference, DateTimeOffset time)
            {
                return new IngredientValueObservation
                {
                    ClaimId = Context.Identity.ClaimId, IngredientId = Node.Id, Source = RoundTrip(Context.Source), Target = RoundTrip(Context.Target),
                    ValuePath = "webpart.export", ValueDigest = Node.EvidenceDigest, ObservedAtUtc = time, Origin = origin, EvidenceReference = reference
                };
            }
        }
    }
}
