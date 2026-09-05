using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Schema.ContentTypes;
using PnP.Framework.Migration.Schema.Fields;
using System;
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
            StringAssert.Contains(string.Join(" ", result.Diagnostics), "Parameter name: member");
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
        }

        [TestMethod]
        public void PerFieldBatchIsolationPreservesOtherTaxonomyBindings()
        {
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
                field => field.ReadTyped());

            Assert.AreEqual(2, results.Count);
            Assert.IsFalse(results[bad.Id].IsComplete);
            Assert.IsNull(results[bad.Id].Binding);
            Assert.IsTrue(results[good.Id].IsComplete);
            Assert.AreEqual(TermSetId, results[good.Id].Binding.SourceTermSetId);
            Assert.AreEqual(1, bad.ReadCount);
            Assert.AreEqual(1, good.ReadCount);
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
                Assert.AreEqual(0, result.Sources.Count);
                StringAssert.Contains(
                    string.Join(" ", result.Diagnostics),
                    "No literal HTTP 401/403 wire evidence was captured");
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
                RequiredFieldClosure = new[] { field }
            };

            Assert.IsFalse(ContentTypeSchemaPlanner.TryCreateTargetRuntimeRequirement(schema, out var plan));
            Assert.IsNull(plan);
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
