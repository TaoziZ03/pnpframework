using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Modernization.Cache;
using PnP.Framework.Modernization.Publishing;
using PnP.Framework.Modernization.Telemetry;
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
        public void InPlaceDifferentSiteCollectionFailsClosedBeforeOverwritePolicy()
        {
            var result = PublishingPageTransformationValidator.ValidateTarget(
                true,
                SourceSiteId,
                Guid.Parse("2e33ad34-f43a-4d75-af55-e0deba981252"),
                SourceWebId,
                Guid.Parse("7896708f-f5ea-4f22-aeb3-6223bf8897b0"),
                "STS",
                true);

            Assert.AreEqual(PublishingPageTransformationTarget.InPlaceDifferentSiteCollection, result);
            Assert.IsFalse(PublishingPageTransformationValidator.CanOverwriteTarget(result, true));
            Assert.IsFalse(PublishingPageTransformationValidator.UsesCrossSitePermissionSemantics(result));
        }

        [TestMethod]
        public void DefaultSameSiteDifferentWebKeepsExistingSameSiteRejection()
        {
            var result = PublishingPageTransformationValidator.ValidateTarget(
                false,
                SourceSiteId,
                SourceSiteId,
                SourceWebId,
                Guid.Parse("7896708f-f5ea-4f22-aeb3-6223bf8897b0"),
                "STS",
                false);

            Assert.AreEqual(PublishingPageTransformationTarget.SameSiteCollectionNotAllowed, result);
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

        [TestMethod]
        public void TransformSeamKnownCollisionNeverInvokesAddAndKeepsSourceUnchanged()
        {
            using (var sourceContext = CreateContext("https://contoso.sharepoint.com/sites/enterprise-wiki"))
            using (var targetContext = CreateContext("https://contoso.sharepoint.com/sites/enterprise-wiki"))
            {
                var information = CreateInPlaceInformation(sourceContext, overwrite: true);
                var sourceBefore = SnapshotSource(information.SourcePage);
                var sourceHadPendingRequest = sourceContext.HasPendingRequest;
                var addCalls = 0;
                var transformator = CreateTransformator(sourceContext, targetContext);

                Assert.ThrowsException<ArgumentException>(() => transformator.CreateTargetPageAfterCollisionProbe(
                    PublishingPageTransformationTarget.SameWeb,
                    information,
                    () => null,
                    _ =>
                    {
                        addCalls++;
                        return null;
                    },
                    out _));

                Assert.AreEqual(0, addCalls, "AddClientSidePage seam must not run for a known in-place collision.");
                AssertSourceUnchanged(sourceBefore, information.SourcePage);
                Assert.AreEqual(sourceHadPendingRequest, sourceContext.HasPendingRequest, "The source context request state changed.");
            }
        }

        [TestMethod]
        public void TransformSeamRejectedInPlaceTargetsNeverProbeOrAddAndKeepSourceUnchanged()
        {
            using (var sourceContext = CreateContext("https://contoso.sharepoint.com/sites/enterprise-wiki"))
            using (var targetContext = CreateContext("https://contoso.sharepoint.com/sites/enterprise-wiki"))
            {
                var information = CreateInPlaceInformation(sourceContext, overwrite: true);
                var sourceBefore = SnapshotSource(information.SourcePage);
                var sourceHadPendingRequest = sourceContext.HasPendingRequest;
                var transformator = CreateTransformator(sourceContext, targetContext);

                foreach (var rejectedTarget in new[]
                {
                    PublishingPageTransformationTarget.InPlaceDifferentSiteCollection,
                    PublishingPageTransformationTarget.SameSiteCollectionDifferentWeb,
                })
                {
                    var probeCalls = 0;
                    var addCalls = 0;

                    Assert.ThrowsException<ArgumentException>(() => transformator.CreateTargetPageAfterCollisionProbe(
                        rejectedTarget,
                        information,
                        () =>
                        {
                            probeCalls++;
                            return null;
                        },
                        _ =>
                        {
                            addCalls++;
                            return null;
                        },
                        out _));

                    Assert.AreEqual(0, probeCalls, $"Target probe ran for rejected target '{rejectedTarget}'.");
                    Assert.AreEqual(0, addCalls, $"AddClientSidePage seam ran for rejected target '{rejectedTarget}'.");
                    AssertSourceUnchanged(sourceBefore, information.SourcePage);
                    Assert.AreEqual(sourceHadPendingRequest, sourceContext.HasPendingRequest, "The source context request state changed.");
                }
            }
        }

        [TestMethod]
        public void TransformSeamInconclusiveProbeNeverInvokesAddAndKeepsSourceUnchanged()
        {
            using (var sourceContext = CreateContext("https://contoso.sharepoint.com/sites/enterprise-wiki"))
            using (var targetContext = CreateContext("https://contoso.sharepoint.com/sites/enterprise-wiki"))
            {
                var information = CreateInPlaceInformation(sourceContext, overwrite: true);
                var sourceBefore = SnapshotSource(information.SourcePage);
                var sourceHadPendingRequest = sourceContext.HasPendingRequest;
                var addCalls = 0;
                var transformator = CreateTransformator(sourceContext, targetContext);

                var exception = Assert.ThrowsException<InvalidOperationException>(() => transformator.CreateTargetPageAfterCollisionProbe(
                    PublishingPageTransformationTarget.SameWeb,
                    information,
                    () => throw new InvalidOperationException("target probe inconclusive"),
                    _ =>
                    {
                        addCalls++;
                        return null;
                    },
                    out _));

                Assert.AreEqual("target probe inconclusive", exception.Message);
                Assert.AreEqual(0, addCalls, "AddClientSidePage seam must not run after an inconclusive in-place probe.");
                AssertSourceUnchanged(sourceBefore, information.SourcePage);
                Assert.AreEqual(sourceHadPendingRequest, sourceContext.HasPendingRequest, "The source context request state changed.");
            }
        }

        [TestMethod]
        public void TransformSeamConfirmedMissingTargetInvokesAddOnce()
        {
            using (var sourceContext = CreateContext("https://contoso.sharepoint.com/sites/enterprise-wiki"))
            using (var targetContext = CreateContext("https://contoso.sharepoint.com/sites/enterprise-wiki"))
            {
                var information = CreateInPlaceInformation(sourceContext, overwrite: false);
                var addCalls = 0;
                var addedPageName = string.Empty;
                var transformator = CreateTransformator(sourceContext, targetContext);

                transformator.CreateTargetPageAfterCollisionProbe(
                    PublishingPageTransformationTarget.SameWeb,
                    information,
                    () => throw new ArgumentException($"{information.TargetPageName} - {LogStrings.TransformPageDoesNotExistInWeb}"),
                    pageName =>
                    {
                        addCalls++;
                        addedPageName = pageName;
                        return null;
                    },
                    out _);

                Assert.AreEqual(1, addCalls);
                Assert.AreEqual("converted/source.aspx", addedPageName);
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

        private static PublishingPageTransformator CreateTransformator(ClientContext sourceContext, ClientContext targetContext)
        {
            return new PublishingPageTransformator(sourceContext, targetContext, new PageTransformation(), null);
        }

        private static PublishingPageTransformationInformation CreateInPlaceInformation(ClientContext sourceContext, bool overwrite)
        {
            var sourcePage = sourceContext.Web.Lists.GetByTitle("Pages").GetItemById(7);
            sourcePage["CCD_SourceIdentity"] = "Pages/7/v3";
            sourcePage["CCD_SourceDigest"] = "sha256:unchanged";

            return new PublishingPageTransformationInformation(sourcePage, overwrite)
            {
                InPlacePublishingPage = true,
                Folder = "converted/",
                TargetPageName = "source.aspx",
            };
        }

        private static Dictionary<string, string> SnapshotSource(ListItem sourcePage)
        {
            return sourcePage.FieldValues.ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.ToString());
        }

        private static void AssertSourceUnchanged(Dictionary<string, string> expected, ListItem actual)
        {
            var actualSnapshot = SnapshotSource(actual);
            CollectionAssert.AreEquivalent(expected.Keys.ToArray(), actualSnapshot.Keys.ToArray());

            foreach (var pair in expected)
            {
                Assert.AreEqual(pair.Value, actualSnapshot[pair.Key], $"Source field '{pair.Key}' changed.");
            }
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
