using Microsoft.SharePoint.Client.WebParts;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace PnP.Framework.Utilities.WebParts
{
    /// <summary>
    /// Queues persistence of the view bindings created when a native list-view Web Part is added.
    /// This is not a migration replay capability check.
    /// </summary>
    internal static class ViewBoundWebPartPersistence
    {
        private static readonly XNamespace V2 = "http://schemas.microsoft.com/WebPart/v2";
        private static readonly XNamespace V3 = "http://schemas.microsoft.com/WebPart/v3";

        internal static void QueueSaveIfRequired(WebPartDefinition addedDefinition, string importedXml)
        {
            if (addedDefinition == null)
            {
                throw new ArgumentNullException(nameof(addedDefinition));
            }

            if (RequiresSave(importedXml))
            {
                // Use the returned AddWebPart object, in its originating request. The caller owns
                // execution/retry, and any XLV hidden-view restoration must happen after this save.
                addedDefinition.SaveWebPartChanges();
            }
        }

        internal static bool RequiresSave(string importedXml)
        {
            if (string.IsNullOrWhiteSpace(importedXml))
            {
                return false;
            }

            XElement root;
            try
            {
                using (var text = new StringReader(importedXml))
                using (var reader = XmlReader.Create(text, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                }))
                {
                    root = XDocument.Load(reader).Root;
                }
            }
            catch (XmlException)
            {
                // ImportWebPart retains responsibility for invalid/unknown public input.
                return false;
            }

            if (root?.Name == V2 + "WebPart")
            {
                return IsNativeListViewType(
                    SingleChild(root, V2 + "TypeName")?.Value,
                    SingleChild(root, V2 + "Assembly")?.Value);
            }

            if (root?.Name == "webParts")
            {
                var parts = root.Elements().Take(2).ToArray();
                root = parts.Length == 1 ? parts[0] : null;
            }

            if (root?.Name != V3 + "webPart")
            {
                return false;
            }

            var metadata = SingleChild(root, V3 + "metaData");
            var typeName = (string)SingleChild(metadata, V3 + "type")?.Attribute("name");
            var separator = typeName?.IndexOf(',') ?? -1;
            return separator > 0 && IsNativeListViewType(
                typeName.Substring(0, separator), typeName.Substring(separator + 1));
        }

        private static XElement SingleChild(XElement parent, XName name)
        {
            var children = parent?.Elements(name).Take(2).ToArray();
            return children?.Length == 1 ? children[0] : null;
        }

        private static bool IsNativeListViewType(string typeName, string assemblyIdentity)
        {
            typeName = typeName?.Trim();
            if ((typeName != "Microsoft.SharePoint.WebPartPages.ListViewWebPart"
                && typeName != "Microsoft.SharePoint.WebPartPages.XsltListViewWebPart")
                || string.IsNullOrWhiteSpace(assemblyIdentity))
            {
                return false;
            }

            try
            {
                // Parse the declaration only; never load an assembly or resolve a runtime type.
                var name = new AssemblyName(assemblyIdentity.Trim()).Name;
                return string.Equals(name, "Microsoft.SharePoint", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Microsoft.SharePoint.Core", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FileLoadException)
            {
                return false;
            }
        }
    }
}
