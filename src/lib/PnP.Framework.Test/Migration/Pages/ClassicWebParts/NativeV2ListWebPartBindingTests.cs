using Microsoft.VisualStudio.TestTools.UnitTesting;
using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Lists.Planning;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.ClassicWebParts.Bindings;
using PnP.Framework.Migration.Pages.ClassicWebParts.Planning;
using PnP.Framework.Migration.Pages.Content;
using PnP.Framework.Test.Utilities.WebParts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PnP.Framework.Test.Migration.Pages.ClassicWebParts
{
    [TestClass]
    public class NativeV2ListWebPartBindingTests
    {
        private static readonly XNamespace V2 = "http://schemas.microsoft.com/WebPart/v2";
        private static readonly XNamespace ListView = "http://schemas.microsoft.com/WebPart/v2/ListView";
        private static readonly Guid WebPartId = new Guid("cb9adc96-a41f-478f-b48d-e10d845d3752");
        private static readonly Guid SourceListId = new Guid("edd50b31-96c8-4ab0-b607-45bdcb34f9b5");
        // Synthetic Web/target context: the captured WebId=Empty means the supplied page Web.
        private static readonly Guid PageWebId = new Guid("11111111-1111-1111-1111-111111111111");
        private const string PageWebUrl = "https://microsoft.sharepoint.com/sites/DevCenter/Learn";
        private const string PagePath = "/sites/DevCenter/Learn/Pages/App-publishing-calendar.aspx";
        private const string NativeType = "Microsoft.SharePoint.WebPartPages.ListViewWebPart";

        [TestMethod]
        public void ExactV2ProbeParsesAuthoritativeTypeAndBinding()
        {
            Assert.AreEqual(NativeV2Export.Sha256, MigrationDigest.ComputeSha256(NativeV2Export.Xml));
            var root = XElement.Parse(NativeV2Export.Xml);
            Assert.AreEqual(NativeType + ", " + root.Element(V2 + "Assembly").Value,
                ClassicWebPartMetadataParser.ReadTypeName(NativeV2Export.Xml));
            Assert.IsTrue(ClassicListWebPartBindingParser.IsListBound(Capture()));
            var binding = Binding();
            Assert.AreEqual(SourceListId, binding.SourceListId);
            Assert.AreEqual(PageWebId, binding.SourceListWebId);
            Assert.AreEqual(WebPartId, binding.SourceViewId);
            Assert.AreEqual(root.Element(ListView + "ListViewXml").Value, binding.XmlDefinition);
            Assert.AreEqual(root.Element(V2 + "DetailLink").Value, binding.SourceTitleUrl);
            Assert.AreEqual(binding.SourceTitleUrl, binding.SourceListServerRelativeUrl);
            Assert.AreEqual(NativeV2Export.Sha256, binding.SourceExportSha256);
        }

        [TestMethod]
        public void ExactV2ProbeRebindsInPlaceWithoutChangingNativeClassOrUnrelatedXml()
        {
            // This explicit binding also probes the rewriter independently of the parser.
            var binding = DeclaredBinding();
            var map = Target();
            var rewritten = ClassicListWebPartRewriter.Rewrite(binding, map);
            AssertRebound(rewritten.ExportXml, map);
            Assert.AreEqual(MigrationDigest.ComputeSha256(rewritten.ExportXml), rewritten.ExportSha256);
            Assert.AreEqual(WebPartId, rewritten.SourceWebPartId);
            Assert.AreEqual(rewritten.ExportXml, ClassicListWebPartRewriter.Rewrite(binding, map).ExportXml);
            Assert.AreEqual(NativeV2Export.Xml, binding.SourceExportXml, "Do not mutate captured evidence.");
            Assert.AreEqual(NativeV2Export.Sha256, binding.SourceExportSha256);

            var before = XElement.Parse(NativeV2Export.Xml);
            var after = XElement.Parse(rewritten.ExportXml);
            var oldView = XElement.Parse(before.Element(ListView + "ListViewXml").Value);
            var newView = XElement.Parse(after.Element(ListView + "ListViewXml").Value);
            foreach (var view in new[] { oldView, newView })
            {
                view.Attribute("Name").Remove();
                view.Attribute("Url").Remove();
            }
            Assert.IsTrue(XNode.DeepEquals(oldView, newView), "Calendar CAML and flags must survive.");
            var changed = new[] { ListView + "WebId", ListView + "ListId", ListView + "ListName",
                ListView + "ListViewXml", V2 + "DetailLink" };
            foreach (var root in new[] { before, after })
            {
                root.Elements().Where(element => changed.Contains(element.Name)).Remove();
            }
            Assert.IsTrue(XNode.DeepEquals(before, after), "Unrelated properties/namespaces must survive.");
        }

        [TestMethod]
        public void PrefixesCommentsAndCdataDoNotChangeTheV2Binding()
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            root.SetAttributeValue(XNamespace.Xmlns + "p", V2.NamespaceName);
            root.SetAttributeValue(XNamespace.Xmlns + "lv", ListView.NamespaceName);
            root.AddFirst(new XComment("preserve this unrelated comment"));
            var definition = root.Element(ListView + "ListViewXml");
            definition.ReplaceNodes(new XCData(definition.Value));
            var xml = root.ToString(SaveOptions.DisableFormatting);
            var parsed = Parse(Capture(xml));
            Assert.IsTrue(parsed.IsExecutable, Issues(parsed));
            var rewritten = ClassicListWebPartRewriter.Rewrite(parsed.Binding, Target());
            AssertRebound(rewritten.ExportXml, Target());
            StringAssert.Contains(rewritten.ExportXml, "<!--preserve this unrelated comment-->");
        }

        public static IEnumerable<object[]> InvalidPropertyShapes()
        {
            foreach (var property in new[] { "TypeName", "Assembly", "WebId", "ListId", "ListName", "ListViewXml", "DetailLink" })
            {
                foreach (var shape in new[] { "absent", "duplicate", "foreign-namespace", "nested", "nil" })
                {
                    if (property != "DetailLink" || shape != "absent")
                    {
                        yield return new object[] { property, shape };
                    }
                }
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(InvalidPropertyShapes), DynamicDataSourceType.Method)]
        public void MissingDuplicateMalformedOrWrongNamespacePropertyFailsClosed(string property, string shape)
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            var node = root.Elements().Single(value => value.Name.LocalName == property);
            switch (shape)
            {
                case "absent": node.Remove(); break;
                case "duplicate": node.AddAfterSelf(new XElement(node)); break;
                case "foreign-namespace":
                    node.Name = XName.Get(property, "urn:foreign");
                    node.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
                    break;
                case "nested": node.ReplaceNodes(new XElement("nested", node.Value)); break;
                case "nil": node.SetAttributeValue(XName.Get("nil", "http://www.w3.org/2001/XMLSchema-instance"), "true"); break;
                default: Assert.Fail(shape); break;
            }
            AssertInvalidXml(root.ToString(SaveOptions.DisableFormatting));
        }

        [DataTestMethod]
        [DataRow("TypeName", "Contoso.ListViewWebPart")]
        [DataRow("TypeName", "Microsoft.SharePoint.WebPartPages.ListViewWebPartSpoof")]
        [DataRow("TypeName", "Microsoft.SharePoint.WebPartPages.XsltListViewWebPart")]
        [DataRow("TypeName", "Microsoft.SharePoint.WebPartPages.ContentEditorWebPart")]
        [DataRow("Assembly", "Contoso.Microsoft.SharePoint.Core, Version=16.0.0.0")]
        [DataRow("Assembly", "Microsoft.SharePoint.Portal, Version=16.0.0.0")]
        [DataRow("Assembly", "Microsoft.SharePoint.Core")]
        [DataRow("Assembly", "Microsoft.SharePoint.Core, Version=invalid")]
        [DataRow("Assembly", "Microsoft.SharePoint.Core, Version=15.0.0.0, Culture=neutral, PublicKeyToken=71e9bce111e9429c")]
        [DataRow("Assembly", "Microsoft.SharePoint.Core, Version=16.0.0.0, Culture=fr-FR, PublicKeyToken=71e9bce111e9429c")]
        [DataRow("Assembly", "Microsoft.SharePoint.Core, Version=16.0.0.0, Culture=neutral, PublicKeyToken=null")]
        [DataRow("Assembly", "Microsoft.SharePoint.Core, Version=16.0.0.0, Culture=neutral, PublicKeyToken=0000000000000000")]
        [DataRow("WebId", "not-a-guid")]
        [DataRow("ListId", "not-a-guid")]
        [DataRow("ListId", "{{edd50b31-96c8-4ab0-b607-45bdcb34f9b5}}")]
        [DataRow("ListName", "not-a-guid")]
        [DataRow("ListId", "00000000-0000-0000-0000-000000000000")]
        [DataRow("ListName", "{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}")]
        [DataRow("ListViewXml", "<View")]
        [DataRow("ListViewXml", "<NotView Name='{CB9ADC96-A41F-478F-B48D-E10D845D3752}' />")]
        [DataRow("ListViewXml", "<View xmlns='urn:foreign' Name='{CB9ADC96-A41F-478F-B48D-E10D845D3752}' />")]
        [DataRow("ListViewXml", "<View />")]
        [DataRow("ListViewXml", "<View Name='not-a-guid' />")]
        [DataRow("ListViewXml", "<View Name='{{CB9ADC96-A41F-478F-B48D-E10D845D3752}}' />")]
        [DataRow("ListViewXml", "<View Name='00000000-0000-0000-0000-000000000000' />")]
        public void InvalidTypeAssemblyOrBindingValueFailsClosed(string property, string value)
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            root.Elements().Single(node => node.Name.LocalName == property).Value = value;
            AssertInvalidXml(root.ToString(SaveOptions.DisableFormatting));
        }

        [DataTestMethod]
        [DataRow("Retargetable=Yes")]
        [DataRow("ContentType=WindowsRuntime")]
        [DataRow("ProcessorArchitecture=MSIL")]
        public void UnsupportedNativeV2AssemblyQualifiersFailClosed(string qualifier)
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            root.Element(V2 + "Assembly").Value += ", " + qualifier;
            AssertInvalidXml(root.ToString(SaveOptions.DisableFormatting));
        }

        [DataTestMethod]
        [DataRow("Retargetable=No")]
        [DataRow("ContentType=Default")]
        [DataRow("ProcessorArchitecture=None")]
        [DataRow("Flags=None")]
        [DataRow("Unknown=ignored")]
        [DataRow("Version=16.0.0.0")]
        [DataRow("Culture=neutral")]
        [DataRow("PublicKeyToken=71e9bce111e9429c")]
        public void DefaultUnknownOrDuplicateNativeV2AssemblyFieldsFailClosed(string field)
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            root.Element(V2 + "Assembly").Value += ", " + field;
            AssertInvalidXml(root.ToString(SaveOptions.DisableFormatting));
        }

        [DataTestMethod]
        [DataRow("Version")]
        [DataRow("Culture")]
        [DataRow("PublicKeyToken")]
        public void MissingExplicitNativeV2AssemblyIdentityFieldFailsClosed(string field)
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            var assembly = root.Element(V2 + "Assembly");
            assembly.Value = string.Join(",", assembly.Value.Split(',')
                .Where(value => !value.TrimStart().StartsWith(field + "=", StringComparison.Ordinal)));
            AssertInvalidXml(root.ToString(SaveOptions.DisableFormatting));
        }

        [DataTestMethod]
        [DataRow("microsoft.sharepoint.core, publickeytoken=71E9BCE111E9429C, culture=neutral, version=16.0.0.0")]
        [DataRow("  Microsoft.SharePoint.Core  ,  Version = 16.0.0.0 , Culture = neutral , PublicKeyToken = 71e9bce111e9429c  ")]
        public void NativeV2AssemblyIdentityRetainsCaseOrderAndWhitespaceCompatibility(string identity)
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            root.Element(V2 + "Assembly").Value = identity;
            var result = Parse(Capture(root.ToString(SaveOptions.DisableFormatting)));
            Assert.IsTrue(result.IsExecutable, Issues(result));
            var rewritten = XElement.Parse(ClassicListWebPartRewriter.Rewrite(result.Binding, Target()).ExportXml);
            Assert.AreEqual(identity, rewritten.Element(V2 + "Assembly").Value);
        }

        [TestMethod]
        public void WrongRootNamespaceDtdAndDigestMismatchFailClosed()
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            root.Name = XName.Get("WebPart", "urn:foreign");
            root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
            AssertInvalidXml(root.ToString(SaveOptions.DisableFormatting));
            AssertInvalidXml("<!DOCTYPE WebPart [<!ENTITY title 'untrusted'>]>"
                + XElement.Parse(NativeV2Export.Xml).ToString(SaveOptions.DisableFormatting).Replace("App Publishing Calendar", "&title;"));
            var captured = Capture();
            captured.ExportSha256 = new string('0', 64);
            Assert.IsFalse(Parse(captured).IsExecutable);
            var binding = DeclaredBinding();
            binding.SourceExportSha256 = captured.ExportSha256;
            Assert.ThrowsException<InvalidDataException>(() => ClassicListWebPartRewriter.Rewrite(binding, Target()));
        }

        [TestMethod]
        public void CapturedTypeCannotOverrideAuthoritativeV2Metadata()
        {
            var captured = Capture();
            captured.TypeName = "Contoso.ListViewWebPart";
            Assert.IsFalse(Parse(captured).IsExecutable);
            captured.TypeName = NativeType;
            Assert.IsTrue(Parse(captured).IsExecutable, "A matching unqualified inventory type is compatible.");
        }

        [TestMethod]
        public void NonListV2MetadataIsReadWithoutAcquiringAListBinding()
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            root.Elements().Where(element => element.Name.Namespace == ListView).Remove();
            root.Element(V2 + "TypeName").Value = "Microsoft.SharePoint.WebPartPages.ContentEditorWebPart";
            var captured = Capture(root.ToString(SaveOptions.DisableFormatting));
            Assert.IsFalse(ClassicListWebPartBindingParser.IsListBound(captured));
            Assert.AreEqual(root.Element(V2 + "TypeName").Value + ", " + root.Element(V2 + "Assembly").Value,
                ClassicWebPartMetadataParser.ReadTypeName(captured.ExportXml));
            Assert.IsFalse(Parse(captured).IsExecutable, "Metadata discovery is not native list-view replay permission.");
        }

        [DataTestMethod]
        [DataRow("source-web")]
        [DataRow("source-list")]
        [DataRow("source-view")]
        [DataRow("target-web")]
        [DataRow("target-list")]
        [DataRow("target-view-empty")]
        [DataRow("target-view-missing")]
        [DataRow("target-page")]
        [DataRow("target-list-url")]
        public void UnrelatedOrIncompleteTargetMapCannotRebind(string field)
        {
            var map = Target();
            switch (field)
            {
                case "source-web": map.SourceWebId = map.TargetWebId; break;
                case "source-list": map.SourceListId = map.TargetListId; break;
                case "source-view": map.SourceViewId = map.TargetViewId; break;
                case "target-web": map.TargetWebId = Guid.Empty; break;
                case "target-list": map.TargetListId = Guid.Empty; break;
                case "target-view-empty": map.TargetViewId = Guid.Empty; break;
                case "target-view-missing": map.TargetViewId = null; break;
                case "target-page": map.TargetPageServerRelativeUrl = null; break;
                case "target-list-url": map.TargetListServerRelativeUrl = null; break;
            }
            Assert.ThrowsException<ArgumentException>(() => ClassicListWebPartRewriter.Rewrite(Binding(), map));
        }

        [DataTestMethod]
        [DataRow("list")]
        [DataRow("view")]
        [DataRow("web")]
        public void DetachedBindingCannotOverrideCapturedIds(string field)
        {
            var binding = Binding();
            var map = Target();
            if (field == "list") { binding.SourceListId = map.TargetListId; map.SourceListId = binding.SourceListId; }
            if (field == "view") { binding.SourceViewId = map.TargetViewId; map.SourceViewId = binding.SourceViewId; }
            if (field == "web") { binding.SourceListWebId = map.TargetWebId; map.SourceWebId = binding.SourceListWebId; }
            Assert.ThrowsException<InvalidDataException>(() => ClassicListWebPartRewriter.Rewrite(binding, map));
        }

        [TestMethod]
        public void ExistingComposerRequiresListAndViewSupplierReceiptsForV2()
        {
            var captured = Capture();
            var binding = Binding();
            var map = Target();
            var action = new ClassicWebPartAction
            {
                SourceWebPartId = WebPartId,
                Disposition = ClassicWebPartDisposition.RebindListAfterMaterialization
            };
            string Compose(ListMaterializationReceipt receipt) => ClassicWebPartReplayComposer.Compose(
                captured, action, binding, receipt, map.TargetPageServerRelativeUrl, Array.Empty<PageTextReplacement>());
            var receipt = new ListMaterializationReceipt
            {
                SourceWebId = PageWebId, SourceListId = SourceListId,
                TargetWebId = map.TargetWebId, TargetListId = map.TargetListId,
                TargetRootFolderServerRelativeUrl = map.TargetListServerRelativeUrl,
                TargetViewIds = new Dictionary<Guid, Guid> { [WebPartId] = map.TargetViewId.Value }
            };
            AssertRebound(Compose(receipt), map);
            Assert.ThrowsException<InvalidDataException>(() => Compose(null));
            receipt.SourceListId = map.TargetListId;
            Assert.ThrowsException<InvalidDataException>(() => Compose(receipt));
            receipt.SourceListId = SourceListId;
            receipt.SourceWebId = map.TargetWebId;
            Assert.ThrowsException<InvalidDataException>(() => Compose(receipt));
            receipt.SourceWebId = PageWebId;
            receipt.TargetViewIds.Clear();
            receipt.TargetViewIds[map.TargetViewId.Value] = map.TargetViewId.Value;
            Assert.ThrowsException<InvalidDataException>(() => Compose(receipt));
            action.Disposition = ClassicWebPartDisposition.Block;
            Assert.ThrowsException<InvalidOperationException>(() => Compose(receipt));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("<html><body>Access Denied</body></html>")]
        [DataRow("Unauthorized")]
        public void UnavailableOrDeniedInstanceIsBlockedWhileIndependentPartContinues(string unavailable)
        {
            var denied = Capture(unavailable, unavailableValue: true);
            var parsed = Parse(denied);
            Assert.IsFalse(parsed.IsExecutable);
            Assert.IsTrue(parsed.Issues.All(issue => issue.Subject == "webpart:" + WebPartId.ToString("D")));
            var independent = new ClassicWebPartSnapshot
            {
                Id = PageWebId,
                ExportXml = "<webParts><webPart xmlns='http://schemas.microsoft.com/WebPart/v3'><metaData>"
                    + "<type name='Microsoft.SharePoint.WebPartPages.ContentEditorWebPart, Microsoft.SharePoint'/>"
                    + "</metaData><data><properties/></data></webPart></webParts>"
            };
            var actions = ClassicWebPartActionPlanner.Build(new[] { denied, independent }, null, null, new List<string>());
            Assert.AreEqual(ClassicWebPartDisposition.Block, actions.Single(value => value.SourceWebPartId == WebPartId).Disposition);
            Assert.AreEqual(ClassicWebPartDisposition.CopyCaptured, actions.Single(value => value.SourceWebPartId == PageWebId).Disposition);
            Assert.IsFalse(ClassicListWebPartBindingParser.IsListBound(independent));
            Assert.AreEqual("Microsoft.SharePoint.WebPartPages.ContentEditorWebPart, Microsoft.SharePoint",
                ClassicWebPartMetadataParser.ReadTypeName(independent.ExportXml));
        }

        [TestMethod]
        public void ParserPrerequisiteDoesNotLiftNativeV2ReplayPolicy()
        {
            Assert.IsNotNull(ClassicWebPartReplayCapabilityPolicy.GetBlocker(NativeV2Export.Xml));
            var actions = ClassicWebPartActionPlanner.Build(new[] { Capture() }, new[] { Binding() }, null, new List<string>());
            Assert.AreEqual(ClassicWebPartDisposition.Block, actions.Single().Disposition);
        }

        private static ClassicWebPartSnapshot Capture(string xml = null, bool unavailableValue = false)
        {
            xml = unavailableValue ? xml : xml ?? NativeV2Export.Xml;
            return new ClassicWebPartSnapshot
            {
                Id = WebPartId, Title = "App Publishing Calendar", ZoneId = "wpz", ZoneIndex = 0,
                ExportXml = xml, ExportSha256 = xml == null ? null : MigrationDigest.ComputeSha256(xml)
            };
        }

        private static ClassicListWebPartBindingParseResult Parse(ClassicWebPartSnapshot captured)
        {
            return ClassicListWebPartBindingParser.Parse(captured, PageWebId, PageWebUrl, PagePath);
        }

        private static ClassicListWebPartBindingSnapshot Binding()
        {
            var result = Parse(Capture());
            Assert.IsTrue(result.IsExecutable, Issues(result));
            return result.Binding;
        }

        private static ClassicListWebPartBindingSnapshot DeclaredBinding()
        {
            var root = XElement.Parse(NativeV2Export.Xml);
            return new ClassicListWebPartBindingSnapshot
            {
                SourceWebPartId = WebPartId, SourceListWebId = PageWebId, SourcePageWebId = PageWebId,
                SourceListId = SourceListId, SourceViewId = WebPartId,
                SourcePageWebUrl = PageWebUrl, SourcePageServerRelativeUrl = PagePath,
                TypeName = NativeType + ", " + root.Element(V2 + "Assembly").Value,
                SourceTitleUrl = root.Element(V2 + "DetailLink").Value,
                SourceListServerRelativeUrl = root.Element(V2 + "DetailLink").Value,
                XmlDefinition = root.Element(ListView + "ListViewXml").Value,
                SourceExportXml = NativeV2Export.Xml, SourceExportSha256 = NativeV2Export.Sha256
            };
        }

        private static ClassicListWebPartTargetMap Target()
        {
            return new ClassicListWebPartTargetMap
            {
                SourceWebId = PageWebId, SourceListId = SourceListId, SourceViewId = WebPartId,
                TargetWebId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                TargetListId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                TargetViewId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                TargetListServerRelativeUrl = "/sites/offline-fixture/Lists/Calendar",
                TargetPageServerRelativeUrl = "/sites/offline-fixture/Pages/Calendar.aspx"
            };
        }

        private static void AssertInvalidXml(string xml)
        {
            var result = Parse(Capture(xml));
            Assert.IsFalse(result.IsExecutable, xml);
            Assert.IsNull(result.Binding);
            Assert.IsTrue(result.Issues.Any(issue => issue.Severity == MigrationIssueSeverity.Blocker));
            var binding = DeclaredBinding();
            binding.SourceExportXml = xml;
            binding.SourceExportSha256 = MigrationDigest.ComputeSha256(xml);
            Assert.ThrowsException<InvalidDataException>(() => ClassicListWebPartRewriter.Rewrite(binding, Target()));
            Assert.IsTrue(Parse(Capture()).IsExecutable, "One invalid instance must not poison later parsing.");
        }

        private static void AssertRebound(string xml, ClassicListWebPartTargetMap map)
        {
            var root = XElement.Parse(xml);
            Assert.AreEqual(V2 + "WebPart", root.Name);
            Assert.AreEqual(NativeType, root.Element(V2 + "TypeName").Value);
            Assert.AreEqual(XElement.Parse(NativeV2Export.Xml).Element(V2 + "Assembly").Value,
                root.Element(V2 + "Assembly").Value);
            Assert.AreEqual(map.TargetWebId, Guid.Parse(root.Element(ListView + "WebId").Value));
            Assert.AreEqual(map.TargetListId, Guid.Parse(root.Element(ListView + "ListId").Value));
            Assert.AreEqual(map.TargetListId, Guid.Parse(root.Element(ListView + "ListName").Value));
            var view = XElement.Parse(root.Element(ListView + "ListViewXml").Value);
            Assert.AreEqual(map.TargetViewId, Guid.Parse((string)view.Attribute("Name")));
            Assert.AreEqual(map.TargetPageServerRelativeUrl, (string)view.Attribute("Url"));
            Assert.AreEqual(map.TargetListServerRelativeUrl, root.Element(V2 + "DetailLink").Value);
            Assert.IsFalse(root.Descendants().Any(element => element.Name.LocalName == "property"));
        }

        private static string Issues(ClassicListWebPartBindingParseResult result)
        {
            return string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Code + ": " + issue.Message));
        }
    }
}
