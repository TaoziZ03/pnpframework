using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Fields;
using PnP.Framework.Migration.Lists.Packaging;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Assessment;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Fields.Taxonomy;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.Publishing.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging.Taxonomy;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Schema.ContentTypes;
using PnP.Framework.Migration.Schema.ContentTypes.Packaging;
using PnP.Framework.Migration.Schema.Fields;
using PnP.Framework.Migration.Topology;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        private const string SourceWebUrl = "https://source.example/sites/ipkit";

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
        public void PageFieldTypedMemberFailureRecoversBindingFromCapturedSchemaXml()
        {
            var field = PageTaxonomyField(ValidPageSchemaXml());

            Assert.IsTrue(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                field,
                SourceWebUrl,
                () => throw MemberFailure()));
            PageTaxonomyRelationshipProof.Seal(field);

            Assert.AreEqual(PageCaptureStatus.Captured, field.CaptureStatus);
            Assert.IsTrue(PageTaxonomyRelationshipEvidence.HasCompleteFieldBinding(field));
            Assert.AreEqual(TermStoreId, field.TaxonomyBinding.TermStoreId);
            Assert.AreEqual(TermSetId, field.TaxonomyBinding.BoundTermSetId);
            Assert.AreEqual(TextFieldId, field.TaxonomyBinding.TextFieldId);
            Assert.IsTrue(field.TaxonomyBinding.Open);
            Assert.IsTrue(field.Diagnostics.Any(value => value.StartsWith(
                "TaxonomyBindingSchemaXmlFallbackUsed:",
                StringComparison.Ordinal)));

            var legacy = PageTaxonomyField(ValidPageSchemaXml());
            legacy.TaxonomyBinding = ValidRelationshipBinding();
            PageTaxonomyRelationshipProof.Seal(legacy);
            Assert.AreEqual(legacy.TaxonomyValueSetSha256, field.TaxonomyValueSetSha256);
        }

        [TestMethod]
        public void HealthyTypedPageBindingRetainsLegacyCanonicalShapeAndDigest()
        {
            var actual = PageTaxonomyField(ValidPageSchemaXml());
            var expected = PageTaxonomyField(ValidPageSchemaXml());
            expected.TaxonomyBinding = ValidRelationshipBinding();
            PageTaxonomyRelationshipProof.Seal(expected);

            Assert.IsTrue(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                actual,
                SourceWebUrl,
                ValidBinding));
            PageTaxonomyRelationshipProof.Seal(actual);

            Assert.AreEqual(0, actual.Diagnostics.Count);
            var actualCanonical = MigrationContractSerializer.SerializeCanonical(actual);
            var expectedCanonical = MigrationContractSerializer.SerializeCanonical(expected);
            Assert.AreEqual(
                expectedCanonical,
                actualCanonical);
            Assert.AreEqual(
                MigrationDigest.ComputeSha256(expectedCanonical),
                MigrationDigest.ComputeSha256(actualCanonical));
            Assert.IsFalse(actualCanonical.Contains("authorizationEvidence", StringComparison.Ordinal));
        }

        [TestMethod]
        public void IncompletePageBindingBlocksOnlyItsFieldEvenWhenValueIsEmpty()
        {
            var field = PageTaxonomyField("<Field Type=\"TaxonomyFieldTypeMulti\"><Customization /></Field>");
            field.HasValue = false;
            field.Kind = PageFieldValueKind.Null;
            field.RawType = null;
            field.RawValue = null;
            field.RawValueJson = null;

            Assert.IsTrue(PageTaxonomyRelationshipEvidence.IsTaxonomyField(field));

            Assert.IsFalse(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                field,
                SourceWebUrl,
                () => new TaxonomyFieldBindingSnapshot()));
            PageTaxonomyRelationshipProof.Seal(field);

            Assert.AreEqual(PageCaptureStatus.CapturedWithLimitations, field.CaptureStatus);
            Assert.IsFalse(PageTaxonomyRelationshipEvidence.HasCompleteFieldBinding(field));
            Assert.AreEqual(FieldId, field.TaxonomyBinding.FieldId);
            Assert.AreEqual(Guid.Empty, field.TaxonomyBinding.TermStoreId);
            Assert.IsTrue(field.Diagnostics.Any(value => value.StartsWith(
                "TaxonomyBindingSchemaXmlFallbackIncomplete:",
                StringComparison.Ordinal)));

            var snapshot = new PublishingPageCaptureBundle
            {
                Source = SourceIdentity(),
                Fields = new List<PageFieldValueSnapshot> { field }
            };
            var plan = new PublishingPageMigrationPlan
            {
                FieldActions = new List<PageFieldAction>
                {
                    new PageFieldAction
                    {
                        SourceInternalName = field.InternalName,
                        TargetInternalName = field.InternalName,
                        Disposition = PageFieldDisposition.SkipEmpty
                    }
                }
            };
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageTaxonomyPlanValidator.Validate(snapshot, plan));

            plan.FieldActions[0].Disposition = PageFieldDisposition.Block;
            PublishingPageTaxonomyPlanValidator.Validate(snapshot, plan);

            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode
                    {
                        Id = PublishingPageIngredientIds.Field(field.InternalName),
                        Kind = PageIngredientKind.Field,
                        HasContent = true
                    }
                }
            };
            snapshot.IngredientGraph = graph;
            plan.IngredientGraph = graph;
            var mitigationActions = PublishingPageIngredientActionProjector.Project(
                snapshot,
                plan,
                graph);
            Assert.AreEqual(
                IngredientDisposition.Defer,
                mitigationActions.Single(value => value.IngredientId ==
                    PublishingPageIngredientIds.Field(field.InternalName)).Disposition);

            var accumulator = new PublishingPageAssessmentAccumulator(graph);
            PublishingPageCoreAssessmentProjector.Project(
                new PublishingPageAssessmentContext
                {
                    Snapshot = snapshot,
                    WorkflowPolicy = TestWorkflowPolicy(),
                    Options = new PagePlanningOptions(),
                    TargetPageServerRelativeUrl = "/sites/target/Pages/page.aspx"
                },
                accumulator);
            var assessment = accumulator.Complete().Single();
            Assert.AreEqual(PageIngredientAssessmentState.KnownGap, assessment.State);
            Assert.AreEqual(IngredientDisposition.Defer, assessment.ProposedDisposition);
            Assert.AreEqual("TaxonomyFieldBindingUnavailable", assessment.MitigationCode);

            Assert.IsFalse(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                field,
                SourceWebUrl,
                () => throw new HttpRequestException(
                    "forbidden",
                    null,
                    HttpStatusCode.Forbidden)));
            var authorizationActions = PublishingPageIngredientActionProjector.Project(
                snapshot,
                plan,
                graph);
            Assert.AreEqual(
                IngredientDisposition.Block,
                authorizationActions.Single(value => value.IngredientId ==
                    PublishingPageIngredientIds.Field(field.InternalName)).Disposition);
        }

        [TestMethod]
        public void PopulatedUnsupportedTaxonomyValueCannotBecomeSkipEmpty()
        {
            var field = PageTaxonomyField(ValidPageSchemaXml());
            field.Kind = PageFieldValueKind.Unsupported;
            field.CaptureStatus = PageCaptureStatus.CapturedWithLimitations;
            field.RawType = "Sanitized.UnsupportedTaxonomyValue";
            field.RawValue = "retained-recovery-evidence";
            field.RawValueJson = "{\"kind\":\"unsupported\"}";
            Assert.IsTrue(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                field,
                SourceWebUrl,
                ValidBinding));

            var snapshot = new PublishingPageCaptureBundle
            {
                Source = SourceIdentity(),
                Fields = new List<PageFieldValueSnapshot> { field }
            };
            var plan = new PublishingPageMigrationPlan
            {
                FieldActions = new List<PageFieldAction>
                {
                    new PageFieldAction
                    {
                        SourceInternalName = field.InternalName,
                        TargetInternalName = field.InternalName,
                        Disposition = PageFieldDisposition.SkipEmpty
                    }
                }
            };
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageTaxonomyPlanValidator.Validate(snapshot, plan));

            plan.FieldActions[0].Disposition = PageFieldDisposition.Block;
            PublishingPageTaxonomyPlanValidator.Validate(snapshot, plan);

            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = new List<PageIngredientNode>
                {
                    new PageIngredientNode
                    {
                        Id = PublishingPageIngredientIds.Field(field.InternalName),
                        Kind = PageIngredientKind.Field,
                        HasContent = true
                    }
                }
            };
            var accumulator = new PublishingPageAssessmentAccumulator(graph);
            PublishingPageCoreAssessmentProjector.Project(
                new PublishingPageAssessmentContext
                {
                    Snapshot = snapshot,
                    WorkflowPolicy = TestWorkflowPolicy(),
                    Options = new PagePlanningOptions(),
                    TargetPageServerRelativeUrl = "/sites/target/Pages/page.aspx"
                },
                accumulator);
            var assessment = accumulator.Complete().Single();
            Assert.AreEqual(PageIngredientAssessmentState.KnownGap, assessment.State);
            Assert.AreEqual(IngredientDisposition.Defer, assessment.ProposedDisposition);
            Assert.AreEqual("TaxonomyFieldValueCaptureUnsupported", assessment.MitigationCode);
        }

        [DataTestMethod]
        [DataRow(401)]
        [DataRow(403)]
        public void PageBindingRetainsDirectLiteralAuthorizationEvidenceWithoutSchemaFallback(int statusCode)
        {
            var field = PageTaxonomyField(ValidPageSchemaXml());
            var requestUri = PageTaxonomyFieldAuthorizationEvidence.CsomRequestUri(SourceWebUrl);

            Assert.IsFalse(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                field,
                SourceWebUrl,
                () => throw LiteralWebException((HttpStatusCode)statusCode, requestUri)));

            Assert.AreEqual(PageCaptureStatus.CapturedWithLimitations, field.CaptureStatus);
            Assert.IsFalse(PageTaxonomyRelationshipEvidence.HasCompleteFieldBinding(field));
            Assert.AreEqual(statusCode, field.AuthorizationEvidence.LiteralEvidence.HttpStatusCode);
            Assert.AreEqual(
                PageTaxonomyFieldAuthorizationEvidence.SourceBindingCaptureOperation,
                field.AuthorizationEvidence.LiteralEvidence.Operation);
            Assert.AreEqual(requestUri, field.AuthorizationEvidence.LiteralEvidence.RequestUri);
            Assert.AreEqual(
                PublishingPageIngredientIds.Field(field.InternalName),
                field.AuthorizationEvidence.ActionId);
            Assert.IsFalse(field.Diagnostics.Any(value => value.StartsWith(
                "TaxonomyBindingSchemaXmlFallbackUsed:",
                StringComparison.Ordinal)));
            PageTaxonomyFieldAuthorizationEvidence.ValidateSource(SourceIdentity(), field);
        }

        [TestMethod]
        public void PageBindingFindsNestedWebAndHttpRequestAuthorizationEvidence()
        {
            var requestUri = PageTaxonomyFieldAuthorizationEvidence.CsomRequestUri(SourceWebUrl);
            var nested = PageTaxonomyField(ValidPageSchemaXml());
            Assert.IsFalse(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                nested,
                SourceWebUrl,
                () => throw new IOException(
                    "outer transport wrapper",
                    LiteralWebException(HttpStatusCode.Forbidden, requestUri))));
            Assert.AreEqual(403, nested.AuthorizationEvidence.LiteralEvidence.HttpStatusCode);

            var httpRequest = PageTaxonomyField(ValidPageSchemaXml());
            Assert.IsFalse(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                httpRequest,
                SourceWebUrl,
                () => throw new IOException(
                    "outer transport wrapper",
                    new HttpRequestException(
                        "unauthorized",
                        null,
                        HttpStatusCode.Unauthorized))));
            Assert.AreEqual(401, httpRequest.AuthorizationEvidence.LiteralEvidence.HttpStatusCode);
            Assert.AreEqual(requestUri, httpRequest.AuthorizationEvidence.LiteralEvidence.RequestUri);

            var redirectedResponse = PageTaxonomyField(ValidPageSchemaXml());
            Assert.IsFalse(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                redirectedResponse,
                SourceWebUrl,
                () => throw LiteralWebException(
                    HttpStatusCode.Forbidden,
                    "https://login.example/redirected-response")));
            Assert.AreEqual(403, redirectedResponse.AuthorizationEvidence.LiteralEvidence.HttpStatusCode);
            Assert.AreEqual(requestUri, redirectedResponse.AuthorizationEvidence.LiteralEvidence.RequestUri);
            Assert.IsFalse(redirectedResponse.Diagnostics.Any(value => value.StartsWith(
                "TaxonomyBindingSchemaXmlFallbackUsed:",
                StringComparison.Ordinal)));
        }

        [TestMethod]
        public void AuthorizationTextAndNonAuthorizationResponsesStillUseSafeSchemaFallback()
        {
            var textOnly = PageTaxonomyField(ValidPageSchemaXml());
            Assert.IsTrue(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                textOnly,
                SourceWebUrl,
                () => throw CreateServerException(
                    "The payload says HTTP 401 and 403 without a wire response.",
                    "System.UnauthorizedAccessException",
                    -2147024891)));
            Assert.IsNull(textOnly.AuthorizationEvidence);
            Assert.IsTrue(PageTaxonomyRelationshipEvidence.HasCompleteFieldBinding(textOnly));

            var notFound = PageTaxonomyField(ValidPageSchemaXml());
            Assert.IsTrue(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                notFound,
                SourceWebUrl,
                () => throw LiteralWebException(
                    HttpStatusCode.NotFound,
                    PageTaxonomyFieldAuthorizationEvidence.CsomRequestUri(SourceWebUrl))));
            Assert.IsNull(notFound.AuthorizationEvidence);
            Assert.IsTrue(PageTaxonomyRelationshipEvidence.HasCompleteFieldBinding(notFound));

            var transportFailure = PageTaxonomyField(ValidPageSchemaXml());
            Assert.IsTrue(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                transportFailure,
                SourceWebUrl,
                () => throw LiteralWebException(
                    HttpStatusCode.Forbidden,
                    PageTaxonomyFieldAuthorizationEvidence.CsomRequestUri(SourceWebUrl),
                    WebExceptionStatus.ConnectFailure)));
            Assert.IsNull(transportFailure.AuthorizationEvidence);
            Assert.IsTrue(PageTaxonomyRelationshipEvidence.HasCompleteFieldBinding(transportFailure));
        }

        [TestMethod]
        public void LiteralFieldEvidenceBlocksOnlyTheExactFieldIngredient()
        {
            var blocked = PageTaxonomyField(ValidPageSchemaXml());
            Assert.IsFalse(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                blocked,
                SourceWebUrl,
                () => throw new HttpRequestException(
                    "forbidden",
                    null,
                    HttpStatusCode.Forbidden)));
            PageTaxonomyRelationshipProof.Seal(blocked);

            var independent = new PageFieldValueSnapshot
            {
                Id = new Guid("55555555-5555-5555-5555-555555555555"),
                InternalName = "IndependentEmptyField",
                Title = "Independent Empty Field",
                TypeAsString = "Text",
                SchemaXml = "<Field Type=\"Text\" Name=\"IndependentEmptyField\" />",
                HasValue = false,
                Kind = PageFieldValueKind.Null,
                CaptureStatus = PageCaptureStatus.Captured
            };
            var snapshot = new PublishingPageCaptureBundle
            {
                Source = SourceIdentity(),
                Fields = new List<PageFieldValueSnapshot> { blocked, independent }
            };
            var graph = new CanonicalPageIngredientGraph
            {
                Nodes = snapshot.Fields.Select(field => new PageIngredientNode
                {
                    Id = PublishingPageIngredientIds.Field(field.InternalName),
                    Kind = PageIngredientKind.Field,
                    HasContent = true
                }).ToList()
            };
            var accumulator = new PublishingPageAssessmentAccumulator(graph);
            PublishingPageCoreAssessmentProjector.Project(
                new PublishingPageAssessmentContext
                {
                    Snapshot = snapshot,
                    WorkflowPolicy = TestWorkflowPolicy(),
                    Options = new PagePlanningOptions(),
                    TargetPageServerRelativeUrl = "/sites/target/Pages/page.aspx"
                },
                accumulator);
            var assessments = accumulator.Complete();

            PublishingPageAuthorizationEvidenceProjector.Apply(
                assessments,
                PublishingPageSnapshotAuthorizationEvidence.Merge(snapshot, null));

            var blockedAssessment = assessments.Single(value => value.IngredientId ==
                PublishingPageIngredientIds.Field(blocked.InternalName));
            var independentAssessment = assessments.Single(value => value.IngredientId ==
                PublishingPageIngredientIds.Field(independent.InternalName));
            Assert.AreEqual(PageIngredientAssessmentState.AuthorizationBlocked, blockedAssessment.State);
            Assert.AreEqual(IngredientDisposition.Block, blockedAssessment.ProposedDisposition);
            Assert.AreEqual(403, blockedAssessment.AuthorizationEvidence.HttpStatusCode);
            Assert.AreEqual(PageIngredientAssessmentState.Determined, independentAssessment.State);
            Assert.AreEqual(1, assessments.Count(value =>
                value.State == PageIngredientAssessmentState.AuthorizationBlocked));

            var policyEvidence = PublishingPageIngredientAuthorizationPolicy.GetEvidence(snapshot);
            Assert.AreEqual(1, policyEvidence.Count);
            Assert.AreEqual(403, policyEvidence[PublishingPageIngredientIds.Field(blocked.InternalName)].HttpStatusCode);

            var actions = snapshot.Fields.ToDictionary(
                field => PublishingPageIngredientIds.Field(field.InternalName),
                field => new PageIngredientAction
                {
                    IngredientId = PublishingPageIngredientIds.Field(field.InternalName),
                    Capability = IngredientCapability.Available,
                    Disposition = IngredientDisposition.Preserve,
                    Realization = "preserve"
                },
                StringComparer.Ordinal);
            PublishingPageIngredientAuthorizationPolicy.Apply(snapshot, null, actions);
            Assert.AreEqual(
                IngredientDisposition.Block,
                actions[PublishingPageIngredientIds.Field(blocked.InternalName)].Disposition);
            Assert.AreEqual(
                IngredientDisposition.Preserve,
                actions[PublishingPageIngredientIds.Field(independent.InternalName)].Disposition);
        }

        [TestMethod]
        public void FieldAuthorizationEvidenceRejectsForgedScopeAndCompleteBinding()
        {
            var field = PageTaxonomyField(ValidPageSchemaXml());
            Assert.IsFalse(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                field,
                SourceWebUrl,
                () => throw new HttpRequestException(
                    "forbidden",
                    null,
                    HttpStatusCode.Forbidden)));

            var copied = PageTaxonomyField("<Field Type=\"TaxonomyFieldTypeMulti\"><Customization /></Field>");
            copied.Id = new Guid("88888888-8888-8888-8888-888888888888");
            copied.InternalName = "OtherCategories";
            Assert.IsFalse(PageTaxonomyRelationshipSnapshotReader.CaptureFieldBinding(
                copied,
                SourceWebUrl,
                () => new TaxonomyFieldBindingSnapshot()));
            copied.AuthorizationEvidence = field.AuthorizationEvidence;
            Assert.ThrowsException<InvalidDataException>(() =>
                PageTaxonomyFieldAuthorizationEvidence.ValidateSource(SourceIdentity(), copied));

            var wrongRequestUri = "https://source.example/sites/other/_vti_bin/client.svc/ProcessQuery";
            field.AuthorizationEvidence = BoundLiteralHttpAuthorizationEvidence.Create(
                PageTaxonomyFieldAuthorizationEvidence.FieldActionId(field),
                PageTaxonomyFieldAuthorizationEvidence.SourceBindingCaptureOperation,
                wrongRequestUri,
                LiteralHttpAuthorizationEvidence.Create(
                    PageTaxonomyFieldAuthorizationEvidence.SourceBindingCaptureOperation,
                    wrongRequestUri,
                    403,
                    DateTimeOffset.UtcNow));
            Assert.ThrowsException<InvalidDataException>(() =>
                PageTaxonomyFieldAuthorizationEvidence.ValidateSource(SourceIdentity(), field));

            var requestUri = PageTaxonomyFieldAuthorizationEvidence.CsomRequestUri(SourceWebUrl);
            field.AuthorizationEvidence = BoundLiteralHttpAuthorizationEvidence.Create(
                PageTaxonomyFieldAuthorizationEvidence.FieldActionId(field),
                PageTaxonomyFieldAuthorizationEvidence.SourceBindingCaptureOperation,
                requestUri,
                LiteralHttpAuthorizationEvidence.Create(
                    PageTaxonomyFieldAuthorizationEvidence.SourceBindingCaptureOperation,
                    requestUri,
                    403,
                    DateTimeOffset.UtcNow));
            field.TaxonomyBinding = ValidRelationshipBinding();
            Assert.ThrowsException<InvalidDataException>(() =>
                PageTaxonomyFieldAuthorizationEvidence.ValidateSource(SourceIdentity(), field));
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

            var forgedContentType = RuntimeContentTypePlan("TaxonomyFieldTypeBogus");
            var forgedProbe = ExactRuntimeProbe(forgedContentType);
            Assert.ThrowsException<InvalidDataException>(() =>
                ContentTypeSchemaContractValidator.ValidatePlan(forgedContentType));
            var rejectedAdmission = ContentTypeTargetAdmissionEvaluator.Evaluate(forgedContentType, forgedProbe);
            Assert.IsFalse(rejectedAdmission.IsEligible);
            Assert.IsTrue(rejectedAdmission.Issues.Any(value => value.Code == "TaxonomyFieldTypeUnsupported"));

            foreach (var validType in new[] { "TaxonomyFieldType", "TaxonomyFieldTypeMulti" })
            {
                var validPlan = RuntimeContentTypePlan(validType);
                ContentTypeSchemaContractValidator.ValidatePlan(validPlan);
                Assert.IsTrue(ContentTypeTargetAdmissionEvaluator.Evaluate(validPlan, ExactRuntimeProbe(validPlan)).IsEligible);
            }

            var forgedListPlan = ForgedListPlan(list);
            Assert.ThrowsException<InvalidDataException>(() =>
                ListMigrationPlanValidator.Validate(new[] { list }, forgedListPlan));
            var listAdmissionIssues = ListMigrationTargetAnalyzer.GetUnsupportedTaxonomyFieldIssues(
                forgedListPlan.Lists.Single().Fields);
            Assert.AreEqual(1, listAdmissionIssues.Count);
            Assert.AreEqual("TaxonomyFieldTypeUnsupported", listAdmissionIssues[0].Code);
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

        private static PageFieldValueSnapshot PageTaxonomyField(string schemaXml)
        {
            return new PageFieldValueSnapshot
            {
                Id = FieldId,
                InternalName = "Categories",
                Title = "Categories",
                TypeAsString = "TaxonomyFieldTypeMulti",
                SchemaXml = schemaXml,
                HasValue = true,
                Kind = PageFieldValueKind.TaxonomyCollection,
                RawType = "Microsoft.SharePoint.Client.Taxonomy.TaxonomyFieldValueCollection",
                RawValue = "Microsoft.SharePoint.Client.Taxonomy.TaxonomyFieldValueCollection",
                RawValueJson = "[]",
                CaptureStatus = PageCaptureStatus.Captured
            };
        }

        private static PnP.Framework.Migration.Taxonomy.TaxonomyFieldRelationshipBindingSnapshot ValidRelationshipBinding()
        {
            return new PnP.Framework.Migration.Taxonomy.TaxonomyFieldRelationshipBindingSnapshot
            {
                FieldId = FieldId,
                FieldInternalName = "Categories",
                TermStoreId = TermStoreId,
                BoundTermSetId = TermSetId,
                TextFieldId = TextFieldId,
                Open = true
            };
        }

        private static PageIdentity SourceIdentity()
        {
            return new PageIdentity
            {
                SiteId = new Guid("66666666-6666-6666-6666-666666666666"),
                WebId = new Guid("77777777-7777-7777-7777-777777777777"),
                WebUrl = SourceWebUrl,
                WebServerRelativeUrl = "/sites/ipkit",
                PageServerRelativeUrl = "/sites/ipkit/Pages/page.aspx"
            };
        }

        private static PublishingPageWorkflowPolicy TestWorkflowPolicy()
        {
            return new PublishingPageWorkflowPolicy(
                "test-workflow",
                "Test Publishing Page",
                "0x010100",
                "TestLayout.aspx",
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                _ => null);
        }

        private static WebException LiteralWebException(
            HttpStatusCode statusCode,
            string requestUri,
            WebExceptionStatus webExceptionStatus = WebExceptionStatus.ProtocolError)
        {
            var constructor = typeof(HttpWebResponse)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(value =>
                {
                    var parameters = value.GetParameters();
                    return parameters.Length == 3
                        && parameters[0].ParameterType == typeof(HttpResponseMessage)
                        && parameters[1].ParameterType == typeof(Uri)
                        && parameters[2].ParameterType == typeof(CookieContainer);
                });
            var responseMessage = new HttpResponseMessage(statusCode)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri)
            };
            var response = (HttpWebResponse)constructor.Invoke(new object[]
            {
                responseMessage,
                new Uri(requestUri),
                new CookieContainer()
            });
            return new WebException(
                "literal HTTP response",
                null,
                webExceptionStatus,
                response);
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

        private static ContentTypeMaterializationPlan RuntimeContentTypePlan(string taxonomyType)
        {
            var companionId = TextFieldId;
            return new ContentTypeMaterializationPlan
            {
                Disposition = ContentTypeMaterializationDisposition.ReuseOwned,
                ContentTypeId = "0x010100AABB",
                Name = "Documents",
                ParentContentTypeId = "0x0101",
                ParentContentTypeName = "Document",
                RequiredFieldLinks = new List<ContentTypeFieldLinkSnapshot>
                {
                    new ContentTypeFieldLinkSnapshot { FieldId = FieldId, Name = "Categories" }
                },
                Fields = new List<FieldSchemaMaterializationPlan>
                {
                    new FieldSchemaMaterializationPlan
                    {
                        FieldId = companionId,
                        InternalName = "CategoriesTaxHTField0",
                        TypeAsString = "Note",
                        Hidden = true,
                        Role = FieldSchemaRole.Dependency,
                        Disposition = FieldSchemaMaterializationDisposition.RequireTargetRuntime
                    },
                    new FieldSchemaMaterializationPlan
                    {
                        FieldId = FieldId,
                        InternalName = "Categories",
                        TypeAsString = taxonomyType,
                        Role = FieldSchemaRole.DirectBinding,
                        Disposition = FieldSchemaMaterializationDisposition.RequireTargetRuntime,
                        HiddenTextFieldId = companionId
                    }
                }
            };
        }

        private static ContentTypeTargetProbe ExactRuntimeProbe(ContentTypeMaterializationPlan plan)
        {
            return new ContentTypeTargetProbe
            {
                Availability = EvidenceAvailability.Captured,
                ParentContentTypeAvailable = true,
                ResolvedParentContentTypeId = plan.ParentContentTypeId,
                ContentTypeExists = true,
                ExistingName = plan.Name,
                ExistingDescription = plan.Description,
                ExistingGroup = plan.Group,
                ExistingReadOnly = plan.ReadOnly,
                ExistingSealed = plan.Sealed,
                ExistingHidden = plan.Hidden,
                ExistingParentContentTypeId = plan.ParentContentTypeId,
                ExistingFieldLinks = plan.RequiredFieldLinks.Select(value => new ContentTypeFieldLinkTargetProbe
                {
                    FieldId = value.FieldId,
                    Required = value.Required,
                    Hidden = value.Hidden
                }).ToList(),
                Fields = plan.Fields.Select(value => new FieldSchemaTargetProbe
                {
                    FieldId = value.FieldId,
                    Exists = true,
                    InternalName = value.InternalName,
                    TypeAsString = value.TypeAsString
                }).ToList()
            };
        }

        private static ListMigrationPlanSet ForgedListPlan(ListDependencySnapshot source)
        {
            var list = new ListMaterializationPlan
            {
                SourceSiteId = source.SourceSiteId,
                SourceWebId = source.SourceWebId,
                SourceListId = source.SourceListId,
                TargetWebUrl = "https://target.sharepoint.com/sites/target",
                TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/target",
                TargetWebServerRelativeUrl = "/sites/target",
                PreferredTargetRootFolderServerRelativeUrl = "/sites/target/Lists/BogusTaxonomy",
                TargetRootFolderServerRelativeUrl = "/sites/target/Lists/BogusTaxonomy",
                PreferredTargetTitle = source.Title,
                TargetTitle = source.Title,
                OriginalIdentifier = source.SourceListId.ToString("D"),
                Disposition = ListMaterializationDisposition.ReuseOwned,
                TargetProbe = new ListTargetProbe { Disposition = ListMaterializationDisposition.ReuseOwned },
                Fields = new List<ListFieldMaterializationPlan>
                {
                    new ListFieldMaterializationPlan
                    {
                        SourceFieldId = FieldId,
                        InternalName = "Categories",
                        TypeAsString = "TaxonomyFieldTypeBogus",
                        Disposition = ListFieldMaterializationDisposition.RequireTargetRuntime
                    }
                }
            };
            list.PlanDigest = ListMigrationPlanFactory.ComputePlanDigest(list);
            var result = new ListMigrationPlanSet
            {
                OrderedSourceListIds = new List<Guid> { source.SourceListId },
                Lists = new List<ListMaterializationPlan> { list }
            };
            result.PlanDigest = ListMigrationPlanFactory.ComputeSetDigest(result);
            return result;
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

        private static string ValidPageSchemaXml()
        {
            return ValidSchemaXml().Replace(
                "Type=\"TaxonomyFieldType\"",
                "Type=\"TaxonomyFieldTypeMulti\"");
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
