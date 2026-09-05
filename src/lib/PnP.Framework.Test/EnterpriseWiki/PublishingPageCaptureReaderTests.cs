using Microsoft.SharePoint.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Security;
using PnP.Framework.Utilities.UnitTests.Model;
using PnP.Framework.Utilities.UnitTests.Web;
using System.Collections.Generic;

namespace PnP.Framework.Test.EnterpriseWiki
{
    [TestClass]
    public class PublishingPageCaptureReaderTests
    {
        [TestMethod]
        public void LoadInitialStateBatchesItemFieldsAndSecurityEvidenceInSingleRequest()
        {
            const string siteUrl = "https://contoso.sharepoint.com/sites/publishing";
            const string pagePath = "/sites/publishing/Pages/Article.aspx";
            const string pageContent = "<p>Captured body</p>";
            var innerProvider = new MockEntryResponseProvider();
            innerProvider.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = siteUrl,
                PropertyName = "Site",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.Site"
                }
            });
            innerProvider.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = siteUrl,
                PropertyName = "Web",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.Web"
                }
            });
            innerProvider.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = siteUrl,
                Method = "GetFileByServerRelativePath",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.File",
                    ["Exists"] = true
                }
            });
            innerProvider.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = siteUrl,
                PropertyName = "ListItemAllFields",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.ListItem",
                    ["Id"] = 37,
                    ["HasUniqueRoleAssignments"] = true,
                    ["PublishingPageContent"] = pageContent
                }
            });
            innerProvider.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = siteUrl,
                PropertyName = "ContentType",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.ContentType"
                }
            });
            innerProvider.ResponseEntries.Add(new MockResponseEntry<object>
            {
                Url = siteUrl,
                PropertyName = "RoleAssignments",
                ReturnValue = new Dictionary<string, object>
                {
                    ["_ObjectType_"] = "SP.RoleAssignmentCollection",
                    ["_Child_Items_"] = new object[0]
                }
            });
            var provider = new RecordingResponseProvider(innerProvider);

            using (var context = new ClientContext(siteUrl))
            {
                context.WebRequestExecutorFactory = new MockWebRequestExecutorFactory(provider);
                var file = context.Web.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(pagePath));
                var item = file.ListItemAllFields;

                PublishingPageCaptureReader.LoadInitialState(
                    context,
                    context.Site,
                    context.Web,
                    file,
                    item,
                    item.ContentType);

                Assert.AreEqual(1, provider.RequestBodies.Count, "The initial page envelope must use one CSOM round trip.");
                StringAssert.Contains(provider.RequestBodies[0], "Name=\"HasUniqueRoleAssignments\"");
                StringAssert.Contains(provider.RequestBodies[0], "<Query SelectAllProperties=\"true\"");
                Assert.IsFalse(
                    provider.RequestBodies[0].Contains("<Identity "),
                    "The initial load must not depend on a previously returned ListItem identity.");
                Assert.AreEqual(37, item.Id);
                Assert.IsTrue(item.HasUniqueRoleAssignments, "The item-level security flag must remain captured.");
                Assert.AreEqual(pageContent, item.FieldValues["PublishingPageContent"], "The full field-value envelope must remain captured.");

                var security = PageSecuritySnapshotReader.Read(context, item, new List<string>());
                Assert.IsTrue(security.HasUniqueRoleAssignments);
                Assert.AreEqual(0, security.RoleAssignments.Count);
            }
        }

        private sealed class RecordingResponseProvider : IMockResponseProvider
        {
            private readonly IMockResponseProvider inner;

            public RecordingResponseProvider(IMockResponseProvider inner)
            {
                this.inner = inner;
            }

            public List<string> RequestBodies { get; } = new List<string>();

            public string GetResponse(string url, string verb, string body)
            {
                RequestBodies.Add(body);
                return inner.GetResponse(url, verb, body);
            }
        }
    }
}
