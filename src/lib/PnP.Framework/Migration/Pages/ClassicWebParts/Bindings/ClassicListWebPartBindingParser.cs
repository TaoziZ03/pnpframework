using PnP.Framework.Migration.Diagnostics;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWebParts.Bindings
{
    public static class ClassicListWebPartBindingParser
    {
        private const string NativeV2Type = "Microsoft.SharePoint.WebPartPages.ListViewWebPart";

        public static ClassicListWebPartBindingParseResult Parse(
            ClassicWebPartSnapshot webPart,
            Guid sourcePageWebId,
            string sourcePageWebUrl,
            string sourcePageServerRelativeUrl)
        {
            if (webPart == null)
            {
                throw new ArgumentNullException(nameof(webPart));
            }

            var issues = new List<MigrationIssue>();
            XDocument document;
            try
            {
                document = ClassicWebPartMetadataParser.ReadDocument(webPart.ExportXml, LoadOptions.PreserveWhitespace);
            }
            catch (System.Xml.XmlException exception)
            {
                AddBlocker(issues, webPart.Id, "ListBindingUnavailable", "The list-bound Web Part export XML is malformed: " + exception.Message);
                return new ClassicListWebPartBindingParseResult { Issues = issues };
            }

            var isV2 = ClassicWebPartMetadataParser.IsV2Document(document);
            IDictionary<string, string> properties;
            try
            {
                if (isV2)
                {
                    properties = ReadNativeV2Properties(document);
                    var typeName = ClassicWebPartMetadataParser.ReadTypeName(webPart.ExportXml);
                    if (!string.IsNullOrWhiteSpace(webPart.TypeName)
                        && !string.Equals(webPart.TypeName.Trim(), NativeV2Type, StringComparison.Ordinal)
                        && !string.Equals(webPart.TypeName.Trim(), typeName, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("The captured type does not match the authoritative v2 TypeName/Assembly.");
                    }
                    if (!string.Equals(webPart.ExportSha256, MigrationDigest.ComputeSha256(webPart.ExportXml), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The native v2 export does not match its captured digest.");
                    }
                }
                else
                {
                    // Preserve the established v3 and namespace-free legacy binding path.
                    properties = document.Descendants()
                        .Where(element => string.Equals(element.Name.LocalName, "property", StringComparison.OrdinalIgnoreCase))
                        .Where(element => !string.IsNullOrWhiteSpace((string)element.Attribute("name")))
                        .GroupBy(element => (string)element.Attribute("name"), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is ArgumentException || exception is FileLoadException)
            {
                AddBlocker(issues, webPart.Id, "ListBindingUnavailable", exception.Message);
                return new ClassicListWebPartBindingParseResult { Issues = issues };
            }
            string listIdValue;
            properties.TryGetValue("ListId", out listIdValue);
            string listNameValue;
            properties.TryGetValue("ListName", out listNameValue);
            var declaredListId = ParseGuid(listIdValue, isV2);
            var declaredListName = ParseGuid(listNameValue, isV2);
            var listId = declaredListId ?? declaredListName;
            if (isV2 && (!declaredListId.HasValue || declaredListId == Guid.Empty || declaredListId != declaredListName))
            {
                AddBlocker(issues, webPart.Id, "ListBindingUnavailable", "The native v2 ListId/ListName must declare the same nonempty GUID.");
            }
            if (!listId.HasValue)
            {
                AddBlocker(issues, webPart.Id, "ListBindingUnavailable", "A list-bound Web Part has no parseable ListId/ListName GUID.");
            }

            string webIdValue;
            properties.TryGetValue("WebId", out webIdValue);
            var declaredWebId = ParseGuid(webIdValue, isV2);
            var sourceListWebId = !declaredWebId.HasValue || declaredWebId.Value == Guid.Empty ? sourcePageWebId : declaredWebId.Value;
            if (isV2 && (!declaredWebId.HasValue || sourceListWebId == Guid.Empty))
            {
                AddBlocker(issues, webPart.Id, "ListBindingUnavailable", "The native v2 WebId requires a valid GUID and a resolved source Web.");
            }
            string xmlDefinition;
            properties.TryGetValue("XmlDefinition", out xmlDefinition);
            xmlDefinition = xmlDefinition ?? string.Empty;
            string viewGuid;
            properties.TryGetValue("ViewGuid", out viewGuid);
            var viewId = ParseGuid(viewGuid, isV2);
            string jsLink;
            properties.TryGetValue("JSLink", out jsLink);
            string xslLink;
            properties.TryGetValue("XslLink", out xslLink);
            if (!string.IsNullOrWhiteSpace(xmlDefinition))
            {
                try
                {
                    var view = ClassicWebPartMetadataParser.ReadDocument(xmlDefinition, LoadOptions.PreserveWhitespace).Root;
                    viewId = viewId ?? ParseGuid(view == null ? null : (string)view.Attribute("Name"), isV2);
                    if (isV2 && (view?.Name != "View" || !viewId.HasValue || viewId == Guid.Empty))
                    {
                        AddBlocker(issues, webPart.Id, "ViewMappingUnavailable", "The native v2 ListViewXml requires an unqualified View with a nonempty Name GUID.");
                    }
                    jsLink = FirstNonempty(jsLink, ReadElement(view, "JSLink"));
                    xslLink = FirstNonempty(xslLink, ReadElement(view, "XslLink"));
                }
                catch (System.Xml.XmlException exception)
                {
                    AddBlocker(issues, webPart.Id, "ViewMappingUnavailable", "The embedded view definition is malformed: " + exception.Message);
                }
            }
            else
            {
                AddBlocker(issues, webPart.Id, "ViewMappingUnavailable", "The list-bound Web Part has no captured XmlDefinition/CAML view.");
            }

            if (!listId.HasValue || issues.Any(value => value.Severity == MigrationIssueSeverity.Blocker))
            {
                return new ClassicListWebPartBindingParseResult { Issues = issues };
            }

            string titleUrl;
            properties.TryGetValue("TitleUrl", out titleUrl);
            return new ClassicListWebPartBindingParseResult
            {
                Binding = new ClassicListWebPartBindingSnapshot
                {
                    SourceWebPartId = webPart.Id,
                    TypeName = isV2 ? ClassicWebPartMetadataParser.ReadTypeName(webPart.ExportXml)
                        : webPart.TypeName ?? ClassicWebPartMetadataParser.ReadTypeName(webPart.ExportXml),
                    Title = webPart.Title,
                    SourcePageWebId = sourcePageWebId,
                    SourcePageWebUrl = sourcePageWebUrl,
                    SourcePageServerRelativeUrl = sourcePageServerRelativeUrl,
                    SourceListWebId = sourceListWebId,
                    SourceListId = listId.Value,
                    SourceViewId = viewId,
                    SourceListServerRelativeUrl = ServerRelativePath(titleUrl, sourcePageWebUrl),
                    SourceTitleUrl = titleUrl,
                    XmlDefinition = xmlDefinition,
                    JsLink = NullIfEmpty(jsLink),
                    XslLink = NullIfEmpty(xslLink),
                    SourceExportSha256 = webPart.ExportSha256,
                    SourceExportXml = webPart.ExportXml
                },
                Issues = issues
            };
        }

        public static bool IsListBound(ClassicWebPartSnapshot webPart)
        {
            if (webPart == null || string.IsNullOrWhiteSpace(webPart.ExportXml))
            {
                return false;
            }
            try
            {
                var document = ClassicWebPartMetadataParser.ReadDocument(webPart.ExportXml, LoadOptions.None);
                if (ClassicWebPartMetadataParser.IsV2Document(document))
                {
                    // Candidate detection is not validation or replay permission. Malformed bindings
                    // must still reach Parse and produce an instance-scoped diagnostic.
                    return document.Root.Elements().Any(element => element.Name.Namespace == ClassicWebPartMetadataParser.V2ListView
                        || element.Name.LocalName == "ListId" || element.Name.LocalName == "ListName"
                        || (element.Name.LocalName == "TypeName" && element.Value.Trim() == NativeV2Type));
                }
                return document.Descendants()
                    .Where(element => string.Equals(element.Name.LocalName, "property", StringComparison.OrdinalIgnoreCase))
                    .Any(element => string.Equals((string)element.Attribute("name"), "ListId", StringComparison.OrdinalIgnoreCase)
                        || string.Equals((string)element.Attribute("name"), "ListName", StringComparison.OrdinalIgnoreCase));
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }

        private static IDictionary<string, string> ReadNativeV2Properties(XDocument document)
        {
            var type = ClassicWebPartMetadataParser.ReadV2Property(document, ClassicWebPartMetadataParser.V2 + "TypeName", true).Value.Trim();
            var identity = ClassicWebPartMetadataParser.ReadV2Property(document, ClassicWebPartMetadataParser.V2 + "Assembly", true).Value.Trim();
            var assembly = new AssemblyName(identity);
            var token = assembly.GetPublicKeyToken();
            // AssemblyName can normalize away or ignore extra qualifiers. Require exactly
            // the three declared identity fields, even when an extra field has its default value.
            var identityFields = identity.Split(',').Skip(1)
                .Select(field => field.Split('=')[0].Trim())
                .OrderBy(field => field, StringComparer.OrdinalIgnoreCase);
            if (type != NativeV2Type || !string.Equals(assembly.Name, "Microsoft.SharePoint.Core", StringComparison.OrdinalIgnoreCase)
                || assembly.Version != new Version(16, 0, 0, 0) || !string.IsNullOrEmpty(assembly.CultureName)
                || !identityFields.SequenceEqual(new[] { "Culture", "PublicKeyToken", "Version" }, StringComparer.OrdinalIgnoreCase)
                || token == null || !token.SequenceEqual(new byte[] { 0x71, 0xe9, 0xbc, 0xe1, 0x11, 0xe9, 0x42, 0x9c }))
            {
                throw new InvalidDataException("The v2 binding is not the supported native ListViewWebPart/Microsoft.SharePoint.Core 16.0.0.0 declaration.");
            }
            return new[] { "WebId", "ListId", "ListName", "XmlDefinition", "TitleUrl" }
                .ToDictionary(name => name, name => FindNativeV2Property(document, name)?.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        internal static XElement FindNativeV2Property(XDocument document, string name)
        {
            switch (name)
            {
                case "WebId":
                case "ListId":
                case "ListName":
                    return ClassicWebPartMetadataParser.ReadV2Property(document, ClassicWebPartMetadataParser.V2ListView + name, true);
                case "XmlDefinition":
                    return ClassicWebPartMetadataParser.ReadV2Property(document, ClassicWebPartMetadataParser.V2ListView + "ListViewXml", true);
                case "TitleUrl":
                    return ClassicWebPartMetadataParser.ReadV2Property(document, ClassicWebPartMetadataParser.V2 + "DetailLink", false);
                default:
                    // Do not synthesize v3 ViewGuid/XmlDefinition/property elements in a DWP.
                    return null;
            }
        }

        private static string ReadElement(XElement root, string name)
        {
            return root == null ? null : root.Elements().FirstOrDefault(value => string.Equals(value.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private static Guid? ParseGuid(string value, bool strict = false)
        {
            Guid result;
            var text = (value ?? string.Empty).Trim();
            return Guid.TryParse(strict ? text : text.Trim('{', '}'), out result) ? result : (Guid?)null;
        }

        private static string ServerRelativePath(string value, string sourceWebUrl)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            Uri absolute;
            if (Uri.TryCreate(value, UriKind.Absolute, out absolute))
            {
                return Uri.UnescapeDataString(absolute.AbsolutePath);
            }
            if (value.StartsWith("/", StringComparison.Ordinal))
            {
                return value;
            }
            return new Uri(sourceWebUrl).AbsolutePath.TrimEnd('/') + "/" + value.TrimStart('/');
        }

        private static string FirstNonempty(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) ? first : NullIfEmpty(second);
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void AddBlocker(ICollection<MigrationIssue> issues, Guid webPartId, string code, string message)
        {
            issues.Add(new MigrationIssue
            {
                Code = code,
                Severity = MigrationIssueSeverity.Blocker,
                Subject = "webpart:" + webPartId.ToString("D"),
                Ingredient = "ClassicWebPart.ListBinding",
                Message = message
            });
        }
    }
}
