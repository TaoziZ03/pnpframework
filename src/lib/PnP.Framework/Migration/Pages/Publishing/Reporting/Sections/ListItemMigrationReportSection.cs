using PnP.Framework.Migration.Lists.Capture;
using System;
using System.Linq;
using static PnP.Framework.Migration.Pages.Publishing.Reporting.Sections.MigrationReportSectionFormatter;

namespace PnP.Framework.Migration.Pages.Publishing.Reporting.Sections
{
    internal static class ListItemMigrationReportSection
    {
        public static void Append(MarkdownReportWriter writer, ListDependencySnapshot source)
        {
            writer.Heading(4, $"Current items, folders, files, and attachments ({source.Items.Count})");
            writer.Paragraph("This is current-state evidence only. Source item IDs are mapped to target-generated IDs; version history and Created/Modified/Author/Editor replay are outside the current contract.");
            writer.Table(null, new[] { "Source item ID", "Source unique ID", "Availability", "Document object", "Values", "Attachments", "Diagnostics" },
                source.Items.OrderBy(value => value.SourceItemId).Select(value => Row(
                    value.SourceItemId,
                    value.SourceUniqueId,
                    value.Availability,
                    value.Document == null ? null : $"{value.Document.Kind}:{value.Document.ServerRelativeUrl}",
                    value.Values.Count,
                    value.Attachments.Count,
                    Join(value.Diagnostics))));
            foreach (var item in source.Items.OrderBy(value => value.SourceItemId))
            {
                writer.Heading(5, "Item " + item.SourceItemId);
                if (item.Document != null)
                {
                    writer.Table(null, new[] { "Document property", "Value", "How to read it" }, new[]
                    {
                        Row("kind", item.Document.Kind, "Folder creates hierarchy; File materializes exact current bytes."),
                        Row("name", item.Document.Name, "Leaf name."),
                        Row("serverRelativeUrl", item.Document.ServerRelativeUrl, "Mapped relative to the source and target List roots."),
                        Row("length", item.Document.Length, "Source byte count for files."),
                        Row("majorVersion / minorVersion", $"{item.Document.MajorVersion} / {item.Document.MinorVersion}", "Captured evidence; version history is not replayed."),
                        Row("informationProtection.state", item.Document.InformationProtection?.State, "Protection is classified from item metadata before any binary request."),
                        Row("informationProtection.labelId", item.Document.InformationProtection?.LabelId, "Captured label relationship evidence; it is not proof that the target recognizes the same label."),
                        Row("captureDecision", item.Document.CaptureDecision == null ? null : $"{item.Document.CaptureDecision.Disposition}; policy={item.Document.CaptureDecision.PolicyId}; reason={item.Document.CaptureDecision.ReasonCode}; digest={item.Document.CaptureDecision.DecisionDigest}", "MetadataOnly proves the capture gate did not request or persist bytes."),
                        Row("content", FormatArtifact(item.Document.Content), "Exact bytes may be inline Base64 or content-addressed in the artifact store.")
                    });
                }
                writer.Table(null, new[] { "Internal name", "Kind", "Typed value", "Raw runtime type", "Raw text", "Raw JSON", "Availability", "Diagnostics" },
                    item.Values.OrderBy(value => value.InternalName, StringComparer.OrdinalIgnoreCase).Select(value => Row(
                        value.InternalName,
                        value.Kind,
                        SummarizeListItemValue(value),
                        value.RawType,
                        Summarize(value.RawValue),
                        Summarize(value.RawValueJson),
                        value.Availability,
                        Join(value.Diagnostics))));
                writer.Table(null, new[] { "Attachment file", "Source path", "Content", "Availability / diagnostics" },
                    item.Attachments.OrderBy(value => value.FileName, StringComparer.OrdinalIgnoreCase).Select(value => Row(
                        value.FileName,
                        value.ServerRelativeUrl,
                        FormatArtifact(value.Content),
                        value.Content == null ? null : $"{value.Content.Availability}; {Join(value.Content.Diagnostics)}")));
            }
        }
    }
}
