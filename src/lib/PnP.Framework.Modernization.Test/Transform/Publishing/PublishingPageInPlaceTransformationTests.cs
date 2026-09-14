using System;
using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Modernization.Cache;
using PnP.Framework.Modernization.Publishing;
using PnP.Framework.Modernization.Transform;
using PnP.Framework.Utilities.UnitTests.Model;
using PnP.Framework.Utilities.UnitTests.Web;

namespace PnP.Framework.Modernization.Tests.Transform.Publishing
{
    [TestClass]
    public class PublishingPageInPlaceTransformationTests
    {
        private static readonly Guid SourceSiteId = Guid.Parse("c33f44c0-e1ce-4d87-a961-98669e76bca6");
        private static readonly Guid SourceWebId = Guid.Parse("a467c0f4-dcf4-49f3-8a08-e33de32d3202");

        [TestMethod]
        public void InPlacePublishingPageDefaultsToFalse()
        {
            var information = new PublishingPageTransformationInformation(null);

            Assert.IsFalse(information.InPlacePublishingPage);
        }

        [TestMethod]
        public void SameWebRequiresExplicitOptIn()
        {
            var result = ValidateSameWeb(inPlacePublishingPage: false, targetWebTemplate: "ENTERWIKI", hasWritableSitePages: true);

            Assert.AreEqual(PublishingPageTransformationTarget.SameSiteCollectionNotAllowed, result);
        }

        [TestMethod]
        public void SameSiteCollectionDifferentWebFailsClosed()
        {
            var result = PublishingPageTransformationValidator.ValidateTarget(
                true,
                SourceSiteId,
                SourceSiteId,
                SourceWebId,
                Guid.Parse("7896708f-f5ea-4f22-aeb3-6223bf8897b0"),
                "ENTERWIKI",
                true);

            Assert.AreEqual(PublishingPageTransformationTarget.SameSiteCollectionDifferentWeb, result);
        }

        [TestMethod]
        public void SameWebRequiresEnterpriseWikiAndWritableSitePages()
        {
            Assert.AreEqual(
                PublishingPageTransformationTarget.SameWebRequiresEnterpriseWiki,
                ValidateSameWeb(true, "STS", true));
            Assert.AreEqual(
                PublishingPageTransformationTarget.SameWebRequiresWritableSitePages,
                ValidateSameWeb(true, "ENTERWIKI", false));
            Assert.AreEqual(
                PublishingPageTransformationTarget.SameWeb,
                ValidateSameWeb(true, "enterwiki", true));
        }

        [TestMethod]
        public void CrossSiteCollectionKeepsExistingTargetPolicyAndPermissionSemantics()
        {
            var result = PublishingPageTransformationValidator.ValidateTarget(
                false,
                SourceSiteId,
                Guid.Parse("2e33ad34-f43a-4d75-af55-e0deba981252"),
                SourceWebId,
                Guid.Empty,
                "UNSUPPORTED",
                false);

            Assert.AreEqual(PublishingPageTransformationTarget.CrossSiteCollection, result);
            Assert.IsTrue(PublishingPageTransformationValidator.CanOverwriteTarget(result, true));
            Assert.IsTrue(PublishingPageTransformationValidator.UsesCrossSitePermissionSemantics(result));
        }

        [TestMethod]
        public void SameWebNeverOverwritesAndUsesInPlacePermissionSemantics()
        {
            var result = ValidateSameWeb(true, "ENTERWIKI", true);

            Assert.IsFalse(PublishingPageTransformationValidator.CanOverwriteTarget(result, true));
            Assert.IsFalse(PublishingPageTransformationValidator.UsesCrossSitePermissionSemantics(result));
        }

        [TestMethod]
        public void SameWebUrlTransformationIsANoOp()
        {
            const string webUrl = "https://contoso.sharepoint.com/sites/enterprise-wiki";
            const string sourceUrl = "/sites/enterprise-wiki/Pages/source.aspx";

            using (var sourceContext = CreateContext(webUrl))
            using (var targetContext = CreateContext(webUrl))
            {
                var information = new PublishingPageTransformationInformation(null);
                var transformator = new UrlTransformator(information, sourceContext, targetContext);

                Assert.AreEqual(sourceUrl, transformator.Transform(sourceUrl));
            }
        }

        [TestMethod]
        public void SameWebAssetTransferIsANoOp()
        {
            const string webUrl = "https://contoso.sharepoint.com/sites/enterprise-wiki";
            const string sourceAssetUrl = "/sites/enterprise-wiki/PublishingImages/image.png";

            using (var sourceContext = CreateContext(webUrl))
            using (var targetContext = CreateContext(webUrl))
            {
                var transfer = new AssetTransfer(sourceContext, targetContext);

                Assert.AreEqual(sourceAssetUrl, transfer.TransferAsset(sourceAssetUrl, "target.aspx"));
            }
        }

        private static PublishingPageTransformationTarget ValidateSameWeb(
            bool inPlacePublishingPage,
            string targetWebTemplate,
            bool hasWritableSitePages)
        {
            return PublishingPageTransformationValidator.ValidateTarget(
                inPlacePublishingPage,
                SourceSiteId,
                SourceSiteId,
                SourceWebId,
                SourceWebId,
                targetWebTemplate,
                hasWritableSitePages);
        }

        private static ClientContext CreateContext(string webUrl)
        {
            var responseProvider = new MockEntryResponseProvider();
            responseProvider.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                PropertyName = "Web",
                ReturnValue = new
                {
                    Url = webUrl,
                    ServerRelativeUrl = "/sites/enterprise-wiki",
                },
            });
            responseProvider.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = webUrl,
                PropertyName = "Site",
                ReturnValue = new
                {
                    Url = webUrl,
                    ServerRelativeUrl = "/sites/enterprise-wiki",
                },
            });

            CacheManager.Instance.SetSharePointVersion(new Uri(webUrl), SPVersion.SPO);
            CacheManager.Instance.SetExactSharePointVersion(new Uri(webUrl), "16.0.0.26000");

            return new ClientContext(webUrl)
            {
                WebRequestExecutorFactory = new MockWebRequestExecutorFactory(responseProvider),
            };
        }
    }
}
