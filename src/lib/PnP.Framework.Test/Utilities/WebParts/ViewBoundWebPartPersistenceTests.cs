using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.WebParts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using PnP.Framework.Entities;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWebParts.Planning;
using PnP.Framework.Utilities.UnitTests.Model;
using PnP.Framework.Utilities.UnitTests.Web;
using PnP.Framework.Utilities.WebParts;
using PnP.Framework.Utilities.WebParts.Processors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PnP.Framework.Test.Utilities.WebParts
{
    [TestClass]
    public class ViewBoundWebPartPersistenceTests
    {
        [DataTestMethod]
        [DataRow("ListViewWebPart", "Microsoft.SharePoint.Core")]
        [DataRow("ListViewWebPart", "Microsoft.SharePoint")]
        [DataRow("XsltListViewWebPart", "Microsoft.SharePoint.Core")]
        [DataRow("XsltListViewWebPart", "Microsoft.SharePoint")]
        public void RecognizesExactNativeDeclarationsInBothFormats(string type, string assembly)
        {
            Assert.IsTrue(ViewBoundWebPartPersistence.RequiresSave(ViewBoundRequestFixture.V2(type, assembly)));
            Assert.IsTrue(ViewBoundWebPartPersistence.RequiresSave(ViewBoundRequestFixture.V3(type, assembly)));
        }

        [TestMethod]
        public void UsesXmlNamespacesNotPrefixesQuotesOrSubstringMatches()
        {
            const string v2 = "<p:WebPart xmlns:p='http://schemas.microsoft.com/WebPart/v2'>"
                + "<p:TypeName>Microsoft.SharePoint.WebPartPages.ListViewWebPart</p:TypeName>"
                + "<p:Assembly>Microsoft.SharePoint.Core, Version=16.0.0.0</p:Assembly></p:WebPart>";
            const string v3 = "<webParts><p:webPart xmlns:p='http://schemas.microsoft.com/WebPart/v3'>"
                + "<p:metaData><p:type name='Microsoft.SharePoint.WebPartPages.XsltListViewWebPart, Microsoft.SharePoint'/>"
                + "</p:metaData><p:data><p:properties/></p:data></p:webPart></webParts>";
            Assert.IsTrue(ViewBoundWebPartPersistence.RequiresSave(v2));
            Assert.IsTrue(ViewBoundWebPartPersistence.RequiresSave(v3));
            Assert.IsFalse(ViewBoundWebPartPersistence.RequiresSave(v2.Replace("/v2'", "/v2-spoof'")));
            Assert.IsFalse(ViewBoundWebPartPersistence.RequiresSave(v3.Replace("/v3'", "/v3-spoof'")));
        }

        [DataTestMethod]
        [DataRow("ContentEditorWebPart")]
        [DataRow("ScriptEditorWebPart")]
        [DataRow("PageViewerWebPart")]
        [DataRow("ListFormWebPart")]
        [DataRow("UnknownWebPart")]
        [DataRow("ListViewWebPartSpoof")]
        [DataRow("XsltListViewWebPartSpoof")]
        [DataRow("Custom.ListViewWebPart")]
        [DataRow("Custom.XsltListViewWebPart")]
        public void NonViewAndSpoofedTypesDoNotQueueSave(string type)
        {
            foreach (var xml in new[] { ViewBoundRequestFixture.V2(type), ViewBoundRequestFixture.V3(type) })
            {
                Assert.IsFalse(ViewBoundWebPartPersistence.RequiresSave(xml));
                var requests = new ViewBoundRequestFixture();
                using (var context = requests.CreateContext())
                {
                    var manager = context.Web.GetFileByServerRelativeUrl(ViewBoundRequestFixture.PagePath)
                        .GetLimitedWebPartManager(PersonalizationScope.Shared);
                    var imported = manager.ImportWebPart(xml);
                    var added = manager.AddWebPart(imported.WebPart, "wpz", 0);
                    ViewBoundWebPartPersistence.QueueSaveIfRequired(added, xml);
                    context.ExecuteQueryRetry();
                    Assert.AreEqual(0, requests.SaveCount);
                    requests.AssertOneAdd(xml, 0);
                }
            }
        }

        [TestMethod]
        public void InvalidAmbiguousOrEmbeddedTypeMetadataDoesNotAcquireSaveOrReplayPermission()
        {
            var v2 = ViewBoundRequestFixture.V2("ListViewWebPart");
            var v3 = ViewBoundRequestFixture.V3();
            var invalid = new[]
            {
                null, "", " ", "<broken", "<unknown />",
                v2.Replace("Microsoft.SharePoint.Core", "Custom.Microsoft.SharePoint.Core"),
                v3.Replace("Microsoft.SharePoint,", "Microsoft.SharePoint.Spoof,"),
                v2.Replace("<TypeName>", "<ForeignTypeName>").Replace("</TypeName>", "</ForeignTypeName>"),
                v2.Replace("</WebPart>", "<TypeName>Microsoft.SharePoint.WebPartPages.ListViewWebPart</TypeName></WebPart>"),
                v3.Replace("</metaData>", "<type name='Microsoft.SharePoint.WebPartPages.ListViewWebPart, Microsoft.SharePoint'/></metaData>"),
                v3.Replace("</webParts>", "<webPart xmlns='http://schemas.microsoft.com/WebPart/v3'/></webParts>"),
                "<container>" + v2 + "</container>",
                "<webParts><webPart xmlns='http://schemas.microsoft.com/WebPart/v3'><data><properties>"
                    + "<property name='Description'><![CDATA[" + v3 + "]]></property>"
                    + "</properties></data></webPart></webParts>",
                "<!DOCTYPE WebPart [<!ENTITY native 'Microsoft.SharePoint.WebPartPages.ListViewWebPart'>]>"
                    + v2.Replace("Microsoft.SharePoint.WebPartPages.ListViewWebPart", "&native;")
            };
            foreach (var xml in invalid)
            {
                Assert.IsFalse(ViewBoundWebPartPersistence.RequiresSave(xml), xml ?? "null");
            }
            Assert.IsInstanceOfType<PassThroughProcessor>(WebPartPostProcessorFactory.Resolve("<unknown />"));
            Assert.IsNotNull(ClassicWebPartReplayCapabilityPolicy.GetBlocker("<unknown />"));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void QueuesExactlyOneSaveOnAddedDefinitionInOriginatingAddRequest(bool xlv)
        {
            var xml = xlv ? ViewBoundRequestFixture.V3() : NativeV2Export.Xml;
            var requests = new ViewBoundRequestFixture();
            using (var context = requests.CreateContext())
            {
                var manager = context.Web.GetFileByServerRelativeUrl(ViewBoundRequestFixture.PagePath)
                    .GetLimitedWebPartManager(PersonalizationScope.Shared);
                var imported = manager.ImportWebPart(xml);
                var added = manager.AddWebPart(imported.WebPart, "wpz", 0);
                ViewBoundWebPartPersistence.QueueSaveIfRequired(added, xml);
                Assert.AreEqual(0, requests.Bodies.Count, "The helper must not execute or retry a request.");
                context.Load(added, value => value.Id);
                context.ExecuteQueryRetry();
                Assert.AreEqual(ViewBoundRequestFixture.AddedId, added.Id);
                Assert.AreSame(context, added.Context);
                Assert.AreEqual(1, requests.Bodies.Count);
                requests.AssertOneAdd(xml, 1);
            }
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void PageExtensionsSaveBeforeReturningUsingOriginalContext(bool xlv, bool childWeb)
        {
            var xml = xlv ? ViewBoundRequestFixture.V3() : NativeV2Export.Xml;
            var requests = new ViewBoundRequestFixture();
            using (var context = requests.CreateContext())
            {
                var web = childWeb ? context.Site.OpenWebById(ViewBoundRequestFixture.ChildWebId) : context.Web;
                var added = web.AddWebPartToWebPartPage(ViewBoundRequestFixture.PagePath,
                    new WebPartEntity { WebPartXml = xml, WebPartZone = "wpz", WebPartIndex = 0 });
                Assert.AreEqual(ViewBoundRequestFixture.AddedId, added.Id);
                Assert.AreSame(context, added.Context);
                requests.AssertOneAdd(xml, 1);
                Assert.AreEqual(ViewBoundRequestFixture.RootUrl + "/_vti_bin/client.svc/ProcessQuery",
                    requests.Urls[requests.AddRequestIndex]);
            }
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void XlvHiddenViewRestorationRemainsAfterSaveWithNoSecondSave(bool selectView, bool childWeb)
        {
            var fallbackView = "<View><Query><OrderBy><FieldRef Name='ID'/></OrderBy></Query><RowLimit>7</RowLimit></View>";
            var properties = "<property name='ListId' type='string'>" + ViewBoundRequestFixture.ListId + "</property>"
                + "<property name='XmlDefinition' type='string'><![CDATA[" + fallbackView + "]]></property>"
                + (selectView ? "<property name='ViewId' type='string'>" + ViewBoundRequestFixture.SelectedViewId + "</property>" : "");
            var xml = ViewBoundRequestFixture.V3(properties: properties);
            var requests = new ViewBoundRequestFixture();
            using (var context = requests.CreateContext())
            {
                var web = childWeb ? context.Site.OpenWebById(ViewBoundRequestFixture.ChildWebId) : context.Web;
                web.AddWebPartToWebPartPage(ViewBoundRequestFixture.PagePath,
                    new WebPartEntity { WebPartXml = xml, WebPartZone = "wpz", WebPartIndex = 0 });
            }
            requests.AssertOneAdd(xml, 1);
            var updateIndex = requests.Bodies.FindIndex(body => body.Descendants()
                .Any(value => value.Name.LocalName == "SetProperty" && (string)value.Attribute("Name") == "ListViewXml"));
            Assert.IsTrue(updateIndex > requests.AddRequestIndex, "Hidden-view correction must follow the completed Add/Save request.");
            var update = requests.Bodies[updateIndex];
            var setter = update.Descendants().Single(value => value.Name.LocalName == "SetProperty"
                && (string)value.Attribute("Name") == "ListViewXml");
            var valueXml = setter.Elements().Single().Value;
            Assert.IsTrue(XNode.DeepEquals(XElement.Parse(selectView ? ViewBoundRequestFixture.SelectedViewXml : fallbackView),
                XElement.Parse("<View>" + valueXml + "</View>")), "View selection/schema restoration changed.");
            StringAssert.Contains(update.ToString(), "view:" + ViewBoundRequestFixture.AddedId);
            Assert.AreEqual((childWeb ? ViewBoundRequestFixture.ChildUrl : ViewBoundRequestFixture.RootUrl)
                + "/_vti_bin/client.svc/ProcessQuery", requests.Urls[updateIndex]);
            Assert.AreEqual(1, requests.SaveCount, "A later save could overwrite the corrected hidden view.");
        }

        [DataTestMethod]
        [DataRow("AddWebPart")]
        [DataRow("SaveWebPartChanges")]
        [DataRow("Execute")]
        public void PageExtensionsPropagatesFailuresWithoutASecondImportOrAdd(string failure)
        {
            var requests = new ViewBoundRequestFixture { FailOn = failure };
            using (var context = requests.CreateContext())
            {
                var error = Assert.ThrowsException<ServerException>(() => context.Web.AddWebPartToWebPartPage(
                    ViewBoundRequestFixture.PagePath, new WebPartEntity
                    {
                        WebPartXml = NativeV2Export.Xml, WebPartZone = "wpz", WebPartIndex = 0
                    }));
                StringAssert.Contains(error.Message, "Synthetic " + failure + " failure");
                requests.AssertOneAdd(NativeV2Export.Xml, 1);
                Assert.AreEqual(requests.AddRequestIndex + 1, requests.Bodies.Count,
                    "No postprocessor or blind Add replay may follow a failed save/execute.");
            }
        }

        [TestMethod]
        public void ExactNativeV2ExportIsRecognizedWithoutChangingItsTypeOrPostprocessor()
        {
            Assert.AreEqual(NativeV2Export.Sha256, MigrationDigest.ComputeSha256(NativeV2Export.Xml));
            Assert.IsTrue(ViewBoundWebPartPersistence.RequiresSave(NativeV2Export.Xml));
            Assert.IsInstanceOfType<PassThroughProcessor>(WebPartPostProcessorFactory.Resolve(NativeV2Export.Xml));
        }
    }

    // Synthetic CSOM transport only: real CSOM serialization and the existing PnP mock executor/provider.
    // It is neither a SharePoint runtime emulator nor evidence of persisted target fidelity.
    internal sealed class ViewBoundRequestFixture : IMockResponseProvider
    {
        internal const string RootUrl = "https://contoso.sharepoint.com/sites/save-test";
        internal const string ChildUrl = RootUrl + "/child";
        internal const string PagePath = "/sites/save-test/child/Pages/test.aspx";
        internal const string SelectedViewXml = "<View><Query><OrderBy><FieldRef Name='Title'/></OrderBy></Query><RowLimit>19</RowLimit></View>";
        internal static readonly Guid RootWebId = new Guid("11111111-1111-1111-1111-111111111111");
        internal static readonly Guid ChildWebId = new Guid("22222222-2222-2222-2222-222222222222");
        internal static readonly Guid AddedId = new Guid("33333333-3333-3333-3333-333333333333");
        internal static readonly Guid ListId = new Guid("44444444-4444-4444-4444-444444444444");
        internal static readonly Guid SelectedViewId = new Guid("55555555-5555-5555-5555-555555555555");
        private readonly MockEntryResponseProvider inner = new MockEntryResponseProvider();
        private bool failed;

        internal List<XDocument> Bodies { get; } = new List<XDocument>();
        internal List<string> Urls { get; } = new List<string>();
        internal string FailOn { get; set; }
        internal int FailureCode { get; set; } = -2146233079;
        internal int SaveCount => Bodies.Sum(body => Actions(body, "SaveWebPartChanges").Count());
        internal int AddRequestIndex => Bodies.FindIndex(body => Paths(body, "AddWebPart").Any());

        internal ViewBoundRequestFixture()
        {
            foreach (var url in new[] { RootUrl, ChildUrl })
            {
                AddResponse(url, "Web", null, Object("SP.Web", "web", "Id", GuidValue(RootWebId), "Url", RootUrl));
                AddResponse(url, null, "OpenWebById", Object("SP.Web", "child", "Id", GuidValue(ChildWebId), "Url", ChildUrl));
                var file = Object("SP.File", "file", "UniqueId", GuidValue(new Guid("66666666-6666-6666-6666-666666666666")),
                    "Exists", true, "ServerRelativeUrl", PagePath);
                AddResponse(url, null, "GetFileByServerRelativePath", file);
                AddResponse(url, null, "GetFileByServerRelativeUrl", file);
                AddResponse(url, "file", null, file);
                AddResponse(url, null, "ImportWebPart", Object("SP.WebParts.WebPartDefinition", "imported"));
                AddResponse(url, null, "AddWebPart", Object("SP.WebParts.WebPartDefinition", "added", "Id", GuidValue(AddedId)));
                AddResponse(url, null, "SaveWebPartChanges", null);
                AddResponse(url, null, "Update", null);
                AddResponse(url, null, "GetById", Object("SP.View", "view:" + AddedId, "Id", GuidValue(AddedId), "ListViewXml", "<View/>"));
            }
        }

        internal ClientContext CreateContext()
        {
            return new ClientContext(RootUrl) { WebRequestExecutorFactory = new MockWebRequestExecutorFactory(this) };
        }

        public string GetResponse(string url, string verb, string body)
        {
            var document = XDocument.Parse(body);
            Bodies.Add(document);
            Urls.Add(url);
            if (!failed && FailOn != null && Paths(document, "AddWebPart").Any()
                && (FailOn != "SaveWebPartChanges" || Actions(document, FailOn).Any()))
            {
                failed = true;
                return new JArray(new JObject
                {
                    ["SchemaVersion"] = "15.0.0.0", ["LibraryVersion"] = "16.0.27612.12000",
                    ["TraceCorrelationId"] = "77777777-7777-7777-7777-777777777777",
                    ["ErrorInfo"] = new JObject
                    {
                        ["ErrorMessage"] = "Synthetic " + FailOn + " failure",
                        ["ErrorCode"] = FailureCode, ["ErrorTypeName"] = "System.InvalidOperationException",
                        ["ErrorValue"] = null, ["ErrorDetails"] = new JObject()
                    }
                }).ToString();
            }
            var viewId = Paths(document, "GetById").SelectMany(path => path.Descendants())
                .Any(value => value.Name.LocalName == "Parameter" && Guid.TryParse(value.Value, out var id) && id == SelectedViewId)
                ? SelectedViewId : AddedId;
            foreach (var entry in inner.ResponseEntries.Where(entry => entry.Method == "GetById"))
            {
                entry.ReturnValue = Object("SP.View", "view:" + viewId, "Id", GuidValue(viewId),
                    "ListViewXml", viewId == SelectedViewId ? SelectedViewXml : "<View/>");
            }
            return inner.GetResponse(url, verb, body);
        }

        internal void AssertOneAdd(string importedXml, int expectedSaves, string zone = "wpz", int index = 0)
        {
            Assert.AreEqual(1, Bodies.Sum(body => Paths(body, "ImportWebPart").Count()), "No helper-owned import/retry.");
            Assert.AreEqual(1, Bodies.Sum(body => Paths(body, "AddWebPart").Count()), "No helper-owned duplicate Add.");
            Assert.AreEqual(expectedSaves, SaveCount);
            var request = Bodies[AddRequestIndex];
            var imported = Paths(request, "ImportWebPart").Single();
            Assert.AreEqual(importedXml, imported.Descendants().Single(value => value.Name.LocalName == "Parameter").Value,
                "The original XML, including native v2 type/assembly, must be imported unchanged.");
            var added = Paths(request, "AddWebPart").Single();
            var parameters = added.Elements().Single().Elements().ToArray();
            Assert.AreEqual(zone, parameters[1].Value);
            Assert.AreEqual(index.ToString(System.Globalization.CultureInfo.InvariantCulture), parameters[2].Value);
            if (expectedSaves == 1)
            {
                var actions = request.Root.Elements().Single(value => value.Name.LocalName == "Actions").Elements().ToList();
                var save = Actions(request, "SaveWebPartChanges").Single();
                Assert.AreEqual((string)added.Attribute("Id"), (string)save.Attribute("ObjectPathId"));
                Assert.AreNotEqual((string)imported.Attribute("Id"), (string)save.Attribute("ObjectPathId"));
                var addAction = actions.First(value => value.Name.LocalName == "ObjectPath"
                    && (string)value.Attribute("ObjectPathId") == (string)added.Attribute("Id"));
                Assert.IsTrue(actions.IndexOf(addAction) < actions.IndexOf(save), "Add must precede Save in one request.");
                Assert.IsTrue(actions.Where(value => value.Name.LocalName == "Query")
                    .All(query => actions.IndexOf(save) < actions.IndexOf(query)), "Save must precede success readback.");
            }
        }

        internal static IEnumerable<XElement> Paths(XDocument body, string name) => body.Root.Elements()
            .Single(value => value.Name.LocalName == "ObjectPaths").Elements()
            .Where(value => value.Name.LocalName == "Method" && (string)value.Attribute("Name") == name);

        internal static IEnumerable<XElement> Actions(XDocument body, string name) => body.Root.Elements()
            .Single(value => value.Name.LocalName == "Actions").Elements()
            .Where(value => value.Name.LocalName == "Method" && (string)value.Attribute("Name") == name);

        internal static string V2(string type, string assembly = "Microsoft.SharePoint.Core") =>
            "<WebPart xmlns='http://schemas.microsoft.com/WebPart/v2'><TypeName>Microsoft.SharePoint.WebPartPages."
            + type + "</TypeName><Assembly>" + assembly + ", Version=16.0.0.0</Assembly></WebPart>";

        internal static string V3(string type = "XsltListViewWebPart", string assembly = "Microsoft.SharePoint", string properties = "") =>
            "<webParts><webPart xmlns=\"http://schemas.microsoft.com/WebPart/v3\"><metaData><type name='Microsoft.SharePoint.WebPartPages."
            + type + ", " + assembly + ", Version=16.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c'/></metaData>"
            + "<data><properties>" + properties + "</properties></data></webPart></webParts>";

        private void AddResponse(string url, string property, string method, object value) =>
            inner.ResponseEntries.Add(new MockResponseEntry<object> { Url = url, PropertyName = property, Method = method, ReturnValue = value });

        private static string GuidValue(Guid value) => "/Guid(" + value + ")/";

        private static Dictionary<string, object> Object(string type, string identity, params object[] values)
        {
            var result = new Dictionary<string, object> { ["_ObjectType_"] = type, ["_ObjectIdentity_"] = identity };
            for (var i = 0; i < values.Length; i += 2) result[(string)values[i]] = values[i + 1];
            return result;
        }
    }
}
