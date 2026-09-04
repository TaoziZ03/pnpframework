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

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("PageFromDocPack.aspx", out var docPack));
            Assert.AreEqual(BuiltInContentTypeId.ArticlePage, docPack.AssociatedContentTypeId);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("BlankWebPartPage.aspx", out var blankWp));
            Assert.AreEqual(BuiltInContentTypeId.WelcomePage, blankWp.AssociatedContentTypeId);
            Assert.AreEqual("Welcome Page", blankWp.AssociatedContentTypeName);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("WelcomeSplash.aspx", out var splash));
            Assert.AreEqual(BuiltInContentTypeId.WelcomePage, splash.AssociatedContentTypeId);

            Assert.IsTrue(PublishingPageNativeLayoutCatalog.TryGetProfile("WelcomeLinks.aspx", out var welcomeLinks));
            Assert.AreEqual(BuiltInContentTypeId.WelcomePage, welcomeLinks.AssociatedContentTypeId);

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
                Description = "Article Page with left image"
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
                Description = "Article Page with left image"
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
            Assert.AreSame(EnterpriseWikiV1WorkflowPolicy.Instance, ewPolicy);

            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByWorkflowId("article-page-v1", out var articlePolicy));
            Assert.AreSame(ArticlePageV1WorkflowPolicy.Instance, articlePolicy);

            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByWorkflowId("welcome-page-v1", out var welcomePolicy));
            Assert.AreSame(WelcomePageV1WorkflowPolicy.Instance, welcomePolicy);

            // Resolve by profile ID
            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByProfileId(PageProfileIds.EnterpriseWiki, out var ewByProfile));
            Assert.AreSame(EnterpriseWikiV1WorkflowPolicy.Instance, ewByProfile);

            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByProfileId(PageProfileIds.ArticlePage, out var articleByProfile));
            Assert.AreSame(ArticlePageV1WorkflowPolicy.Instance, articleByProfile);

            Assert.IsTrue(PublishingPageProfileRegistry.TryGetPolicyByProfileId(PageProfileIds.WelcomePage, out var welcomeByProfile));
            Assert.AreSame(WelcomePageV1WorkflowPolicy.Instance, welcomeByProfile);

            // Resolve by ContentTypeId
            Assert.IsTrue(PublishingPageProfileRegistry.TryResolvePolicyByContentType(BuiltInContentTypeId.ArticlePage + "0099", out var resolvedArticle));
            Assert.AreSame(ArticlePageV1WorkflowPolicy.Instance, resolvedArticle);

            Assert.IsTrue(PublishingPageProfileRegistry.TryResolvePolicyByContentType(BuiltInContentTypeId.WelcomePage + "0088", out var resolvedWelcome));
            Assert.AreSame(WelcomePageV1WorkflowPolicy.Instance, resolvedWelcome);

            Assert.IsTrue(PublishingPageProfileRegistry.TryResolvePolicyByContentType(BuiltInContentTypeId.EnterpriseWikiPage + "0077", out var resolvedEw));
            Assert.AreSame(EnterpriseWikiV1WorkflowPolicy.Instance, resolvedEw);

            Assert.IsFalse(PublishingPageProfileRegistry.TryResolvePolicyByContentType("0x010100UNKNOWN", out _));

            // Unified ResolvePolicy method
            Assert.AreSame(ArticlePageV1WorkflowPolicy.Instance, PublishingPageProfileRegistry.ResolvePolicy(workflowId: "article-page-v1"));
            Assert.AreSame(WelcomePageV1WorkflowPolicy.Instance, PublishingPageProfileRegistry.ResolvePolicy(profileId: PageProfileIds.WelcomePage));
            Assert.AreSame(EnterpriseWikiV1WorkflowPolicy.Instance, PublishingPageProfileRegistry.ResolvePolicy(contentTypeId: BuiltInContentTypeId.EnterpriseWikiPage));

            // All registered policies count
            Assert.AreEqual(3, PublishingPageProfileRegistry.RegisteredPolicies.Count);
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
            Assert.IsFalse(article.RecognizedPageFields.Contains("SummaryLinks"));
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
            var pageBytes = Encoding.UTF8.GetBytes("<%@ Page %>");
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
                Runtime = new PageRuntimeSnapshot
                {
                    SchemaVersion = "pnp-page-runtime/v1",
                    AdapterId = PageRuntimeAdapterIds.Publishing,
                    ResolutionState = PageRuntimeResolutionState.Resolved,
                    Diagnostics = new List<string>()
                },
                Layout = new PublishingPageLayoutSnapshot
                {
                    Url = "https://source.sharepoint.com/_catalogs/masterpage/" + policy.PreferredTargetPageLayoutFileName,
                    ServerRelativeUrl = "/_catalogs/masterpage/" + policy.PreferredTargetPageLayoutFileName,
                    FileName = policy.PreferredTargetPageLayoutFileName,
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
            snapshot.ProfileSignals = PublishingPageProfileSignalProjector.Project(snapshot.Source, snapshot.Layout, snapshot.Fields);
            snapshot.IngredientGraph = PublishingPageIngredientGraphProjector.Project(snapshot);

            var snapshotDigest = PublishingPageDigest.ComputeSnapshotDigest(snapshot);
            var layoutPlan = PublishingPageLayoutPlanFactory.Create(
                snapshot.Layout,
                new Uri(snapshot.Source.WebUrl),
                new Uri("https://target.sharepoint.com/sites/target"),
                new Uri("https://target.sharepoint.com/sites/target"),
                policy.PreferredTargetPageLayoutFileName);
            var layoutProbe = new PublishingPageLayoutTargetProbe
            {
                TargetServerRelativeUrl = layoutPlan.TargetServerRelativeUrl,
                FileExists = true,
                ExistingAssociatedContentTypeName = "Test Page",
                ExistingAssociatedContentTypeId = contentTypeId,
                AssociatedContentTypeAvailable = true,
                ResolvedAssociatedContentTypeId = contentTypeId,
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
    }
}
