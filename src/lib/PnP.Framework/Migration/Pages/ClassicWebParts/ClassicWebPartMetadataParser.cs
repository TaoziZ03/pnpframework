using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWebParts
{
    internal static class ClassicWebPartMetadataParser
    {
        internal static readonly XNamespace V2 = "http://schemas.microsoft.com/WebPart/v2";
        internal static readonly XNamespace V2ListView = "http://schemas.microsoft.com/WebPart/v2/ListView";

        public static string ReadTypeName(string exportXml)
        {
            if (string.IsNullOrWhiteSpace(exportXml))
            {
                return null;
            }

            try
            {
                var document = ReadDocument(exportXml, LoadOptions.None);
                if (IsV2Document(document))
                {
                    var type = ReadV2Property(document, V2 + "TypeName", true).Value.Trim();
                    var assembly = ReadV2Property(document, V2 + "Assembly", true).Value.Trim();
                    if (string.IsNullOrWhiteSpace(type) || type.IndexOf(',') >= 0
                        || string.IsNullOrWhiteSpace(assembly) || string.IsNullOrWhiteSpace(new AssemblyName(assembly).Name))
                    {
                        return null;
                    }
                    // Match the existing v3 representation without loading the declared type.
                    return type + ", " + assembly;
                }

                return document
                    .Descendants()
                    .Where(element => string.Equals(element.Name.LocalName, "type", StringComparison.OrdinalIgnoreCase))
                    .Select(element => (string)element.Attribute("name"))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }
            catch (Exception exception) when (exception is XmlException || exception is InvalidDataException
                || exception is ArgumentException || exception is FileLoadException)
            {
                return null;
            }
        }

        internal static XDocument ReadDocument(string xml, LoadOptions options)
        {
            using (var text = new StringReader(xml ?? string.Empty))
            using (var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            }))
            {
                return XDocument.Load(reader, options);
            }
        }

        internal static bool IsV2Document(XDocument document)
        {
            // Include malformed v2 root namespaces so they cannot fall through to v3 parsing.
            return document.Root != null && (document.Root.Name.LocalName == "WebPart"
                || document.Root.Name.Namespace == V2);
        }

        internal static XElement ReadV2Property(XDocument document, XName name, bool required)
        {
            if (document.Root?.Name != V2 + "WebPart")
            {
                throw new InvalidDataException("The native v2 Web Part root has an unsupported name or namespace.");
            }
            var matches = document.Root.Elements()
                .Where(element => string.Equals(element.Name.LocalName, name.LocalName, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (matches.Length == 0 && !required)
            {
                return null;
            }
            if (matches.Length != 1 || matches[0].Name != name || matches[0].HasElements
                || matches[0].Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            {
                throw new InvalidDataException("The native v2 '" + name.LocalName
                    + "' property is missing, ambiguous, malformed, or in the wrong namespace.");
            }
            return matches[0];
        }
    }
}
