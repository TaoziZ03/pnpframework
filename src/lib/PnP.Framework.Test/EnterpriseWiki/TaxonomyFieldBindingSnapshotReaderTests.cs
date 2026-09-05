using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Fields;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Schema.ContentTypes;
using PnP.Framework.Migration.Schema.Fields;
using PnP.Framework.Migration.Topology;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class TaxonomyFieldBindingSnapshotReaderTests
    {
        private static readonly Guid FieldId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid TermStoreId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid TermSetId = new Guid("33333333-3333-3333-3333-333333333333");
        private static readonly Guid TextFieldId = new Guid("44444444-4444-4444-4444-444444444444");

        [TestMethod]
        public void ValidSchemaXmlRecoversCompleteBindingAfterTypedMemberFailure()
        {
            var result = TaxonomyFieldBindingSnapshotReader.Read(
                FieldId,
                "Categories",
                ValidSchemaXml(),
                () => throw MemberFailure());

            Assert.IsTrue(result.IsComplete);
            Assert.IsTrue(result.UsedSchemaXmlFallback);
            Assert.AreEqual(TermStoreId, result.Binding.SourceTermStoreId);
            Assert.AreEqual(TermSetId, result.Binding.SourceTermSetId);
            Assert.AreEqual(Guid.Empty, result.Binding.AnchorTermId);
            Assert.AreEqual(TextFieldId, result.Binding.HiddenTextFieldId);
            Assert.IsTrue(result.Binding.Open);
            Assert.AreEqual("Field.SchemaXml/Customization/ArrayOfProperty", result.Sources.Single().Selector);
            StringAssert.Contains(
                string.Join(" ", result.Diagnostics),
                "exceptionType=Microsoft.SharePoint.Client.ServerException");
            Assert.IsFalse(result.Diagnostics.Any(value => value.Contains("Parameter name: member", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void MalformedOrIncompleteSchemaXmlFailsClosed()
        {
            AssertFallbackFailure("<Field Type=\"TaxonomyFieldType\">");
            AssertFallbackFailure(ValidSchemaXml().Replace(Property("Open", "true"), string.Empty));
            AssertFallbackFailure(ValidSchemaXml().Replace(Property("TextField", TextFieldId.ToString("D")), Property("TextField", "not-a-guid")));
            AssertFallbackFailure(ValidSchemaXml().Replace(Property("SspId", TermStoreId.ToString("D")), Property("SspId", Guid.Empty.ToString("D"))));
            AssertFallbackFailure(ValidSchemaXml().Replace(
                "</ArrayOfProperty>",
                Property("Open", "true") + "</ArrayOfProperty>"));
            AssertFallbackFailure(ValidSchemaXml().Replace(
                Property("SspId", TermStoreId.ToString("D")),
                "<Property><Name>SspId</Name><Name>Ambiguous</Name><Value>"
                    + TermStoreId.ToString("D") + "</Value></Property>"
                    + Property("SspId", TermStoreId.ToString("D"))));
            AssertFallbackFailure(ValidSchemaXml().Replace(
                "Type=\"TaxonomyFieldType\"",
                "Type=\"TaxonomyFieldTypeBogus\""));
        }

        [TestMethod]
        public void PerFieldBatchIsolationPreservesOtherTaxonomyBindings()
        {
            var batchRequestCount = 0;
            var isolatedRequestCount = 0;
            var bad = new FieldInput
            {
                Id = FieldId,
                Name = "BrokenCategories",
                SchemaXml = "<Field Type=\"TaxonomyFieldType\"><Customization /></Field>",
                Failure = MemberFailure()
            };
            var good = new FieldInput
            {
                Id = new Guid("55555555-5555-5555-5555-555555555555"),
                Name = "WorkingCategories",
                SchemaXml = ValidSchemaXml(),
                Binding = ValidBinding()
            };

            var results = TaxonomyFieldBindingSnapshotReader.ReadAll(
                new[] { bad, good },
                field => field.Id,
                field => field.Name,
                field => field.SchemaXml,
                fields =>
                {
                    batchRequestCount++;
                    throw MemberFailure();
                },
                field =>
                {
                    isolatedRequestCount++;
                    return field.ReadTyped();
                });

            Assert.AreEqual(2, results.Count);
            Assert.IsFalse(results[bad.Id].IsComplete);
            Assert.IsNull(results[bad.Id].Binding);
            Assert.IsTrue(results[good.Id].IsComplete);
            Assert.AreEqual(TermSetId, results[good.Id].Binding.SourceTermSetId);
            Assert.AreEqual(1, bad.ReadCount);
            Assert.AreEqual(1, good.ReadCount);
            Assert.AreEqual(1, batchRequestCount);
            Assert.AreEqual(2, isolatedRequestCount);
        }

        [TestMethod]
        public void HealthyBatchUsesOneRequestAndNoIsolatedRoundTrips()
        {
            var batchRequestCount = 0;
            var isolatedRequestCount = 0;
            var fields = new[]
            {
                new FieldInput { Id = FieldId, Name = "Categories", SchemaXml = ValidSchemaXml(), Binding = ValidBinding() },
                new FieldInput { Id = new Guid("55555555-5555-5555-5555-555555555555"), Name = "Topics", SchemaXml = ValidSchemaXml(), Binding = ValidBinding() }
            };

            var results = TaxonomyFieldBindingSnapshotReader.ReadAll(
                fields,
                field => field.Id,
                field => field.Name,
                field => field.SchemaXml,
                batch =>
                {
                    batchRequestCount++;
                    return batch.ToDictionary(field => field.Id, field => field.Binding);
                },
                field =>
                {
                    isolatedRequestCount++;
                    return field.ReadTyped();
                });

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.Values.All(value => value.IsComplete && !value.UsedSchemaXmlFallback));
            Assert.AreEqual(1, batchRequestCount);
            Assert.AreEqual(0, isolatedRequestCount);
        }

        [TestMethod]
        public void StatusTextWithoutWireEvidenceDoesNotForgeAuthorizationClassification()
        {
            foreach (var status in new[] { "401 Unauthorized", "403 Forbidden" })
            {
                var result = TaxonomyFieldBindingSnapshotReader.Read(
                    FieldId,
                    "Categories",
                    "<Field Type=\"TaxonomyFieldType\"><Customization /></Field>",
                    () => throw CreateServerException(
                        "The remote server returned " + status + ".",
                        "System.ArgumentOutOfRangeException",
                        -1));

                Assert.IsFalse(result.IsComplete);
                Assert.IsNull(result.Binding);
                Assert.AreEqual(1, result.Sources.Count);
                Assert.AreEqual("Field.SchemaXml/Customization/ArrayOfProperty", result.Sources.Single().Selector);
                StringAssert.Contains(
                    string.Join(" ", result.Diagnostics),
                    "no literal HTTP 401/403 wire evidence was captured");
                Assert.IsFalse(result.Diagnostics.Any(value => value.Contains(status, StringComparison.Ordinal)));
            }
        }

        [TestMethod]
        public void PartialContentTypeWithMissingTaxonomyBindingCannotBecomeExecutableRuntimePlan()
        {
            var field = new FieldSchemaSnapshot
            {
                Id = FieldId,
                InternalName = "Categories",
                TypeAsString = "TaxonomyFieldType",
                SchemaXml = ValidSchemaXml(),
                Role = FieldSchemaRole.DirectBinding,
                Ownership = FieldOwnership.TargetRuntime
            };
            var schema = new ContentTypeSchemaSnapshot
            {
                EvidenceState = ContentTypeSchemaEvidenceState.Partial,
                Availability = EvidenceAvailability.Partial,
                ContentTypeId = "0x010100AABB",
                Name = "Documents",
                ParentContentTypeId = "0x0101",
                RequiredFieldLinks = new[]
                {
                    new ContentTypeFieldLinkSnapshot
                    {
                        FieldId = FieldId,
                        Name = "Categories",
                        Role = FieldSchemaRole.DirectBinding
                    }
                },
                RequiredFieldClosure = new List<FieldSchemaSnapshot> { field }
            };

            Assert.IsFalse(ContentTypeSchemaPlanner.TryCreateTargetRuntimeRequirement(schema, out var plan));
            Assert.IsNull(plan);
        }

        [TestMethod]
        public void MissingHiddenTextCompanionMarksContentTypeClosurePartial()
        {
            var field = new FieldSchemaSnapshot
            {
                Id = FieldId,
                InternalName = "Categories",
                TypeAsString = "TaxonomyFieldType",
                Taxonomy = ValidBinding()
            };
            var diagnostics = new System.Collections.Generic.List<string>();

            Assert.IsFalse(ContentTypeSchemaSnapshotReader.ValidateTaxonomyCompanionClosure(new[] { field }, diagnostics));
            Assert.IsTrue(field.Diagnostics.Any(value => value.StartsWith("TaxonomyBindingHiddenTextCompanionInvalid:", StringComparison.Ordinal)));
            Assert.AreEqual(1, diagnostics.Count);

            var listField = new ListFieldSnapshot
            {
                Id = FieldId,
                InternalName = "Categories",
                TypeAsString = "TaxonomyFieldType",
                Taxonomy = ValidBinding()
            };
            Assert.IsFalse(ListFieldSnapshotReader.ValidateTaxonomyCompanionClosure(new[] { listField }));
            Assert.AreEqual(EvidenceAvailability.Partial, listField.Availability);
            Assert.IsTrue(listField.Diagnostics.Any(value => value.StartsWith("TaxonomyBindingHiddenTextCompanionInvalid:", StringComparison.Ordinal)));

            var selfBinding = ValidBinding();
            selfBinding.HiddenTextFieldId = FieldId;
            field.Taxonomy = selfBinding;
            diagnostics.Clear();
            Assert.IsFalse(ContentTypeSchemaSnapshotReader.ValidateTaxonomyCompanionClosure(new[] { field }, diagnostics));
            StringAssert.Contains(field.Diagnostics.Last(), "reason=self-reference");

            var wrongType = Companion(TextFieldId, "Text", true);
            field.Taxonomy = ValidBinding();
            diagnostics.Clear();
            Assert.IsFalse(ContentTypeSchemaSnapshotReader.ValidateTaxonomyCompanionClosure(new[] { field, wrongType }, diagnostics));
            StringAssert.Contains(field.Diagnostics.Last(), "reason=wrong-type");

            var visibleNote = Companion(TextFieldId, "Note", false);
            diagnostics.Clear();
            Assert.IsFalse(ContentTypeSchemaSnapshotReader.ValidateTaxonomyCompanionClosure(new[] { field, visibleNote }, diagnostics));
            StringAssert.Contains(field.Diagnostics.Last(), "reason=not-hidden");
        }

        [TestMethod]
        public void PartialContentTypeWithIncompleteBindingOrMissingCompanionCannotBecomeRuntimePlan()
        {
            var incomplete = PartialRuntimeSchema(ValidBinding());
            incomplete.RequiredFieldClosure[0].Taxonomy.SourceTermStoreId = Guid.Empty;
            Assert.IsFalse(ContentTypeSchemaPlanner.TryCreateTargetRuntimeRequirement(incomplete, out _));

            var missingCompanion = PartialRuntimeSchema(ValidBinding());
            Assert.IsFalse(ContentTypeSchemaPlanner.TryCreateTargetRuntimeRequirement(missingCompanion, out _));

            var selfReference = PartialRuntimeSchema(ValidBinding());
            selfReference.RequiredFieldClosure[0].Taxonomy.HiddenTextFieldId = FieldId;
            Assert.IsFalse(ContentTypeSchemaPlanner.TryCreateTargetRuntimeRequirement(selfReference, out _));

            var wrongType = PartialRuntimeSchema(ValidBinding());
            wrongType.RequiredFieldClosure.Add(Companion(TextFieldId, "Text", true));
            Assert.IsFalse(ContentTypeSchemaPlanner.TryCreateTargetRuntimeRequirement(wrongType, out _));

            var visibleNote = PartialRuntimeSchema(ValidBinding());
            visibleNote.RequiredFieldClosure.Add(Companion(TextFieldId, "Note", false));
            Assert.IsFalse(ContentTypeSchemaPlanner.TryCreateTargetRuntimeRequirement(visibleNote, out _));

            var readableBogus = PartialRuntimeSchema(ValidBinding());
            readableBogus.EvidenceState = ContentTypeSchemaEvidenceState.Readable;
            readableBogus.Availability = EvidenceAvailability.Captured;
            readableBogus.RequiredFieldClosure[0].TypeAsString = "TaxonomyFieldTypeBogus";
            readableBogus.RequiredFieldClosure.Add(Companion(TextFieldId, "Note", true));
            var contentTypePlan = ContentTypeSchemaPlanner.CreateRequiredClosure(readableBogus);
            Assert.AreEqual(ContentTypeMaterializationDisposition.Block, contentTypePlan.Disposition);
            Assert.AreEqual(
                FieldSchemaMaterializationDisposition.Block,
                contentTypePlan.Fields.Single(value => value.FieldId == FieldId).Disposition);

            var list = BogusTaxonomyList();
            var listPlan = ListMigrationPlanFactory.Create(
                new[] { list },
                null,
                Topology(list.SourceSiteId, list.SourceWebId),
                null,
                null).Lists.Single();
            Assert.AreEqual(ListMaterializationDisposition.Block, listPlan.Disposition);
            Assert.AreEqual(
                ListFieldMaterializationDisposition.Block,
                listPlan.Fields.Single(value => value.SourceFieldId == FieldId).Disposition);
        }

        [TestMethod]
        public void LegacyV1ListFieldJsonRetainsItsDigestWhenOptionalSourcesAreAbsent()
        {
            const string legacyJson = "{\"id\":\"11111111-1111-1111-1111-111111111111\",\"internalName\":\"Title\",\"title\":\"Title\",\"typeAsString\":\"Text\",\"group\":\"_Hidden\",\"schemaXml\":\"<Field Type=\\\"Text\\\" Name=\\\"Title\\\" />\",\"schemaXmlSha256\":\"schema\",\"portableSchemaSha256\":\"portable\",\"hidden\":false,\"readOnly\":false,\"required\":false,\"fromBaseType\":true,\"sealed\":false,\"sourceLookupWebId\":null,\"sourceLookupListId\":null,\"lookupField\":null,\"taxonomy\":null,\"availability\":\"Captured\",\"diagnostics\":[]}";
            var legacyDigest = MigrationDigest.ComputeSha256(legacyJson);
            var field = MigrationContractSerializer.Deserialize<ListFieldSnapshot>(legacyJson);
            var canonical = MigrationContractSerializer.SerializeCanonical(field);

            Assert.IsNull(field.Sources);
            Assert.AreEqual(legacyJson, canonical);
            Assert.AreEqual(legacyDigest, MigrationDigest.ComputeSha256(canonical));
        }

        private static void AssertFallbackFailure(string schemaXml)
        {
            TaxonomyFieldBindingSnapshot binding;
            string diagnostic;
            Assert.IsFalse(TaxonomyFieldBindingSnapshotReader.TryReadSchemaXml(schemaXml, out binding, out diagnostic));
            Assert.IsNull(binding);
            Assert.IsFalse(string.IsNullOrWhiteSpace(diagnostic));
        }

        private static ServerException MemberFailure()
        {
            return CreateServerException(
                "Specified argument was out of the range of valid values. Parameter name: member",
                "System.ArgumentOutOfRangeException",
                -1);
        }

        private static ServerException CreateServerException(string message, string typeName, int code)
        {
            return (ServerException)Activator.CreateInstance(
                typeof(ServerException),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { message, typeName, code },
                null);
        }

        private static TaxonomyFieldBindingSnapshot ValidBinding()
        {
            return new TaxonomyFieldBindingSnapshot
            {
                SourceTermStoreId = TermStoreId,
                SourceTermSetId = TermSetId,
                AnchorTermId = Guid.Empty,
                HiddenTextFieldId = TextFieldId,
                Open = true
            };
        }

        private static ContentTypeSchemaSnapshot PartialRuntimeSchema(TaxonomyFieldBindingSnapshot binding)
        {
            var field = new FieldSchemaSnapshot
            {
                Id = FieldId,
                InternalName = "Categories",
                TypeAsString = "TaxonomyFieldType",
                SchemaXml = ValidSchemaXml(),
                Role = FieldSchemaRole.DirectBinding,
                Ownership = FieldOwnership.TargetRuntime,
                Taxonomy = binding
            };
            return new ContentTypeSchemaSnapshot
            {
                EvidenceState = ContentTypeSchemaEvidenceState.Partial,
                Availability = EvidenceAvailability.Partial,
                ContentTypeId = "0x010100AABB",
                Name = "Documents",
                ParentContentTypeId = "0x0101",
                RequiredFieldLinks = new[]
                {
                    new ContentTypeFieldLinkSnapshot
                    {
                        FieldId = FieldId,
                        Name = "Categories",
                        Role = FieldSchemaRole.DirectBinding
                    }
                },
                RequiredFieldClosure = new List<FieldSchemaSnapshot> { field }
            };
        }

        private static FieldSchemaSnapshot Companion(Guid id, string typeAsString, bool hidden)
        {
            return new FieldSchemaSnapshot
            {
                Id = id,
                InternalName = "CategoriesTaxHTField0",
                TypeAsString = typeAsString,
                Hidden = hidden,
                SchemaXml = "<Field ID=\"{" + id.ToString("D") + "}\" Name=\"CategoriesTaxHTField0\" Type=\"" + typeAsString + "\" />",
                Role = FieldSchemaRole.Dependency,
                Ownership = FieldOwnership.TargetRuntime
            };
        }

        private static ListDependencySnapshot BogusTaxonomyList()
        {
            var siteId = new Guid("66666666-6666-6666-6666-666666666666");
            var webId = new Guid("77777777-7777-7777-7777-777777777777");
            return new ListDependencySnapshot
            {
                SourceSiteId = siteId,
                SourceWebId = webId,
                SourceWebUrl = "https://source.sharepoint.com/sites/source",
                SourceListId = new Guid("88888888-8888-8888-8888-888888888888"),
                Title = "Bogus taxonomy",
                BaseTemplate = 100,
                BaseType = "GenericList",
                RootFolderServerRelativeUrl = "/sites/source/Lists/BogusTaxonomy",
                Availability = EvidenceAvailability.Captured,
                Fields = new List<ListFieldSnapshot>
                {
                    new ListFieldSnapshot
                    {
                        Id = FieldId,
                        InternalName = "Categories",
                        Title = "Categories",
                        TypeAsString = "TaxonomyFieldTypeBogus",
                        SchemaXml = ValidSchemaXml().Replace("TaxonomyFieldType", "TaxonomyFieldTypeBogus"),
                        Taxonomy = ValidBinding(),
                        Availability = EvidenceAvailability.Captured
                    },
                    new ListFieldSnapshot
                    {
                        Id = TextFieldId,
                        InternalName = "CategoriesTaxHTField0",
                        Title = "CategoriesTaxHTField0",
                        TypeAsString = "Note",
                        Hidden = true,
                        SchemaXml = "<Field Type=\"Note\" Name=\"CategoriesTaxHTField0\" />",
                        Availability = EvidenceAvailability.Captured
                    }
                }
            };
        }

        private static TopologyPlan Topology(Guid siteId, Guid webId)
        {
            var topology = new TopologyPlan
            {
                SiteCollections = new List<SiteCollectionMappingPlan>
                {
                    new SiteCollectionMappingPlan
                    {
                        SourceSiteId = siteId,
                        TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/target",
                        Webs = new List<WebMappingPlan>
                        {
                            new WebMappingPlan
                            {
                                Kind = TopologyNodeKind.SiteCollectionRoot,
                                SourceSiteId = siteId,
                                SourceWebId = webId,
                                SourceServerRelativeUrl = "/sites/source",
                                TargetWebUrl = "https://target.sharepoint.com/sites/target",
                                TargetServerRelativeUrl = "/sites/target"
                            }
                        }
                    }
                }
            };
            topology.PlanDigest = TopologyPlanner.ComputeDigest(topology);
            return topology;
        }

        private static string ValidSchemaXml()
        {
            return "<Field Type=\"TaxonomyFieldType\"><Customization><ArrayOfProperty>"
                + Property("SspId", TermStoreId.ToString("D"))
                + Property("TermSetId", TermSetId.ToString("D"))
                + Property("AnchorId", Guid.Empty.ToString("D"))
                + Property("TextField", TextFieldId.ToString("D"))
                + Property("Open", "true")
                + "</ArrayOfProperty></Customization></Field>";
        }

        private static string Property(string name, string value)
        {
            return "<Property><Name>" + name + "</Name><Value>" + value + "</Value></Property>";
        }

        private sealed class FieldInput
        {
            public Guid Id { get; set; }

            public string Name { get; set; }

            public string SchemaXml { get; set; }

            public TaxonomyFieldBindingSnapshot Binding { get; set; }

            public Exception Failure { get; set; }

            public int ReadCount { get; private set; }

            public TaxonomyFieldBindingSnapshot ReadTyped()
            {
                ReadCount++;
                if (Failure != null)
                {
                    throw Failure;
                }
                return Binding;
            }
        }
    }
}
