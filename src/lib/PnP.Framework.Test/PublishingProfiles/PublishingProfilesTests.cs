using PnP.Framework.Migration.Schema.ContentTypes;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Content;
using ClientContext = Microsoft.SharePoint.Client.ClientContext;
using FieldUrlValue = Microsoft.SharePoint.Client.FieldUrlValue;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Cohorts;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Lifecycle;
using PnP.Framework.Migration.Pages.Markup;
using PnP.Framework.Migration.Pages.Planning;
using PnP.Framework.Migration.Pages.Profiles;
using PnP.Framework.Migration.Pages.Publishing.Article;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.EnterpriseWiki;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Layouts;
using PnP.Framework.Migration.Pages.Publishing.Lifecycle;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Pages.Publishing.Reporting;
using PnP.Framework.Migration.Pages.Publishing.Verification;
using PnP.Framework.Migration.Pages.Runtime;
using PnP.Framework.Migration.Pages.Security;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PnP.Framework.Test.PublishingProfiles
{
    [TestClass]
    public class PublishingProfilesTests
    {
        [TestMethod]
        public void ArticleCohortPolicyIncludesArticleAndExcludesOtherFamilies()
        {
            var articleCt = BuiltInContentTypeId.ArticlePage + "00AABBCC";
            var ewCt = BuiltInContentTypeId.EnterpriseWikiPage + "001122";
            var welcomeCt = BuiltInContentTypeId.WelcomePage + "003344";
            var projectCt = BuiltInContentTypeId.ProjectPage + "005566";

            var articleAssessment = ArticlePageV1CohortPolicy.Assess(articleCt);
            Assert.AreEqual(ValidationCohortDisposition.Included, articleAssessment.Disposition);
            Assert.AreEqual(ArticlePageV1CohortPolicy.CohortId, articleAssessment.CohortId);
            Assert.IsTrue(ArticlePageV1CohortPolicy.IsIncludedContentType(articleCt));

            var ewAssessment = ArticlePageV1CohortPolicy.Assess(ewCt);
            Assert.AreEqual(ValidationCohortDisposition.Excluded, ewAssessment.Disposition);
            Assert.IsFalse(ArticlePageV1CohortPolicy.IsIncludedContentType(ewCt));

            var welcomeAssessment = ArticlePageV1CohortPolicy.Assess(welcomeCt);
            Assert.AreEqual(ValidationCohortDisposition.Excluded, welcomeAssessment.Disposition);
            Assert.IsFalse(ArticlePageV1CohortPolicy.IsIncludedContentType(welcomeCt));

            var projectAssessment = ArticlePageV1CohortPolicy.Assess(projectCt);
            Assert.AreEqual(ValidationCohortDisposition.Excluded, projectAssessment.Disposition);

            Assert.AreEqual(ValidationCohortDisposition.Unknown, ArticlePageV1CohortPolicy.Assess(null).Disposition);
            Assert.AreEqual(ValidationCohortDisposition.Unknown, ArticlePageV1CohortPolicy.Assess("").Disposition);
            Assert.AreEqual(ValidationCohortDisposition.Excluded, ArticlePageV1CohortPolicy.Assess("0x010100RANDOM").Disposition);
        }

        [TestMethod]
        public void WelcomeCohortPolicyIncludesWelcomeAndExcludesOtherFamilies()
        {
            var welcomeCt = BuiltInContentTypeId.WelcomePage + "00AABBCC";
            var ewCt = BuiltInContentTypeId.EnterpriseWikiPage + "001122";
            var articleCt = BuiltInContentTypeId.ArticlePage + "003344";
            var projectCt = BuiltInContentTypeId.ProjectPage + "005566";

            var welcomeAssessment = WelcomePageV1CohortPolicy.Assess(welcomeCt);
            Assert.AreEqual(ValidationCohortDisposition.Included, welcomeAssessment.Disposition);
            Assert.AreEqual(WelcomePageV1CohortPolicy.CohortId, welcomeAssessment.CohortId);
            Assert.IsTrue(WelcomePageV1CohortPolicy.IsIncludedContentType(welcomeCt));

            var ewAssessment = WelcomePageV1CohortPolicy.Assess(ewCt);
            Assert.AreEqual(ValidationCohortDisposition.Excluded, ewAssessment.Disposition);
            Assert.IsFalse(WelcomePageV1CohortPolicy.IsIncludedContentType(ewCt));

            var articleAssessment = WelcomePageV1CohortPolicy.Assess(articleCt);
            Assert.AreEqual(ValidationCohortDisposition.Excluded, articleAssessment.Disposition);
            Assert.IsFalse(WelcomePageV1CohortPolicy.IsIncludedContentType(articleCt));

            var projectAssessment = WelcomePageV1CohortPolicy.Assess(projectCt);
            Assert.AreEqual(ValidationCohortDisposition.Excluded, projectAssessment.Disposition);

            Assert.AreEqual(ValidationCohortDisposition.Unknown, WelcomePageV1CohortPolicy.Assess(null).Disposition);
            Assert.AreEqual(ValidationCohortDisposition.Unknown, WelcomePageV1CohortPolicy.Assess("").Disposition);
            Assert.AreEqual(ValidationCohortDisposition.Excluded, WelcomePageV1CohortPolicy.Assess("0x010100RANDOM").Disposition);
        }

        [TestMethod]
        public void EnterpriseWikiCohortPolicyDoesNotWidenToArticleOrWelcome()
        {
            var articleCt = BuiltInContentTypeId.ArticlePage + "00AABBCC";
            var welcomeCt = BuiltInContentTypeId.WelcomePage + "003344";
            var ewCt = BuiltInContentTypeId.EnterpriseWikiPage + "001122";

            Assert.IsTrue(EnterpriseWikiV1CohortPolicy.IsIncludedContentType(ewCt));
            Assert.IsFalse(EnterpriseWikiV1CohortPolicy.IsIncludedContentType(articleCt));
            Assert.IsFalse(EnterpriseWikiV1CohortPolicy.IsIncludedContentType(welcomeCt));
            Assert.AreEqual(ValidationCohortDisposition.Excluded, EnterpriseWikiV1CohortPolicy.Assess(articleCt).Disposition);
            Assert.AreEqual(ValidationCohortDisposition.Excluded, EnterpriseWikiV1CohortPolicy.Assess(welcomeCt).Disposition);
        }

        [TestMethod]
        public void ProfileSignalProjectorEmitsArticleAndWelcomeSignals()
        {
            var articleSource = new PageIdentity
            {
                ContentTypeId = BuiltInContentTypeId.ArticlePage + "0011"
            };
            var articleFields = new[]
            {
                new PageFieldValueSnapshot { InternalName = "ArticleByLine" },
                new PageFieldValueSnapshot { InternalName = "PublishingPageImage" }
            };
            var articleLayout = new PublishingPageLayoutSnapshot { FileName = "ArticleLeft.aspx" };

            var articleSignals = PublishingPageProfileSignalProjector.Project(articleSource, articleLayout, articleFields);
            Assert.IsTrue(articleSignals.Any(s => s.ProfileId == PageProfileIds.ArticlePage && s.Kind == PageProfileSignalKind.ContentTypeLineage));
            Assert.IsTrue(articleSignals.Any(s => s.ProfileId == PageProfileIds.ArticlePage && s.Kind == PageProfileSignalKind.Layout && s.Subject == "ArticleLeft.aspx"));
            Assert.IsTrue(articleSignals.Any(s => s.ProfileId == PageProfileIds.ArticlePage && s.Kind == PageProfileSignalKind.Field && s.Subject == "ArticleByLine"));
            Assert.IsTrue(articleSignals.Any(s => s.ProfileId == PageProfileIds.ArticlePage && s.Kind == PageProfileSignalKind.Field && s.Subject == "PublishingPageImage"));

            var welcomeSource = new PageIdentity
            {
                ContentTypeId = BuiltInContentTypeId.WelcomePage + "0022"
            };
            var welcomeFields = new[]
            {
                new PageFieldValueSnapshot { InternalName = "SummaryLinks" },
                new PageFieldValueSnapshot { InternalName = "HeaderStyle" }
            };
            var welcomeLayout = new PublishingPageLayoutSnapshot { FileName = "BlankWebPartPage.aspx" };

            var welcomeSignals = PublishingPageProfileSignalProjector.Project(welcomeSource, welcomeLayout, welcomeFields);
            Assert.IsTrue(welcomeSignals.Any(s => s.ProfileId == PageProfileIds.WelcomePage && s.Kind == PageProfileSignalKind.ContentTypeLineage));
            Assert.IsTrue(welcomeSignals.Any(s => s.ProfileId == PageProfileIds.WelcomePage && s.Kind == PageProfileSignalKind.Layout && s.Subject == "BlankWebPartPage.aspx"));
            Assert.IsTrue(welcomeSignals.Any(s => s.ProfileId == PageProfileIds.WelcomePage && s.Kind == PageProfileSignalKind.Field && s.Subject == "SummaryLinks"));
            Assert.IsTrue(welcomeSignals.Any(s => s.ProfileId == PageProfileIds.WelcomePage && s.Kind == PageProfileSignalKind.Field && s.Subject == "HeaderStyle"));
        }

        [TestMethod]
        public void NativeLayoutCatalogExpandsArticleAndWelcomeLayouts()
        {
            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("ArticleLeft.aspx", out var articleLeft));
            Assert.AreEqual(BuiltInContentTypeId.ArticlePage, articleLeft.AssociatedContentTypeId);
            Assert.AreEqual("Article Page", articleLeft.AssociatedContentTypeName);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("ArticleRight.aspx", out var articleRight));
            Assert.AreEqual(BuiltInContentTypeId.ArticlePage, articleRight.AssociatedContentTypeId);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("ArticleLinks.aspx", out var articleLinks));
            Assert.AreEqual(BuiltInContentTypeId.ArticlePage, articleLinks.AssociatedContentTypeId);

            Assert.AreEqual("Image on left", articleLeft.Title);
            Assert.AreEqual("Image on right", articleRight.Title);
            Assert.AreEqual("Summary links", articleLinks.Title);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("PageFromDocLayout.aspx", out var docPack));
            Assert.AreEqual(BuiltInContentTypeId.ArticlePage, docPack.AssociatedContentTypeId);
            Assert.AreEqual("Body only", docPack.Title);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("BlankWebPartPage.aspx", out var blankWp));
            Assert.AreEqual(BuiltInContentTypeId.WelcomePage, blankWp.AssociatedContentTypeId);
            Assert.AreEqual("Welcome Page", blankWp.AssociatedContentTypeName);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("WelcomeSplash.aspx", out var splash));
            Assert.AreEqual(BuiltInContentTypeId.WelcomePage, splash.AssociatedContentTypeId);
            Assert.AreEqual("Splash", splash.Title);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("WelcomeLinks.aspx", out var welcomeLinks));
            Assert.AreEqual(BuiltInContentTypeId.WelcomePage, welcomeLinks.AssociatedContentTypeId);
            Assert.AreEqual("Summary links", welcomeLinks.Title);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("EnterpriseWiki.aspx", out var ew));
            Assert.AreEqual(BuiltInContentTypeId.EnterpriseWikiPage, ew.AssociatedContentTypeId);
        }

        [TestMethod]
        public void NativeLayoutCatalogSubstitutesUnavailableArticleAndWelcomeLayouts()
        {
            var unavailArticle = new PublishingPageLayoutSnapshot
            {
                Availability = EvidenceAvailability.Unavailable,
                EvidenceState = PublishingPageLayoutEvidenceState.Missing,
                Description = "Image on left"
            };
            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetUnavailableSourceSubstitution(
                unavailArticle,
                "ArticleLeft.aspx",
                out var articleProfile));
            Assert.AreEqual("ArticleLeft.aspx", articleProfile.FileName);
            Assert.AreEqual(BuiltInContentTypeId.ArticlePage, articleProfile.AssociatedContentTypeId);

            var unavailWelcome = new PublishingPageLayoutSnapshot
            {
                Availability = EvidenceAvailability.Unavailable,
                EvidenceState = PublishingPageLayoutEvidenceState.Missing,
                Description = "Blank Web Part page"
            };
            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetUnavailableSourceSubstitution(
                unavailWelcome,
                "BlankWebPartPage.aspx",
                out var welcomeProfile));
            Assert.AreEqual("BlankWebPartPage.aspx", welcomeProfile.FileName);
            Assert.AreEqual(BuiltInContentTypeId.WelcomePage, welcomeProfile.AssociatedContentTypeId);

            // Readable layouts must not be substituted
            var readableLayout = new PublishingPageLayoutSnapshot
            {
                Availability = EvidenceAvailability.Captured,
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                Description = "Image on left"
            };
            Assert.IsFalse(PublishingPageNativeLayoutCatalog.TryGetUnavailableSourceSubstitution(
                readableLayout,
                "ArticleLeft.aspx",
                out _));
        }

        [TestMethod]
        public void PublishingProfileRegistryResolvesAllBuiltInProfiles()
        {
            PublishingPageProfileRegistry.ResetToDefaults();

            // Resolve by workflow ID
            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByWorkflowId("enterprise-wiki-v1", out var ewPolicy));
            Assert.AreEqual(EnterpriseWikiV1WorkflowPolicy.Instance.WorkflowId, ewPolicy.WorkflowId);

            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByWorkflowId("article-page-v1", out var articlePolicy));
            Assert.AreEqual(ArticlePageV1WorkflowPolicy.Instance.WorkflowId, articlePolicy.WorkflowId);

            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByWorkflowId("welcome-page-v1", out var welcomePolicy));
            Assert.AreEqual(WelcomePageV1WorkflowPolicy.Instance.WorkflowId, welcomePolicy.WorkflowId);

            // Resolve by profile ID
            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByProfileId(PageProfileIds.EnterpriseWiki, out var ewByProfile));
            Assert.AreEqual(EnterpriseWikiV1WorkflowPolicy.Instance.WorkflowId, ewByProfile.WorkflowId);

            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByProfileId(PageProfileIds.ArticlePage, out var articleByProfile));
            Assert.AreEqual(ArticlePageV1WorkflowPolicy.Instance.WorkflowId, articleByProfile.WorkflowId);

            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByProfileId(PageProfileIds.WelcomePage, out var welcomeByProfile));
            Assert.AreEqual(WelcomePageV1WorkflowPolicy.Instance.WorkflowId, welcomeByProfile.WorkflowId);

            // Resolve by ContentTypeId
            Assert.IsTrue(PublishingPageProfileRegistry.TryResolvePolicyByContentType(BuiltInContentTypeId.ArticlePage + "0099", out var resolvedArticle));
            Assert.AreEqual(ArticlePageV1WorkflowPolicy.Instance.WorkflowId, resolvedArticle.WorkflowId);

            Assert.IsTrue(PublishingPageProfileRegistry.TryResolvePolicyByContentType(BuiltInContentTypeId.WelcomePage + "0088", out var resolvedWelcome));
            Assert.AreEqual(WelcomePageV1WorkflowPolicy.Instance.WorkflowId, resolvedWelcome.WorkflowId);

            Assert.IsTrue(PublishingPageProfileRegistry.TryResolvePolicyByContentType(BuiltInContentTypeId.EnterpriseWikiPage + "0077", out var resolvedEw));
            Assert.AreEqual(EnterpriseWikiV1WorkflowPolicy.Instance.WorkflowId, resolvedEw.WorkflowId);

            Assert.IsFalse(PublishingPageProfileRegistry.TryResolvePolicyByContentType("0x010100UNKNOWN", out _));

            // Unified ResolvePolicy method
            Assert.AreEqual("article-page-v1", PublishingPageProfileRegistry.ResolvePolicy(workflowId: "article-page-v1").WorkflowId);
            Assert.AreEqual("welcome-page-v1", PublishingPageProfileRegistry.ResolvePolicy(profileId: PageProfileIds.WelcomePage).WorkflowId);
            Assert.AreEqual("enterprise-wiki-v1", PublishingPageProfileRegistry.ResolvePolicy(contentTypeId: BuiltInContentTypeId.EnterpriseWikiPage).WorkflowId);

            // All registered policies count
            Assert.AreEqual(3, PublishingPageProfileRegistry.RegisteredPolicies.Count);
        }

        [TestMethod]
        public void PublishingProfileRegistryFallsBackByContentTypeOnlyWhenWorkflowIsMissing()
        {
            PublishingPageProfileRegistry.ResetToDefaults();

            Assert.AreEqual(
                ArticlePageV1CohortPolicy.CohortId,
                PublishingPageProfileRegistry.ResolvePolicy(
                    workflowId: null,
                    contentTypeId: BuiltInContentTypeId.ArticlePage + "0011").WorkflowId);
            Assert.ThrowsException<InvalidOperationException>(() =>
                PublishingPageProfileRegistry.ResolvePolicy(
                    workflowId: "missing-workflow",
                    contentTypeId: BuiltInContentTypeId.ArticlePage));
        }

        [TestMethod]
        public void PublishingProfileRegistrySnapshotsInputsAndUsesDeterministicContentTypeTieBreak()
        {
            var stock = new[] { "Custom.aspx" };
            var policyZ = new PublishingPageWorkflowPolicy(
                "z-workflow", "Article Page", BuiltInContentTypeId.ArticlePage, "Custom.aspx",
                stock, new[] { "Title" }, new[] { "SummaryLinks" }, ArticlePageV1CohortPolicy.Assess);
            var policyA = new PublishingPageWorkflowPolicy(
                "a-workflow", "Article Page", BuiltInContentTypeId.ArticlePage, "Custom.aspx",
                stock, new[] { "Title" }, new[] { "SummaryLinks" }, ArticlePageV1CohortPolicy.Assess);
            stock[0] = "Mutated.aspx";

            try
            {
                PublishingPageProfileRegistry.Register(policyZ, "z-profile", BuiltInContentTypeId.ArticlePage);
                PublishingPageProfileRegistry.Register(policyA, "a-profile", BuiltInContentTypeId.ArticlePage);

                Assert.IsTrue(policyZ.IsStockLayout("Custom.aspx"));
                Assert.IsFalse(policyZ.IsStockLayout("Mutated.aspx"));
                Assert.IsTrue(PublishingPageProfileRegistry.TryResolvePolicyByContentType(
                    BuiltInContentTypeId.ArticlePage + "00AB", out var resolved));
                Assert.AreEqual("a-workflow", resolved.WorkflowId);
            }
            finally
            {
                PublishingPageProfileRegistry.ResetToDefaults();
            }
        }

        [TestMethod]
        public void PublishingProfileRegistryAtomicallyReplacesWorkflowAndProfileAliases()
        {
            var first = new PublishingPageWorkflowPolicy(
                "replace-workflow", "Article Page", BuiltInContentTypeId.ArticlePage, "First.aspx",
                new[] { "First.aspx" }, new[] { "Title" }, new[] { "Comments" }, ArticlePageV1CohortPolicy.Assess);
            var sameWorkflowNewProfile = new PublishingPageWorkflowPolicy(
                "replace-workflow", "Article Page", BuiltInContentTypeId.ArticlePage, "Second.aspx",
                new[] { "Second.aspx" }, new[] { "Title" }, new[] { "Comments" }, ArticlePageV1CohortPolicy.Assess);
            var sameProfileNewWorkflow = new PublishingPageWorkflowPolicy(
                "replacement-workflow", "Article Page", BuiltInContentTypeId.ArticlePage, "Third.aspx",
                new[] { "Third.aspx" }, new[] { "Title" }, new[] { "Comments" }, ArticlePageV1CohortPolicy.Assess);

            try
            {
                PublishingPageProfileRegistry.Register(first, "old-profile", "0x010100AA");
                PublishingPageProfileRegistry.Register(sameWorkflowNewProfile, "new-profile", "0x010100BB");
                Assert.IsFalse(PublishingPageProfileRegistry.TryGetPolicyByProfileId("old-profile", out _));
                Assert.AreEqual("Second.aspx", PublishingPageProfileRegistry.ResolvePolicy(workflowId: "replace-workflow").PreferredTargetPageLayoutFileName);
                Assert.AreEqual("replace-workflow", PublishingPageProfileRegistry.ResolvePolicy(profileId: "new-profile").WorkflowId);
                Assert.IsFalse(PublishingPageProfileRegistry.TryResolvePolicyByContentType("0x010100AA01", out _));

                PublishingPageProfileRegistry.Register(sameProfileNewWorkflow, "new-profile", "0x010100CC");
                Assert.IsFalse(PublishingPageProfileRegistry.TryGetPolicyByWorkflowId("replace-workflow", out _));
                Assert.AreEqual("replacement-workflow", PublishingPageProfileRegistry.ResolvePolicy(profileId: "new-profile").WorkflowId);
                Assert.AreEqual("replacement-workflow", PublishingPageProfileRegistry.ResolvePolicy(contentTypeId: "0x010100CC01").WorkflowId);
                Assert.IsFalse(PublishingPageProfileRegistry.TryResolvePolicyByContentType("0x010100BB01", out _));
            }
            finally
            {
                PublishingPageProfileRegistry.ResetToDefaults();
            }
        }

        [TestMethod]
        public void WorkflowPoliciesDefineProfileSpecificFieldOwnership()
        {
            var article = ArticlePageV1WorkflowPolicy.Instance;
            Assert.AreEqual("article-page-v1", article.WorkflowId);
            Assert.AreEqual("ArticleLeft.aspx", article.PreferredTargetPageLayoutFileName);
            Assert.IsTrue(article.FieldsHandledByPageWriter.Contains("ContentTypeId"));
            Assert.IsTrue(article.FieldsHandledByPageWriter.Contains("PublishingPageContent"));
            Assert.IsTrue(article.FieldsHandledByPageWriter.Contains("PublishingPageLayout"));
            Assert.IsTrue(article.RecognizedPageFields.Contains("ArticleByLine"));
            Assert.IsTrue(article.RecognizedPageFields.Contains("PublishingPageImage"));
            Assert.IsTrue(article.RecognizedPageFields.Contains("PublishingStartDate"));
            Assert.IsTrue(article.RecognizedPageFields.Contains("PublishingExpirationDate"));
            Assert.IsTrue(article.RecognizedPageFields.Contains("SummaryLinks"));
            Assert.IsFalse(article.RecognizedPageFields.Contains("Wiki_x0020_Page_x0020_Categories"));

            var welcome = WelcomePageV1WorkflowPolicy.Instance;
            Assert.AreEqual("welcome-page-v1", welcome.WorkflowId);
            Assert.AreEqual("BlankWebPartPage.aspx", welcome.PreferredTargetPageLayoutFileName);
            Assert.IsTrue(welcome.FieldsHandledByPageWriter.Contains("ContentTypeId"));
            Assert.IsTrue(welcome.RecognizedPageFields.Contains("SummaryLinks"));
            Assert.IsTrue(welcome.RecognizedPageFields.Contains("SummaryLinks2"));
            Assert.IsTrue(welcome.RecognizedPageFields.Contains("HeaderStyle"));
            Assert.IsTrue(welcome.RecognizedPageFields.Contains("PublishingRollupImage"));
            Assert.IsFalse(welcome.RecognizedPageFields.Contains("ArticleByLine"));
            Assert.IsFalse(welcome.RecognizedPageFields.Contains("Wiki_x0020_Page_x0020_Categories"));
            Assert.IsTrue(EnterpriseWikiV1WorkflowPolicy.Instance.RecognizedPageFields.Contains("Wiki_x0020_Page_x0020_Categories"));
        }

        [TestMethod]
        public void DiscoveryClassesFilterRespectiveContentTypes()
        {
            Assert.IsTrue(ArticlePageDiscovery.IsArticlePageContentType(BuiltInContentTypeId.ArticlePage + "001122"));
            Assert.IsFalse(ArticlePageDiscovery.IsArticlePageContentType(BuiltInContentTypeId.WelcomePage + "001122"));
            Assert.IsFalse(ArticlePageDiscovery.IsArticlePageContentType(BuiltInContentTypeId.EnterpriseWikiPage + "001122"));

            Assert.IsTrue(PnP.Framework.Migration.Pages.Publishing.Welcome.WelcomePageDiscovery.IsWelcomePageContentType(BuiltInContentTypeId.WelcomePage + "001122"));
            Assert.IsFalse(PnP.Framework.Migration.Pages.Publishing.Welcome.WelcomePageDiscovery.IsWelcomePageContentType(BuiltInContentTypeId.ArticlePage + "001122"));
            Assert.IsFalse(PnP.Framework.Migration.Pages.Publishing.Welcome.WelcomePageDiscovery.IsWelcomePageContentType(BuiltInContentTypeId.EnterpriseWikiPage + "001122"));

            Assert.IsTrue(EnterpriseWikiPageDiscovery.IsEnterpriseWikiContentType(BuiltInContentTypeId.EnterpriseWikiPage + "001122"));
            Assert.IsFalse(EnterpriseWikiPageDiscovery.IsEnterpriseWikiContentType(BuiltInContentTypeId.ArticlePage + "001122"));
            Assert.IsFalse(EnterpriseWikiPageDiscovery.IsEnterpriseWikiContentType(BuiltInContentTypeId.WelcomePage + "001122"));
        }

        [TestMethod]
        public void PackageFileStoresDeclareDistinctDefaultFileNames()
        {
            Assert.AreEqual("article-page-package.json", ArticlePagePackageFileStore.DefaultPackageFileName);
            Assert.AreEqual("article-page-export.json", ArticlePagePackageFileStore.DefaultExportFileName);
            Assert.AreEqual("article-page-report.md", ArticlePagePackageFileStore.DefaultReportFileName);

            Assert.AreEqual("welcome-page-package.json", PnP.Framework.Migration.Pages.Publishing.Welcome.WelcomePagePackageFileStore.DefaultPackageFileName);
            Assert.AreEqual("welcome-page-export.json", PnP.Framework.Migration.Pages.Publishing.Welcome.WelcomePagePackageFileStore.DefaultExportFileName);
            Assert.AreEqual("welcome-page-report.md", PnP.Framework.Migration.Pages.Publishing.Welcome.WelcomePagePackageFileStore.DefaultReportFileName);

            Assert.AreEqual("enterprise-wiki-package.json", EnterpriseWikiPackageFileStore.DefaultPackageFileName);
        }

        [TestMethod]
        public void ImportPlanValidatorRejectsWorkflowPolicyMismatch()
        {
            var package = CreatePackage(BuiltInContentTypeId.ArticlePage, ArticlePageV1WorkflowPolicy.Instance);
            var scope = PublishingPageExecutionScope.Create(package);

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageImportPlanValidator.Validate(
                    package,
                    EnterpriseWikiV1WorkflowPolicy.Instance,
                    scope));

            PublishingPageImportPlanValidator.Validate(
                package,
                ArticlePageV1WorkflowPolicy.Instance,
                scope);
        }

        [TestMethod]
        public void ImportPlanValidatorRejectsWelcomePolicyMismatch()
        {
            var package = CreatePackage(BuiltInContentTypeId.WelcomePage, WelcomePageV1WorkflowPolicy.Instance);
            var scope = PublishingPageExecutionScope.Create(package);

            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPageImportPlanValidator.Validate(
                    package,
                    ArticlePageV1WorkflowPolicy.Instance,
                    scope));

            PublishingPageImportPlanValidator.Validate(
                package,
                WelcomePageV1WorkflowPolicy.Instance,
                scope);
        }

        private static PublishingPageMigrationPackage CreatePackage(string contentTypeId, PublishingPageWorkflowPolicy policy)
        {
            var fileUniqueId = Guid.NewGuid();
            var modifiedUtc = new DateTime(2026, 8, 26, 1, 2, 3, DateTimeKind.Utc);
            var pageBytes = Encoding.UTF8.GetBytes("<%@ Page Language=\"C#\" Inherits=\"Microsoft.SharePoint.Publishing.TemplateRedirectionPage, Microsoft.SharePoint.Publishing\" %>");
            var snapshot = new PublishingPageCaptureBundle
            {
                CapturePolicy = new PageCaptureOptions
                {
                    SourcePageServerRelativeUrl = "/sites/source/Pages/source.aspx"
                },
                Source = new PageIdentity
                {
                    SiteId = Guid.NewGuid(),
                    WebId = Guid.NewGuid(),
                    WebUrl = "https://source.sharepoint.com/sites/source",
                    WebServerRelativeUrl = "/sites/source",
                    PageServerRelativeUrl = "/sites/source/Pages/source.aspx",
                    FileUniqueId = fileUniqueId,
                    ContentTypeId = contentTypeId,
                    ContentTypeName = "Test Page",
                    Title = "Source",
                    Length = pageBytes.LongLength,
                    ModifiedUtc = modifiedUtc,
                    VersionLabel = "0.1"
                },
                PageArtifact = new PageArtifactSnapshot
                {
                    FileUniqueId = fileUniqueId,
                    ServerRelativeUrl = "/sites/source/Pages/source.aspx",
                    Bytes = MigrationArtifact.Describe(pageBytes, "application/vnd.ms-aspx", "source.aspx"),
                    ContentBase64 = Convert.ToBase64String(pageBytes),
                    PageDirective = PageDirectiveParser.Parse(Encoding.UTF8.GetString(pageBytes)),
                    Availability = EvidenceAvailability.Captured
                },
                Layout = new PublishingPageLayoutSnapshot
                {
                    Url = "https://source.sharepoint.com/_catalogs/masterpage/" + policy.PreferredTargetPageLayoutFileName,
                    ServerRelativeUrl = "/_catalogs/masterpage/" + policy.PreferredTargetPageLayoutFileName,
                    FileName = policy.PreferredTargetPageLayoutFileName,
                    CustomizedPageStatus = 1,
                    AssociatedContentTypeName = policy.SourceContentTypeName,
                    AssociatedContentTypeId = contentTypeId,
                    PageDirective = PageDirectiveParser.Parse("<%@ Page %>"),
                    Bytes = MigrationArtifact.Describe(Encoding.UTF8.GetBytes("<%@ Page %>"), "application/vnd.ms-aspx", policy.PreferredTargetPageLayoutFileName),
                    ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<%@ Page %>")),
                    Availability = EvidenceAvailability.Captured,
                    EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                    Controls = new List<PublishingPageLayoutControl>
                    {
                        new PublishingPageLayoutControl
                        {
                            TagPrefix = "PublishingWebControls",
                            ControlName = "RichHtmlField",
                            FieldName = "PublishingPageContent"
                        }
                    }
                },
                PublishingPageContent = "<p>source</p>",
                PublishingPageContentSha256 = PublishingPageDigest.ComputeSha256("<p>source</p>"),
                Fields = new List<PageFieldValueSnapshot>
                {
                    new PageFieldValueSnapshot
                    {
                        Id = Guid.NewGuid(),
                        InternalName = "Title",
                        Title = "Title",
                        TypeAsString = "Text",
                        SchemaXml = "<Field Name='Title' Type='Text' />",
                        HasValue = true,
                        Kind = PageFieldValueKind.String,
                        StringValues = new List<string> { "Test Page" },
                        CaptureStatus = PageCaptureStatus.Captured
                    }
                },
                Security = new PageSecuritySnapshot(),
                Lifecycle = new PageLifecycleSnapshot
                {
                    CheckOutType = "None",
                    Level = "Published",
                    ModerationStatus = 0
                },
                SourceFence = new SourcePageFence
                {
                    FileUniqueId = fileUniqueId,
                    VersionLabel = "0.1",
                    Length = pageBytes.LongLength,
                    ModifiedUtc = modifiedUtc
                }
            };
            snapshot.Runtime = PageRuntimeResolver.Resolve(snapshot.PageArtifact, snapshot.Layout.PageDirective, snapshot.Source.ContentTypeId);
            snapshot.Runtime = PageRuntimeResolver.Resolve(snapshot.PageArtifact, snapshot.Layout.PageDirective, snapshot.Source.ContentTypeId);
            snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(snapshot.Source, snapshot.Layout, snapshot.Fields);
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);

            var snapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot);
            var layoutPlan = PublishingPageLayoutPlanFactory.Create(
                snapshot.Layout,
                new Uri(snapshot.Source.WebUrl),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                policy);
            var layoutProbe = new PublishingPageLayoutTargetProbe
            {
                TargetServerRelativeUrl = layoutPlan.TargetServerRelativeUrl,
                FileExists = true,
                ExistingAssociatedContentTypeName = policy.SourceContentTypeName,
                ExistingAssociatedContentTypeId = policy.SourceContentTypeIdPrefix,
                AssociatedContentTypeAvailable = true,
                ResolvedAssociatedContentTypeId = policy.SourceContentTypeIdPrefix,
                Availability = EvidenceAvailability.Captured
            };
            var layoutAdmission = PublishingPageLayoutTargetAdmissionEvaluator.Evaluate(layoutPlan, layoutProbe);
            var plan = new PublishingPageMigrationPlan
            {
                SourceSnapshotDigest = snapshotDigest,
                SourceWebUrl = snapshot.Source.WebUrl,
                SourcePageServerRelativeUrl = snapshot.Source.PageServerRelativeUrl,
                OriginalIdentifier = PublishingPageTargetOwnership.OriginalIdentifier(snapshot.Source),
                TargetWebUrl = "https://target.sharepoint.com/sites/target",
                TargetWebServerRelativeUrl = "/sites/target",
                PreferredTargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx",
                TargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx",
                PageLayoutName = Path.GetFileNameWithoutExtension(policy.PreferredTargetPageLayoutFileName),
                TargetLifecycle = PublishingPageTargetLifecycle.Published,
                LifecycleReason = "The source file level is 'Published', so the target will remain Published.",
                PlanningPolicy = new PagePlanningOptions
                {
                    TargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx"
                },
                TargetProbe = new PublishingPageTargetSnapshot
                {
                    WebUrl = "https://target.sharepoint.com/sites/target",
                    WebServerRelativeUrl = "/sites/target",
                    PagesLibraryServerRelativeUrl = "/sites/target/Pages",
                    PagesLibraryBaseTemplate = 850,
                    PageContentTypeId = contentTypeId,
                    PageLayoutUrl = "https://target.sharepoint.com/_catalogs/masterpage/" + policy.PreferredTargetPageLayoutFileName,
                    PageLayoutExists = true,
                    PreferredTargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx",
                    TargetPageServerRelativeUrl = "/sites/target/Pages/source.aspx"
                },
                LayoutMaterialization = layoutPlan,
                LayoutTargetProbe = layoutProbe,
                LayoutAdmission = layoutAdmission,
                FieldActions = new List<PageFieldAction>
                {
                    new PageFieldAction
                    {
                        SourceInternalName = "Title",
                        TargetInternalName = "Title",
                        Disposition = PageFieldDisposition.AlreadyHandled,
                        Reason = "The field is handled by page writer."
                    }
                },
                ExpectedPublishingPageContentSha256 = snapshot.PublishingPageContentSha256,
                RuntimeVerification = new RuntimeVerificationManifest
                {
                    Requirements = new List<RuntimeVerificationRequirement>
                    {
                        new RuntimeVerificationRequirement
                        {
                            Id = "authored-dom-equality",
                            Kind = RuntimeVerificationRequirementKind.AuthoredDomEquality,
                            Required = true,
                            Description = "Normalized authored DOM is equal."
                        }
                    }
                },
                IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot)
            };
            plan.IngredientActions = PublishingPageIngredientActionProjector.Project(snapshot, plan, plan.IngredientGraph);
            var ingredientEvaluation = PageIngredientPlanEvaluator.Evaluate(plan.IngredientGraph, plan.IngredientActions);
            plan.MigrationOutcome = ingredientEvaluation.Outcome;
            plan.IngredientIssues = ingredientEvaluation.Issues;
            plan.ExecutionFrontier = ingredientEvaluation.ExecutionFrontier;

            var selection = policy.Select(contentTypeId);
            return new PublishingPageMigrationPackage
            {
                PlannedAtUtc = DateTimeOffset.UtcNow,
                ExportedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                State = PublishingPagePackageState.ApprovalReady,
                Selection = selection,
                SelectionDigest = PublishingPageDigest.ComputeSelectionDigest(selection),
                Snapshot = snapshot,
                Plan = plan,
                SnapshotDigest = snapshotDigest,
                PlanDigest = PublishingPageDigest.ComputePlanDigest(plan),
                Report = new PublishingPageMigrationReport
                {
                    Summary = "Test report"
                }
            };
        }

        [TestMethod]
        public void LayoutPlannerReusesTargetStockForReadableArticleRightLayout()
        {
            var layout = new PublishingPageLayoutSnapshot
            {
                Url = "https://source.sharepoint.com/_catalogs/masterpage/ArticleRight.aspx",
                ServerRelativeUrl = "/_catalogs/masterpage/ArticleRight.aspx",
                FileName = "ArticleRight.aspx",
                Availability = EvidenceAvailability.Captured,
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                CustomizedPageStatus = 1,
                AssociatedContentTypeName = "Article Page",
                AssociatedContentTypeId = BuiltInContentTypeId.ArticlePage,
                Bytes = MigrationArtifact.Describe(Encoding.UTF8.GetBytes("<%@ Page %>"), "application/vnd.ms-aspx", "ArticleRight.aspx")
            };

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                ArticlePageV1WorkflowPolicy.Instance);

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.ReuseTargetStock, plan.Disposition);
            Assert.AreEqual("ArticleRight.aspx", plan.TargetFileName);
            Assert.AreEqual("ArticleRight", plan.TargetPageLayoutName);
            Assert.AreEqual("/sites/target/_catalogs/masterpage/ArticleRight.aspx", plan.TargetServerRelativeUrl);
            Assert.AreEqual(BuiltInContentTypeId.ArticlePage, plan.AssociatedContentTypeId);
        }

        [TestMethod]
        public void LayoutPlannerReusesTargetStockForReadableArticleLinksLayout()
        {
            var layout = new PublishingPageLayoutSnapshot
            {
                Url = "https://source.sharepoint.com/_catalogs/masterpage/ArticleLinks.aspx",
                ServerRelativeUrl = "/_catalogs/masterpage/ArticleLinks.aspx",
                FileName = "ArticleLinks.aspx",
                Availability = EvidenceAvailability.Captured,
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                CustomizedPageStatus = 1,
                AssociatedContentTypeName = "Article Page",
                AssociatedContentTypeId = BuiltInContentTypeId.ArticlePage,
                Bytes = MigrationArtifact.Describe(Encoding.UTF8.GetBytes("<%@ Page %>"), "application/vnd.ms-aspx", "ArticleLinks.aspx")
            };

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                ArticlePageV1WorkflowPolicy.Instance);

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.ReuseTargetStock, plan.Disposition);
            Assert.AreEqual("ArticleLinks.aspx", plan.TargetFileName);
            Assert.AreEqual("ArticleLinks", plan.TargetPageLayoutName);
        }

        [TestMethod]
        public void LayoutPlannerReusesTargetStockForReadableWelcomeSplashLayout()
        {
            var layout = new PublishingPageLayoutSnapshot
            {
                Url = "https://source.sharepoint.com/_catalogs/masterpage/WelcomeSplash.aspx",
                ServerRelativeUrl = "/_catalogs/masterpage/WelcomeSplash.aspx",
                FileName = "WelcomeSplash.aspx",
                Availability = EvidenceAvailability.Captured,
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                CustomizedPageStatus = 1,
                AssociatedContentTypeName = "Welcome Page",
                AssociatedContentTypeId = BuiltInContentTypeId.WelcomePage,
                Bytes = MigrationArtifact.Describe(Encoding.UTF8.GetBytes("<%@ Page %>"), "application/vnd.ms-aspx", "WelcomeSplash.aspx")
            };

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                WelcomePageV1WorkflowPolicy.Instance);

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.ReuseTargetStock, plan.Disposition);
            Assert.AreEqual("WelcomeSplash.aspx", plan.TargetFileName);
            Assert.AreEqual("WelcomeSplash", plan.TargetPageLayoutName);
            Assert.AreEqual("/sites/target/_catalogs/masterpage/WelcomeSplash.aspx", plan.TargetServerRelativeUrl);
            Assert.AreEqual(BuiltInContentTypeId.WelcomePage, plan.AssociatedContentTypeId);
        }

        [TestMethod]
        public void LayoutPlannerReusesTargetStockForReadableWelcomeLinksLayout()
        {
            var layout = new PublishingPageLayoutSnapshot
            {
                Url = "https://source.sharepoint.com/_catalogs/masterpage/WelcomeLinks.aspx",
                ServerRelativeUrl = "/_catalogs/masterpage/WelcomeLinks.aspx",
                FileName = "WelcomeLinks.aspx",
                Availability = EvidenceAvailability.Captured,
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                CustomizedPageStatus = 1,
                AssociatedContentTypeName = "Welcome Page",
                AssociatedContentTypeId = BuiltInContentTypeId.WelcomePage,
                Bytes = MigrationArtifact.Describe(Encoding.UTF8.GetBytes("<%@ Page %>"), "application/vnd.ms-aspx", "WelcomeLinks.aspx")
            };

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                WelcomePageV1WorkflowPolicy.Instance);

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.ReuseTargetStock, plan.Disposition);
            Assert.AreEqual("WelcomeLinks.aspx", plan.TargetFileName);
        }

        [TestMethod]
        public void LayoutPlannerFailsClosedForCrossProfileOrMissingStockAssociation()
        {
            var crossProfile = new PublishingPageLayoutSnapshot
            {
                Url = "https://source.sharepoint.com/_catalogs/masterpage/WelcomeSplash.aspx",
                ServerRelativeUrl = "/_catalogs/masterpage/WelcomeSplash.aspx",
                FileName = "WelcomeSplash.aspx",
                Availability = EvidenceAvailability.Captured,
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                CustomizedPageStatus = 1,
                AssociatedContentTypeName = "Welcome Page",
                AssociatedContentTypeId = BuiltInContentTypeId.WelcomePage,
                Bytes = MigrationArtifact.Describe(Encoding.UTF8.GetBytes("<%@ Page %>"), "application/vnd.ms-aspx", "WelcomeSplash.aspx")
            };
            var missingAssociation = new PublishingPageLayoutSnapshot
            {
                Url = "https://source.sharepoint.com/_catalogs/masterpage/ArticleLeft.aspx",
                ServerRelativeUrl = "/_catalogs/masterpage/ArticleLeft.aspx",
                FileName = "ArticleLeft.aspx",
                Availability = EvidenceAvailability.Captured,
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                CustomizedPageStatus = 1,
                Bytes = MigrationArtifact.Describe(Encoding.UTF8.GetBytes("<%@ Page %>"), "application/vnd.ms-aspx", "ArticleLeft.aspx")
            };

            Assert.AreEqual(
                PublishingPageLayoutMaterializationDisposition.Block,
                PublishingPageLayoutPlanFactory.Create(
                    crossProfile,
                    new Uri("https://source.sharepoint.com/sites/source"),
                    new Uri("https://target.sharepoint.com/sites/target"),
                    new Uri("https://target.sharepoint.com/sites/target"),
                    ArticlePageV1WorkflowPolicy.Instance).Disposition);
            Assert.AreEqual(
                PublishingPageLayoutMaterializationDisposition.Block,
                PublishingPageLayoutPlanFactory.Create(
                    missingAssociation,
                    new Uri("https://source.sharepoint.com/sites/source"),
                    new Uri("https://target.sharepoint.com/sites/target"),
                    new Uri("https://target.sharepoint.com/sites/target"),
                    ArticlePageV1WorkflowPolicy.Instance).Disposition);
        }

        [TestMethod]
        public void LayoutPlannerUsesRealUnavailableMetadataWithinExactWorkflowFamily()
        {
            var unavailable = new PublishingPageLayoutSnapshot
            {
                Url = "https://source.sharepoint.com/_catalogs/masterpage/PageFromDocLayout.aspx",
                ServerRelativeUrl = "/_catalogs/masterpage/PageFromDocLayout.aspx",
                FileName = "PageFromDocLayout.aspx",
                Description = "Body only",
                Availability = EvidenceAvailability.Unavailable,
                EvidenceState = PublishingPageLayoutEvidenceState.Missing
            };

            var article = PublishingPageLayoutPlanFactory.Create(
                unavailable,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                ArticlePageV1WorkflowPolicy.Instance);
            var welcome = PublishingPageLayoutPlanFactory.Create(
                unavailable,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                WelcomePageV1WorkflowPolicy.Instance);

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.ReuseTargetStock, article.Disposition);
            Assert.AreEqual("PageFromDocLayout.aspx", article.TargetFileName);
            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.Block, welcome.Disposition);
        }

        [TestMethod]
        public void LayoutPlannerCreatesOwnedForCustomArticleLayout()
        {
            var layout = new PublishingPageLayoutSnapshot
            {
                Url = "https://source.sharepoint.com/_catalogs/masterpage/CustomArticleLayout.aspx",
                ServerRelativeUrl = "/_catalogs/masterpage/CustomArticleLayout.aspx",
                FileName = "CustomArticleLayout.aspx",
                Availability = EvidenceAvailability.Captured,
                EvidenceState = PublishingPageLayoutEvidenceState.Readable,
                CustomizedPageStatus = 1,
                AssociatedContentTypeName = "Article Page",
                AssociatedContentTypeId = BuiltInContentTypeId.ArticlePage,
                Bytes = MigrationArtifact.Describe(Encoding.UTF8.GetBytes("<%@ Page %>"), "application/vnd.ms-aspx", "CustomArticleLayout.aspx"),
                ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<%@ Page %>")),
                AssociatedContentTypeSchema = new ContentTypeSchemaSnapshot
                {
                    EvidenceState = ContentTypeSchemaEvidenceState.Readable,
                    Availability = EvidenceAvailability.Captured,
                    SourceWebUrl = "https://source.sharepoint.com/sites/source",
                    ContentTypeId = BuiltInContentTypeId.ArticlePage,
                    Name = "Article Page",
                    Description = "Article Page schema",
                    Group = "Page Layout Content Types",
                    ParentContentTypeId = BuiltInContentTypeId.Page,
                    ParentContentTypeName = "Page"
                }
            };

            var plan = PublishingPageLayoutPlanFactory.Create(
                layout,
                new Uri("https://source.sharepoint.com/sites/source"),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                ArticlePageV1WorkflowPolicy.Instance);

            Assert.AreEqual(PublishingPageLayoutMaterializationDisposition.CreateOwned, plan.Disposition);
            Assert.IsTrue(plan.TargetFileName.StartsWith("pnp-customarticlelayout-", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void ArticlePageExtractsAndRewritesPublishingPageImageReference()
        {
            var sourceIdentity = new PageIdentity
            {
                WebUrl = "https://source.sharepoint.com/sites/source",
                WebServerRelativeUrl = "/sites/source",
                PageServerRelativeUrl = "/sites/source/Pages/article.aspx"
            };

            var fields = new List<PageFieldValueSnapshot>
            {
                new PageFieldValueSnapshot
                {
                    InternalName = "PublishingPageImage",
                    Kind = PageFieldValueKind.String,
                    HasValue = true,
                    Value = "<img src=\"https://source.sharepoint.com/sites/source/PublishingImages/hero.jpg\" alt=\"Hero\" />"
                }
            };

            var references = PageReferenceSnapshotReader.Read(
                null,
                sourceIdentity,
                null,
                null,
                null,
                new PageCaptureOptions { SourcePageServerRelativeUrl = "/sites/source/Pages/article.aspx" },
                new List<string>(),
                fields);

            Assert.AreEqual(1, references.Count);
            Assert.AreEqual("https://source.sharepoint.com/sites/source/PublishingImages/hero.jpg", references[0].OriginalValue);
            Assert.AreEqual(PageReferenceKind.Image, references[0].Kind);
            Assert.AreEqual(PageCaptureStatus.CapturedWithLimitations, references[0].CaptureStatus);
            Assert.IsNull(references[0].ContentSha256);

            var replacements = new List<PageTextReplacement>
            {
                new PageTextReplacement
                {
                    Source = "https://source.sharepoint.com/sites/source/PublishingImages/hero.jpg",
                    Target = "https://target.sharepoint.com/sites/target/PublishingImages/hero.jpg"
                }
            };

            var rewritten = PageTextTransformer.Rewrite(fields[0].Value, replacements);
            Assert.IsTrue(rewritten.Contains("https://target.sharepoint.com/sites/target/PublishingImages/hero.jpg"));
            Assert.IsFalse(rewritten.Contains("https://source.sharepoint.com/sites/source/PublishingImages/hero.jpg"));
        }

        [TestMethod]
        public void WelcomePageExtractsAndRewritesSummaryLinksReference()
        {
            var sourceIdentity = new PageIdentity
            {
                WebUrl = "https://source.sharepoint.com/sites/source",
                WebServerRelativeUrl = "/sites/source",
                PageServerRelativeUrl = "/sites/source/Pages/welcome.aspx"
            };

            var fields = new List<PageFieldValueSnapshot>
            {
                new PageFieldValueSnapshot
                {
                    InternalName = "SummaryLinks",
                    Kind = PageFieldValueKind.String,
                    HasValue = true,
                    Value = "<div class=\"slm-layout-main\"><a href=\"/sites/source/Pages/about.aspx\">About</a></div>"
                }
            };

            var references = PageReferenceSnapshotReader.Read(
                null,
                sourceIdentity,
                null,
                null,
                null,
                new PageCaptureOptions { SourcePageServerRelativeUrl = "/sites/source/Pages/welcome.aspx" },
                new List<string>(),
                fields);

            Assert.AreEqual(1, references.Count);
            Assert.AreEqual("/sites/source/Pages/about.aspx", references[0].OriginalValue);

            var replacements = new List<PageTextReplacement>
            {
                new PageTextReplacement
                {
                    Source = "/sites/source/Pages/about.aspx",
                    Target = "/sites/target/Pages/about.aspx"
                }
            };

            var rewritten = PageTextTransformer.Rewrite(fields[0].Value, replacements);
            Assert.IsTrue(rewritten.Contains("/sites/target/Pages/about.aspx"));
            Assert.IsFalse(rewritten.Contains("/sites/source/Pages/about.aspx"));
        }

        [TestMethod]
        public void ReferenceReaderCapturesUrlFieldCandidatesWithoutClaimingMissingPayloadEvidence()
        {
            var references = PageReferenceSnapshotReader.Read(
                null,
                new PageIdentity
                {
                    WebId = Guid.NewGuid(),
                    WebUrl = "https://source.sharepoint.com/sites/source",
                    WebServerRelativeUrl = "/sites/source",
                    PageServerRelativeUrl = "/sites/source/Pages/article.aspx"
                },
                null,
                null,
                null,
                new PageCaptureOptions { SourcePageServerRelativeUrl = "/sites/source/Pages/article.aspx" },
                new List<string>(),
                new[]
                {
                    new PageFieldValueSnapshot
                    {
                        InternalName = "PublishingRollupImage",
                        Kind = PageFieldValueKind.Url,
                        UrlValue = new PageUrlValueSnapshot
                        {
                            Url = "https://source.sharepoint.com/sites/source/PublishingImages/rollup.jpg",
                            Description = "Rollup"
                        }
                    }
                });

            Assert.AreEqual(1, references.Count);
            Assert.AreEqual("field:PublishingRollupImage", references[0].Consumer);
            Assert.IsTrue(references[0].IsRenderableResource);
            Assert.AreEqual(PageCaptureStatus.CapturedWithLimitations, references[0].CaptureStatus);
            Assert.IsNull(references[0].ContentBase64);
        }

        [TestMethod]
        public void ReferenceReaderClosesAllReplayableStringAndUrlFieldsIncludingComments()
        {
            var source = new PageIdentity
            {
                WebId = Guid.NewGuid(),
                WebUrl = "https://source.sharepoint.com/sites/source",
                WebServerRelativeUrl = "/sites/source",
                PageServerRelativeUrl = "/sites/source/Pages/article.aspx"
            };
            var references = PageReferenceSnapshotReader.Read(
                null,
                source,
                null,
                null,
                null,
                new PageCaptureOptions { SourcePageServerRelativeUrl = source.PageServerRelativeUrl },
                new List<string>(),
                new[]
                {
                    new PageFieldValueSnapshot
                    {
                        InternalName = "Comments",
                        Kind = PageFieldValueKind.String,
                        Value = "<p><img src=\"/sites/source/SiteAssets/comment.png\" /></p>"
                    },
                    new PageFieldValueSnapshot
                    {
                        InternalName = "ContosoReplayMarkup",
                        Kind = PageFieldValueKind.String,
                        Value = "<a href=\"/sites/source/Pages/custom.aspx\">Custom</a>"
                    },
                    new PageFieldValueSnapshot
                    {
                        InternalName = "ContosoReplayUrl",
                        Kind = PageFieldValueKind.Url,
                        UrlValue = new PageUrlValueSnapshot
                        {
                            Url = "https://source.sharepoint.com/sites/source/Pages/url.aspx",
                            Description = "URL"
                        }
                    }
                });

            Assert.AreEqual(3, references.Count);
            var commentResource = references.Single(value => value.OriginalValue.EndsWith("comment.png", StringComparison.Ordinal));
            Assert.IsTrue(commentResource.IsRenderableResource);
            Assert.AreEqual(PageCaptureStatus.CapturedWithLimitations, commentResource.CaptureStatus);
            Assert.IsTrue(references.Any(value => value.OriginalValue.EndsWith("custom.aspx", StringComparison.Ordinal)));
            Assert.IsTrue(references.Any(value => value.Consumer == "field:ContosoReplayUrl"));

            var actions = PageReferencePlanner.BuildActions(
                source,
                references,
                "https://target.sharepoint.com/sites/target",
                "/sites/target",
                new PnP.Framework.Migration.Topology.SiteCollectionMappingPlan
                {
                    SourceSiteCollectionUrl = "https://source.sharepoint.com/sites/source",
                    TargetSiteCollectionUrl = "https://target.sharepoint.com/sites/target"
                },
                new PagePlanningOptions { AllowExternalResourceReferences = true },
                new List<string>());
            Assert.AreEqual(PageReferenceDisposition.PreserveExternal, actions.Single(value => value.SnapshotDependencyId == commentResource.Id).Disposition);
            Assert.AreEqual(2, actions.Count(value => value.Disposition == PageReferenceDisposition.RewriteToTarget));
        }

        [TestMethod]
        public void ExecutionReplacementProjectorRejectsVacuousOrIncompleteActionFrontiers()
        {
            var package = CreatePackage(BuiltInContentTypeId.ArticlePage, ArticlePageV1WorkflowPolicy.Instance);
            package.Plan.Replacements = new List<PageTextReplacement>
            {
                new PageTextReplacement
                {
                    Source = package.Snapshot.Source.WebUrl,
                    Target = package.Plan.TargetWebUrl
                }
            };
            var dependencyId = "iframe-dependency";
            package.Snapshot.Dependencies.Add(new PageReferenceSnapshot
            {
                Id = dependencyId,
                OriginalValue = "https://source.sharepoint.com/sites/source/Pages/embedded.aspx",
                SourceAbsoluteUrl = "https://source.sharepoint.com/sites/source/Pages/embedded.aspx",
                Kind = PageReferenceKind.IFrame,
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.CapturedWithLimitations
            });
            package.Plan.ExecutionFrontier = new PageIngredientExecutionFrontier
            {
                Decisions = new List<PageIngredientExecutionDecision>
                {
                    new PageIngredientExecutionDecision
                    {
                        IngredientId = PublishingPageIngredientIds.Reference(dependencyId),
                        State = PageIngredientExecutionState.Executable
                    }
                }
            };

            package.Plan.DependencyActions = new List<PageReferenceAction>
            {
                new PageReferenceAction
                {
                    SnapshotDependencyId = dependencyId,
                    Disposition = PageReferenceDisposition.Delegate
                }
            };
            var delegatedScope = PublishingPageExecutionScope.Create(package);
            Assert.AreEqual(0, PublishingPageExecutionReplacementProjector.Project(package, delegatedScope).Count);

            package.Plan.DependencyActions[0].Disposition = PageReferenceDisposition.Block;
            var blockedScope = PublishingPageExecutionScope.Create(package);
            Assert.AreEqual(0, PublishingPageExecutionReplacementProjector.Project(package, blockedScope).Count);

            package.Plan.DependencyActions.Clear();
            package.Plan.ExecutionFrontier.Decisions.Clear();
            var emptyScope = PublishingPageExecutionScope.Create(package);
            Assert.AreEqual(0, PublishingPageExecutionReplacementProjector.Project(package, emptyScope).Count);
        }

        [TestMethod]
        public void FreshFieldAndLayoutReadbackVerifiesRewrittenValuesAndRejectsDrift()
        {
            using (var context = new ClientContext("https://target.sharepoint.com/sites/target"))
            {
                var item = context.Web.Lists.GetByTitle("Pages").GetItemById(1);
                item["SummaryLinks"] = "<a href=\"/sites/target/Pages/about.aspx\">About</a>";
                item["PublishingPageLayout"] = new FieldUrlValue
                {
                    Url = "https://target.sharepoint.com/sites/target/_catalogs/masterpage/ArticleLinks.aspx",
                    Description = "Summary links"
                };
                item["PublishingRollupImage"] = new FieldUrlValue
                {
                    Url = "https://target.sharepoint.com/sites/target/PublishingImages/rollup.jpg",
                    Description = "Rollup"
                };
                var field = new PageFieldValueSnapshot
                {
                    InternalName = "SummaryLinks",
                    Kind = PageFieldValueKind.String,
                    Value = "<a href=\"/sites/source/Pages/about.aspx\">About</a>"
                };
                var action = new PageFieldAction
                {
                    SourceInternalName = "SummaryLinks",
                    TargetInternalName = "SummaryLinks",
                    Disposition = PageFieldDisposition.Apply
                };
                var urlField = new PageFieldValueSnapshot
                {
                    InternalName = "PublishingRollupImage",
                    Kind = PageFieldValueKind.Url,
                    UrlValue = new PageUrlValueSnapshot
                    {
                        Url = "https://source.sharepoint.com/sites/source/PublishingImages/rollup.jpg",
                        Description = "Rollup"
                    }
                };
                var urlAction = new PageFieldAction
                {
                    SourceInternalName = "PublishingRollupImage",
                    TargetInternalName = "PublishingRollupImage",
                    Disposition = PageFieldDisposition.Apply
                };
                var replacements = new[]
                {
                    new PageTextReplacement
                    {
                        Source = "/sites/source/Pages/about.aspx",
                        Target = "/sites/target/Pages/about.aspx"
                    },
                    new PageTextReplacement
                    {
                        Source = "https://source.sharepoint.com/sites/source/PublishingImages/rollup.jpg",
                        Target = "https://target.sharepoint.com/sites/target/PublishingImages/rollup.jpg"
                    }
                };

                var passed = PublishingPageFieldFreshReadbackVerifier.Verify(
                    item, new[] { field, urlField }, new[] { action, urlAction }, replacements, Array.Empty<PageFieldImportResult>());
                Assert.IsTrue(passed.All(value => value.Succeeded));
                Assert.IsTrue(PublishingPageFieldFreshReadbackVerifier.LayoutMatches(
                    item, "/sites/target/_catalogs/masterpage/ArticleLinks.aspx"));

                item["SummaryLinks"] = "<a href=\"/sites/target/Pages/drift.aspx\">Drift</a>";
                var drifted = PublishingPageFieldFreshReadbackVerifier.Verify(
                    item, new[] { field, urlField }, new[] { action, urlAction }, replacements, Array.Empty<PageFieldImportResult>());
                Assert.IsFalse(drifted.Single(value => value.InternalName == "SummaryLinks").Succeeded);
                Assert.IsFalse(PublishingPageFieldFreshReadbackVerifier.LayoutMatches(
                    item, "/sites/target/_catalogs/masterpage/ArticleRight.aspx"));
            }
        }

        [TestMethod]
        public void PackageLifecycleProjectsCapturedFieldReferencesAndDropsUnsafeRewriteWithoutPayloadEvidence()
        {
            var package = CreatePackage(BuiltInContentTypeId.ArticlePage, ArticlePageV1WorkflowPolicy.Instance);
            var field = new PageFieldValueSnapshot
            {
                Id = Guid.NewGuid(),
                InternalName = "PublishingPageImage",
                TypeAsString = "Image",
                Kind = PageFieldValueKind.String,
                HasValue = true,
                CaptureStatus = PageCaptureStatus.Captured,
                Value = "<img src=\"https://source.sharepoint.com/sites/source/PublishingImages/guide.jpg\" alt=\"Guide\" />"
            };
            var dependency = PageReferenceSnapshotReader.Read(
                null,
                package.Snapshot.Source,
                null,
                null,
                null,
                package.Snapshot.CapturePolicy,
                new List<string>(),
                new[] { field }).Single();
            dependency.CaptureStatus = PageCaptureStatus.Captured;
            dependency.ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("guide"));
            dependency.ContentSha256 = PublishingPageDigest.ComputeSha256(Encoding.UTF8.GetBytes("guide"));
            dependency.ContentLength = 5;
            package.Snapshot.Fields.Add(field);
            package.Snapshot.Dependencies.Add(dependency);
            package.Plan.FieldActions.Add(new PageFieldAction
            {
                SourceInternalName = "PublishingPageImage",
                TargetInternalName = "PublishingPageImage",
                TargetTypeAsString = "Image",
                Disposition = PageFieldDisposition.Apply
            });
            package.Plan.DependencyActions = new List<PageReferenceAction>
            {
                new PageReferenceAction
                {
                    SnapshotDependencyId = dependency.Id,
                    Disposition = PageReferenceDisposition.MaterializeAtTarget,
                    TargetServerRelativeUrl = "/sites/target/PublishingImages/guide.jpg",
                    TargetAbsoluteUrl = "https://target.sharepoint.com/sites/target/PublishingImages/guide.jpg"
                }
            };
            package.Plan.TargetProbe.EnableModeration = true;
            package.Plan.TargetProbe.ReferenceVerifications = new List<PageReferenceVerificationResult>
            {
                PageReferenceVerification.InspectPlan(
                    dependency,
                    package.Plan.DependencyActions.Single(),
                    null,
                    new Uri(package.Plan.TargetWebUrl))
            };
            package.Plan.RuntimeVerification.Requirements.Clear();
            package.Plan.Replacements = PageReferencePlanner.BuildTextReplacements(
                package.Snapshot.Source,
                package.Plan.TargetWebUrl,
                package.Plan.TargetWebServerRelativeUrl,
                package.Snapshot.Dependencies,
                package.Plan.DependencyActions);
            ResealPackage(package);

            var scope = PublishingPageExecutionScope.Create(package);
            var replacements = PublishingPageExecutionReplacementProjector.Project(package, scope);
            using (var context = new ClientContext(package.Plan.TargetWebUrl))
            {
                var item = context.Web.Lists.GetByTitle("Pages").GetItemById(1);
                item["PublishingPageImage"] = PageTextTransformer.Rewrite(field.Value, replacements);
                item["PublishingPageContent"] = PageTextTransformer.Rewrite(package.Snapshot.PublishingPageContent, replacements);
                item["ContentTypeId"] = package.Plan.TargetProbe.PageContentTypeId;
                item["PublishingPageLayout"] = new FieldUrlValue
                {
                    Url = "https://target.sharepoint.com" + package.Plan.LayoutMaterialization.TargetServerRelativeUrl,
                    Description = "Image on left"
                };
                var verified = PublishingPageFieldFreshReadbackVerifier.Verify(
                    item,
                    package.Snapshot.Fields,
                    scope.PageFieldActions(package),
                    replacements,
                    Array.Empty<PageFieldImportResult>());
                Assert.AreEqual(1, verified.Count, string.Join(", ", package.Plan.IngredientActions.Select(value => value.IngredientId + "=" + value.Disposition)));
                Assert.IsTrue(verified.Single().Succeeded);
                StringAssert.Contains((string)item["PublishingPageImage"], "https://target.sharepoint.com/sites/target/PublishingImages/guide.jpg");

                var storage = new PublishingPageTargetStorageState
                {
                    Exists = true,
                    FileUniqueId = Guid.NewGuid(),
                    ListItemId = 42,
                    VersionLabel = "1.0",
                    Level = Microsoft.SharePoint.Client.FileLevel.Published,
                    CheckOutType = Microsoft.SharePoint.Client.CheckOutType.None,
                    PagesLibraryModerationEnabled = true,
                    Fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PublishingPageImage"] = item["PublishingPageImage"],
                        ["PublishingPageContent"] = item["PublishingPageContent"],
                        ["PublishingPageLayout"] = item["PublishingPageLayout"],
                        ["ContentTypeId"] = item["ContentTypeId"],
                        ["_ModerationStatus"] = 0
                    },
                    Properties = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        [PublishingPageTargetOwnership.OriginalIdentifierPropertyName] = package.Plan.OriginalIdentifier,
                        [PublishingPageTargetOwnership.SourceSnapshotDigestPropertyName] = package.SnapshotDigest,
                        [PublishingPageTargetOwnership.PlanDigestPropertyName] = package.PlanDigest
                    }
                };
                var referenceReadback = new PageReferenceTargetReadState
                {
                    Exists = true,
                    HttpStatusCode = 200,
                    MediaType = "image/jpeg",
                    ContentLength = dependency.ContentLength,
                    ContentSha256 = dependency.ContentSha256
                };
                var executionSeam = new PublishingPageImportExecutionSeam
                {
                    TargetWebUrl = package.Plan.TargetWebUrl,
                    ReadTargetPage = () => storage,
                    ReadTargetReference = (_, __) => referenceReadback
                };
                var resumed = new PublishingPageMigrationImporter().ImportWithExecutionSeam(
                    context,
                    package,
                    package.PlanDigest,
                    executionSeam,
                    ArticlePageV1WorkflowPolicy.Instance);
                Assert.IsTrue(resumed.MutationStarted);
                Assert.IsTrue(resumed.FreshReadbackPassed);
                Assert.IsTrue(resumed.PageFieldsMatched);
                Assert.IsTrue(resumed.LayoutMatched);
                Assert.IsTrue(resumed.StorageContentEqual);
                Assert.IsTrue(resumed.ContentTypeMatched);
                Assert.IsTrue(resumed.LifecycleMatched);
                Assert.AreEqual("Published", resumed.ActualFileLevel);
                Assert.AreEqual("None", resumed.ActualCheckOutType);
                Assert.AreEqual(0, resumed.ActualModerationStatus);
                Assert.AreEqual(MigrationAcceptanceStatus.Accepted, resumed.AcceptanceStatus);
                Assert.IsTrue(resumed.Steps.Any(value => value.ActionId == "admission.target-storage-session"));
                Assert.IsTrue(resumed.Steps.Any(value => value.ActionId == "topology.controlled-session"));
                Assert.IsTrue(resumed.Steps.Any(value => value.ActionId == "page.create" && value.Outcome == MutationOutcome.AlreadySatisfied));

                var sealedReferencePreflight = package.Plan.TargetProbe.ReferenceVerifications.Single();
                package.Plan.TargetProbe.ReferenceVerifications = null;
                package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);
                Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateMigration(package));
                package.Plan.TargetProbe.ReferenceVerifications = new List<PageReferenceVerificationResult>();
                package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);
                Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateMigration(package));
                package.Plan.TargetProbe.ReferenceVerifications = new List<PageReferenceVerificationResult>
                {
                    sealedReferencePreflight,
                    sealedReferencePreflight
                };
                package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);
                Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateMigration(package));
                package.Plan.TargetProbe.ReferenceVerifications = new List<PageReferenceVerificationResult>
                {
                    sealedReferencePreflight
                };
                sealedReferencePreflight.TargetMatched = false;
                package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);
                Assert.ThrowsException<InvalidDataException>(() => PublishingPagePackageValidator.ValidateMigration(package));
                sealedReferencePreflight.TargetMatched = true;
                package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);

                referenceReadback.Exists = false;
                referenceReadback.HttpStatusCode = 404;
                var missingDependency = new PublishingPageMigrationImporter().ImportWithExecutionSeam(
                    context,
                    package,
                    package.PlanDigest,
                    executionSeam,
                    ArticlePageV1WorkflowPolicy.Instance);
                Assert.IsFalse(missingDependency.FreshReadbackPassed);
                Assert.IsFalse(missingDependency.DependenciesMatched);
                Assert.IsTrue(missingDependency.ReferenceVerifications.Single().TargetMatched == false);
                Assert.AreEqual(1, missingDependency.MaterializedDependencyCount);
                referenceReadback.HttpStatusCode = 403;
                referenceReadback.EvidenceComplete = false;
                var accessDeniedDependency = new PublishingPageMigrationImporter().ImportWithExecutionSeam(
                    context,
                    package,
                    package.PlanDigest,
                    executionSeam,
                    ArticlePageV1WorkflowPolicy.Instance);
                Assert.IsFalse(accessDeniedDependency.FreshReadbackPassed);
                Assert.AreEqual(MigrationAcceptanceStatus.Rejected, accessDeniedDependency.AcceptanceStatus);
                Assert.AreEqual(0, accessDeniedDependency.AuthorizationBlockedIngredientCount);
                referenceReadback.Exists = true;
                referenceReadback.HttpStatusCode = 200;
                referenceReadback.EvidenceComplete = true;

                storage.Fields["PublishingPageImage"] = "<img src=\"https://target.sharepoint.com/sites/target/PublishingImages/drift.jpg\" />";
                var fieldDrift = new PublishingPageMigrationImporter().ImportWithExecutionSeam(
                    context,
                    package,
                    package.PlanDigest,
                    executionSeam,
                    ArticlePageV1WorkflowPolicy.Instance);
                Assert.IsFalse(fieldDrift.FreshReadbackPassed);
                Assert.IsFalse(fieldDrift.PageFieldsMatched);
                Assert.AreEqual(MigrationAcceptanceStatus.Rejected, fieldDrift.AcceptanceStatus);

                storage.Fields["PublishingPageImage"] = item["PublishingPageImage"];
                storage.Fields["PublishingPageLayout"] = new FieldUrlValue
                {
                    Url = "https://target.sharepoint.com/sites/target/_catalogs/masterpage/ArticleRight.aspx",
                    Description = "Image on right"
                };
                var layoutDrift = new PublishingPageMigrationImporter().ImportWithExecutionSeam(
                    context, package, package.PlanDigest, executionSeam, ArticlePageV1WorkflowPolicy.Instance);
                Assert.IsFalse(layoutDrift.FreshReadbackPassed);
                Assert.IsFalse(layoutDrift.LayoutMatched);

                storage.Fields["PublishingPageLayout"] = item["PublishingPageLayout"];
                storage.CheckOutType = Microsoft.SharePoint.Client.CheckOutType.Online;
                var checkoutDrift = new PublishingPageMigrationImporter().ImportWithExecutionSeam(
                    context, package, package.PlanDigest, executionSeam, ArticlePageV1WorkflowPolicy.Instance);
                Assert.IsFalse(checkoutDrift.FreshReadbackPassed);
                Assert.IsFalse(checkoutDrift.LifecycleMatched);

                storage.CheckOutType = Microsoft.SharePoint.Client.CheckOutType.None;
                storage.Fields["_ModerationStatus"] = 2;
                var moderationDrift = new PublishingPageMigrationImporter().ImportWithExecutionSeam(
                    context, package, package.PlanDigest, executionSeam, ArticlePageV1WorkflowPolicy.Instance);
                Assert.IsFalse(moderationDrift.FreshReadbackPassed);
                Assert.IsFalse(moderationDrift.LifecycleMatched);
                Assert.AreEqual(2, moderationDrift.ActualModerationStatus);

                storage.Fields["_ModerationStatus"] = 0;
                storage.PagesLibraryModerationEnabled = false;
                var moderationContractDrift = new PublishingPageMigrationImporter().ImportWithExecutionSeam(
                    context, package, package.PlanDigest, executionSeam, ArticlePageV1WorkflowPolicy.Instance);
                Assert.IsFalse(moderationContractDrift.FreshReadbackPassed);
                Assert.IsFalse(moderationContractDrift.LifecycleMatched);

                storage.PagesLibraryModerationEnabled = true;
                storage.Level = Microsoft.SharePoint.Client.FileLevel.Draft;
                var lifecycleDrift = new PublishingPageMigrationImporter().ImportWithExecutionSeam(
                    context, package, package.PlanDigest, executionSeam, ArticlePageV1WorkflowPolicy.Instance);
                Assert.IsFalse(lifecycleDrift.FreshReadbackPassed);
                Assert.IsFalse(lifecycleDrift.LifecycleMatched);
            }

            dependency.CaptureStatus = PageCaptureStatus.CapturedWithLimitations;
            dependency.ContentBase64 = null;
            dependency.ContentSha256 = null;
            package.Plan.DependencyActions[0].Disposition = PageReferenceDisposition.PreserveExternal;
            package.Plan.DependencyActions[0].TargetServerRelativeUrl = null;
            package.Plan.DependencyActions[0].TargetAbsoluteUrl = dependency.SourceAbsoluteUrl;
            var withoutEvidence = PublishingPageExecutionReplacementProjector.Project(package, scope);
            Assert.IsFalse(withoutEvidence.Any(value =>
                string.Equals(value.Source, package.Snapshot.Source.WebUrl, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value.Source, package.Snapshot.Source.WebServerRelativeUrl, StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void LifecycleVerifierAuditsPublishedAndDraftModerationContracts()
        {
            Assert.IsTrue(PublishingPageLifecycleVerifier.Verify(
                PublishingPageTargetLifecycle.Published,
                true,
                Microsoft.SharePoint.Client.FileLevel.Published,
                Microsoft.SharePoint.Client.CheckOutType.None,
                0).Matched);
            Assert.IsFalse(PublishingPageLifecycleVerifier.Verify(
                PublishingPageTargetLifecycle.Published,
                true,
                Microsoft.SharePoint.Client.FileLevel.Published,
                Microsoft.SharePoint.Client.CheckOutType.Online,
                0).Matched);
            Assert.IsFalse(PublishingPageLifecycleVerifier.Verify(
                PublishingPageTargetLifecycle.Published,
                true,
                Microsoft.SharePoint.Client.FileLevel.Published,
                Microsoft.SharePoint.Client.CheckOutType.None,
                null).Matched);
            Assert.IsTrue(PublishingPageLifecycleVerifier.Verify(
                PublishingPageTargetLifecycle.Draft,
                true,
                Microsoft.SharePoint.Client.FileLevel.Draft,
                Microsoft.SharePoint.Client.CheckOutType.None,
                3).Matched);
            Assert.IsFalse(PublishingPageLifecycleVerifier.Verify(
                PublishingPageTargetLifecycle.Draft,
                true,
                Microsoft.SharePoint.Client.FileLevel.Draft,
                Microsoft.SharePoint.Client.CheckOutType.None,
                0).Matched);
            Assert.IsTrue(PublishingPageLifecycleVerifier.Verify(
                PublishingPageTargetLifecycle.Draft,
                false,
                Microsoft.SharePoint.Client.FileLevel.Draft,
                Microsoft.SharePoint.Client.CheckOutType.None,
                null).Matched);
            Assert.IsFalse(PublishingPageLifecycleVerifier.Verify(
                PublishingPageTargetLifecycle.Draft,
                null,
                Microsoft.SharePoint.Client.FileLevel.Draft,
                Microsoft.SharePoint.Client.CheckOutType.None,
                null).Matched);
        }

        [TestMethod]
        public void TargetReference403BlocksOnlyItsIngredientAndRejectsTamperedEvidence()
        {
            var package = CreatePackage(BuiltInContentTypeId.ArticlePage, ArticlePageV1WorkflowPolicy.Instance);
            var dependency = new PageReferenceSnapshot
            {
                Id = "runtime-script",
                OriginalValue = "/_layouts/15/init.js",
                SourceAbsoluteUrl = "https://source.sharepoint.com/_layouts/15/init.js",
                SourceServerRelativeUrl = "/_layouts/15/init.js",
                Consumer = "field:PublishingPageContent",
                Kind = PageReferenceKind.Script,
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.CapturedWithLimitations
            };
            var action = new PageReferenceAction
            {
                SnapshotDependencyId = dependency.Id,
                Disposition = PageReferenceDisposition.RewriteToTarget,
                TargetServerRelativeUrl = "/_layouts/15/init.js",
                TargetAbsoluteUrl = "https://target.sharepoint.com/_layouts/15/init.js"
            };
            var targetRead = new PageReferenceTargetReadState
            {
                Exists = false,
                HttpStatusCode = 403,
                EvidenceComplete = false,
                AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create(
                    PageReferenceAuthorizationEvidence.TargetHttpProbeOperation,
                    PageReferenceAuthorizationEvidence.HttpRequestUri(
                        package.Plan.TargetWebUrl,
                        action.TargetServerRelativeUrl),
                    403,
                    DateTimeOffset.Parse("2026-09-05T00:00:00Z"))
            };
            package.Snapshot.Dependencies.Add(dependency);
            package.Plan.DependencyActions.Add(action);
            var verification = PageReferenceVerification.InspectPlan(
                dependency,
                action,
                (_, __) => targetRead,
                new Uri(package.Plan.TargetWebUrl));
            package.Plan.TargetProbe.ReferenceVerifications.Add(verification);

            ResealPackage(package);

            PublishingPagePackageValidator.ValidateMigration(package);
            Assert.AreEqual(PageMigrationOutcome.PartiallyExecutable, package.Plan.MigrationOutcome);
            Assert.AreEqual(
                PageIngredientExecutionState.AuthorizationBlocked,
                package.Plan.ExecutionFrontier.GetState(PublishingPageIngredientIds.Reference(dependency.Id)));
            Assert.AreEqual(
                PageIngredientExecutionState.Executable,
                package.Plan.ExecutionFrontier.GetState(PublishingPageIngredientIds.Layout));
            Assert.IsTrue(package.Plan.IsExecutable);

            targetRead.AuthorizationEvidence.RequestUri = "https://target.sharepoint.com/_layouts/15/other.js";
            targetRead.AuthorizationEvidence.EvidenceSha256 = LiteralHttpAuthorizationEvidence.ComputeSha256(
                targetRead.AuthorizationEvidence);
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateMigration(package));

            targetRead.AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create(
                PageReferenceAuthorizationEvidence.TargetHttpProbeOperation,
                PageReferenceAuthorizationEvidence.HttpRequestUri(
                    package.Plan.TargetWebUrl,
                    action.TargetServerRelativeUrl),
                403,
                DateTimeOffset.Parse("2026-09-05T00:00:00Z"));
            verification.SnapshotDependencyId = "other-reference";
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateMigration(package));

            verification.SnapshotDependencyId = dependency.Id;
            targetRead.AuthorizationEvidence = null;
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateMigration(package));
        }

        [TestMethod]
        public void ExportValidationRejectsResealedSourceReferenceAuthorizationTampering()
        {
            var package = CreatePackage(BuiltInContentTypeId.ArticlePage, ArticlePageV1WorkflowPolicy.Instance);
            var reference = new PageReferenceSnapshot
            {
                Id = "source-denied-script",
                OriginalValue = "/sites/source/SiteAssets/denied.js",
                SourceAbsoluteUrl = "https://source.sharepoint.com/sites/source/SiteAssets/denied.js",
                SourceServerRelativeUrl = "/sites/source/SiteAssets/denied.js",
                Consumer = "field:PublishingPageContent",
                Kind = PageReferenceKind.Script,
                IsRenderableResource = true,
                CaptureStatus = PageCaptureStatus.Failed,
                AuthorizationEvidence = LiteralHttpAuthorizationEvidence.Create(
                    PageReferenceAuthorizationEvidence.SourceCaptureOperation,
                    PageReferenceAuthorizationEvidence.CsomRequestUri(package.Snapshot.Source.WebUrl),
                    403,
                    DateTimeOffset.Parse("2026-09-05T00:00:00Z"))
            };
            package.Snapshot.Dependencies.Add(reference);
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            var export = new PublishingPageExportPackage
            {
                ExportedAtUtc = package.ExportedAtUtc,
                Selection = package.Selection,
                SelectionDigest = package.SelectionDigest,
                Snapshot = package.Snapshot,
                SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(package.Snapshot)
            };

            PublishingPagePackageValidator.ValidateExport(export);

            reference.AuthorizationEvidence.RequestUri = "https://source.sharepoint.com/sites/other/_vti_bin/client.svc/ProcessQuery";
            reference.AuthorizationEvidence.EvidenceSha256 = LiteralHttpAuthorizationEvidence.ComputeSha256(
                reference.AuthorizationEvidence);
            export.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(export.Snapshot);
            Assert.ThrowsException<InvalidDataException>(() =>
                PublishingPagePackageValidator.ValidateExport(export));
        }

        private static void ResealPackage(PublishingPageMigrationPackage package)
        {
            package.Snapshot.SourceTopology = new PnP.Framework.Migration.Topology.SourceSiteCollectionSnapshot
            {
                SiteId = package.Snapshot.Source.SiteId,
                SiteCollectionUrl = package.Snapshot.Source.WebUrl,
                ServerRelativeUrl = package.Snapshot.Source.WebServerRelativeUrl,
                RootWebId = package.Snapshot.Source.WebId,
                Webs = new List<PnP.Framework.Migration.Topology.SourceWebSnapshot>
                {
                    new PnP.Framework.Migration.Topology.SourceWebSnapshot
                    {
                        SiteId = package.Snapshot.Source.SiteId,
                        WebId = package.Snapshot.Source.WebId,
                        SiteCollectionUrl = package.Snapshot.Source.WebUrl,
                        WebUrl = package.Snapshot.Source.WebUrl,
                        ServerRelativeUrl = package.Snapshot.Source.WebServerRelativeUrl
                    }
                }
            };
            package.Snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(
                package.Snapshot.Source,
                package.Snapshot.Layout,
                package.Snapshot.Fields);
            package.Snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(package.Snapshot);
            package.SnapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(package.Snapshot);
            package.Plan.SourceSnapshotDigest = package.SnapshotDigest;
            package.Plan.IngredientGraph = package.Snapshot.IngredientGraph;
            var topologyBuild = new PnP.Framework.Migration.Topology.TopologyPlanner().Build(
                new[] { package.Snapshot.SourceTopology },
                new[]
                {
                    new PnP.Framework.Migration.Topology.TargetSiteCollectionSpec
                    {
                        SourceSiteId = package.Snapshot.Source.SiteId,
                        Mode = PnP.Framework.Migration.Topology.TargetSiteMode.ExistingTargetSite,
                        TargetSiteUrl = package.Plan.TargetWebUrl,
                        ExpectedTargetSiteId = Guid.NewGuid(),
                        Title = "Target"
                    }
                });
            Assert.IsTrue(topologyBuild.IsExecutable, string.Join(Environment.NewLine, topologyBuild.Issues.Select(value => value.Message)));
            package.Plan.Topology = topologyBuild.Plan;
            package.Plan.TopologyTargetAnalysis = new PnP.Framework.Migration.Topology.TopologyTargetAnalysis
            {
                TopologyPlanDigest = package.Plan.Topology.PlanDigest,
                SiteCollections = new List<PnP.Framework.Migration.Topology.TopologySiteTargetProbe>
                {
                    new PnP.Framework.Migration.Topology.TopologySiteTargetProbe
                    {
                        SourceSiteId = package.Snapshot.Source.SiteId,
                        TargetSiteCollectionUrl = package.Plan.TargetWebUrl,
                        Exists = true,
                        Disposition = PnP.Framework.Migration.Topology.TopologyMaterializationDisposition.ReuseApprovedHost,
                        Webs = new List<PnP.Framework.Migration.Topology.TopologyWebTargetProbe>
                        {
                            new PnP.Framework.Migration.Topology.TopologyWebTargetProbe
                            {
                                SourceSiteId = package.Snapshot.Source.SiteId,
                                SourceWebId = package.Snapshot.Source.WebId,
                                TargetWebUrl = package.Plan.TargetWebUrl,
                                TargetServerRelativeUrl = package.Plan.TargetWebServerRelativeUrl,
                                Exists = true,
                                Disposition = PnP.Framework.Migration.Topology.TopologyMaterializationDisposition.ReuseApprovedHost
                            }
                        }
                    }
                }
            };
            package.Plan.IngredientActions = PublishingPageIngredientActionProjector.Project(
                package.Snapshot,
                package.Plan,
                package.Plan.IngredientGraph);
            var evaluation = PageIngredientPlanEvaluator.Evaluate(
                package.Plan.IngredientGraph,
                package.Plan.IngredientActions,
                PublishingPageIngredientAuthorizationPolicy.GetEvidence(package.Snapshot, package.Plan));
            package.Plan.MigrationOutcome = evaluation.Outcome;
            package.Plan.IngredientIssues = evaluation.Issues;
            package.Plan.ExecutionFrontier = evaluation.ExecutionFrontier;
            package.State = PublishingPagePackageStatePolicy.Derive(package.Plan);
            package.PlanDigest = PublishingPageDigest.ComputePlanDigest(package.Plan);
        }

        [TestMethod]
        public void UnifiedPublishingImporterValidatesArticleAndWelcomePackages()
        {
            var articlePackage = CreatePackage(BuiltInContentTypeId.ArticlePage, ArticlePageV1WorkflowPolicy.Instance);
            var welcomePackage = CreatePackage(BuiltInContentTypeId.WelcomePage, WelcomePageV1WorkflowPolicy.Instance);

            var articleScope = PublishingPageExecutionScope.Create(articlePackage);
            var welcomeScope = PublishingPageExecutionScope.Create(welcomePackage);

            PublishingPageImportPlanValidator.Validate(articlePackage, ArticlePageV1WorkflowPolicy.Instance, articleScope);
            PublishingPageImportPlanValidator.Validate(welcomePackage, WelcomePageV1WorkflowPolicy.Instance, welcomeScope);

            using (var articleContext = new ClientContext(articlePackage.Plan.TargetWebUrl))
            using (var welcomeContext = new ClientContext(welcomePackage.Plan.TargetWebUrl))
            {
                var articleReceipt = new ArticlePageMigrationImporter().Import(
                    articleContext, articlePackage, "not-the-approved-digest");
                var welcomeReceipt = new PnP.Framework.Migration.Pages.Publishing.Welcome.WelcomePageMigrationImporter().Import(
                    welcomeContext, welcomePackage, "not-the-approved-digest");

                Assert.IsFalse(articleReceipt.MutationStarted);
                Assert.IsFalse(welcomeReceipt.MutationStarted);
                Assert.AreEqual("PlanDigestNotApproved", articleReceipt.AdmissionFailure.Code);
                Assert.AreEqual("PlanDigestNotApproved", welcomeReceipt.AdmissionFailure.Code);
            }
        }

        [TestMethod]
        public void PageStorageAssertionBuilderGeneratesAssertionsForArticleAndWelcome()
        {
            var articlePackage = CreatePackage(BuiltInContentTypeId.ArticlePage, ArticlePageV1WorkflowPolicy.Instance);
            var assertions = PageStorageAssertionBuilder.Build(
                articlePackage.Snapshot,
                articlePackage.Plan.TargetPageServerRelativeUrl,
                articlePackage.Plan.DependencyActions,
                articlePackage.Plan.ExpectedPublishingPageContentSha256,
                articlePackage.Plan.TargetLifecycle);

            Assert.IsTrue(assertions.Any(a => a.StartsWith("target-page=")));
            Assert.IsTrue(assertions.Contains("fresh-read-target-file-identity"));
            Assert.IsTrue(assertions.Contains("fresh-read-target-page-content-type"));
        }

        [TestMethod]
        public void PublishingPagePackageFileStoreSavesAndLoadsAcrossProfiles()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "pnp-pub-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                var package = CreatePackage(BuiltInContentTypeId.ArticlePage, ArticlePageV1WorkflowPolicy.Instance);

                var savedPath = PublishingPagePackageFileStore.SaveMigration(tempDir, package, null, true);
                Assert.IsTrue(File.Exists(savedPath));

                var loaded = PublishingPagePackageFileStore.LoadMigration(tempDir);
                Assert.IsNotNull(loaded);
                Assert.AreEqual(package.Snapshot.Source.Title, loaded.Snapshot.Source.Title);

                var articleSaved = ArticlePagePackageFileStore.SaveMigration(tempDir, package, true);
                Assert.IsTrue(File.Exists(articleSaved));

                var articleLoaded = ArticlePagePackageFileStore.LoadMigration(tempDir);
                Assert.IsNotNull(articleLoaded);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
}
}
