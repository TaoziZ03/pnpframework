using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Lists.Items.Protection;
using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;

namespace PnP.Framework.Migration.Lists.Items
{
    internal static class ListItemSnapshotReader
    {
        public static IList<ListItemSnapshot> Read(
            ClientContext context,
            List list,
            long maximumBytes,
            IMigrationArtifactStore artifactStore,
            ProtectedAssetCapturePolicy protectedAssetPolicy,
            ICollection<string> warnings)
        {
            var result = new List<ListItemSnapshot>();
            ListItemCollectionPosition position = null;
            do
            {
                var page = list.GetItems(new CamlQuery
                {
                    ViewXml = BuildViewXml(list),
                    ListItemCollectionPosition = position
                });
                context.Load(page);
                context.ExecuteQueryRetry();
                if (list.BaseType == BaseType.DocumentLibrary)
                {
                    foreach (var item in page)
                    {
                        if (item.FileSystemObjectType == FileSystemObjectType.File)
                        {
                            context.Load(item.File, value => value.Name, value => value.ServerRelativeUrl, value => value.Length, value => value.MajorVersion, value => value.MinorVersion);
                        }
                        else
                        {
                            context.Load(item.Folder, value => value.Name, value => value.ServerRelativeUrl);
                        }
                    }
                    context.ExecuteQueryRetry();
                }
                if (list.EnableAttachments)
                {
                    foreach (var item in page.Where(HasAttachments))
                    {
                        context.Load(item.AttachmentFiles, values => values.Include(value => value.FileName, value => value.ServerRelativeUrl));
                    }
                    context.ExecuteQueryRetry();
                }

                foreach (var item in page)
                {
                    var snapshot = new ListItemSnapshot
                    {
                        SourceItemId = item.Id,
                        SourceUniqueId = ReadUniqueId(item),
                        Values = item.FieldValues.OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(value => ListItemValueSerializer.Serialize(value.Key, value.Value)).ToList(),
                        Attachments = CaptureAttachments(context, item, maximumBytes, artifactStore),
                        Document = list.BaseType == BaseType.DocumentLibrary
                            ? CaptureDocument(context, item, maximumBytes, artifactStore, protectedAssetPolicy)
                            : null
                    };
                    var unavailableBinary = snapshot.Attachments.Any(value => value.Content == null || value.Content.Availability != EvidenceAvailability.Captured)
                        || (snapshot.Document != null && snapshot.Document.Kind == ListDocumentObjectKind.File
                            && snapshot.Document.CaptureDecision?.IsMetadataOnly != true
                            && (snapshot.Document.Content == null || snapshot.Document.Content.Availability != EvidenceAvailability.Captured));
                    if (unavailableBinary || snapshot.Values.Any(value => value.Availability != EvidenceAvailability.Captured))
                    {
                        snapshot.Availability = EvidenceAvailability.Partial;
                    }
                    if (unavailableBinary)
                    {
                        warnings.Add("List item " + item.Id + " has document or attachment bytes that could not be captured exactly.");
                    }
                    if (snapshot.Document?.CaptureDecision?.IsMetadataOnly == true)
                    {
                        warnings.Add("List item " + item.Id + " retained document metadata only under protected-asset policy '"
                            + snapshot.Document.CaptureDecision.PolicyId + "'; no binary request was made.");
                    }
                    result.Add(snapshot);
                }
                position = page.ListItemCollectionPosition;
            }
            while (position != null);

            return result.OrderBy(value => value.SourceItemId).ToList();
        }

        private static IList<ListAttachmentSnapshot> CaptureAttachments(ClientContext context, ListItem item, long maximumBytes, IMigrationArtifactStore artifactStore)
        {
            if (!HasAttachments(item))
            {
                return new List<ListAttachmentSnapshot>();
            }
            return item.AttachmentFiles.AsEnumerable().Select(attachment =>
            {
                var file = context.Web.GetFileByServerRelativePath(ResourcePath.FromDecodedUrl(Uri.UnescapeDataString(attachment.ServerRelativeUrl)));
                return new ListAttachmentSnapshot
                {
                    FileName = attachment.FileName,
                    ServerRelativeUrl = attachment.ServerRelativeUrl,
                    Content = ListBinaryArtifactReader.Read(context, file, maximumBytes, artifactStore, "application/octet-stream", attachment.FileName)
                };
            }).ToList();
        }

        private static ListDocumentSnapshot CaptureDocument(
            ClientContext context,
            ListItem item,
            long maximumBytes,
            IMigrationArtifactStore artifactStore,
            ProtectedAssetCapturePolicy protectedAssetPolicy)
        {
            if (item.FileSystemObjectType == FileSystemObjectType.Folder)
            {
                return new ListDocumentSnapshot
                {
                    Kind = ListDocumentObjectKind.Folder,
                    Name = item.Folder.Name,
                    ServerRelativeUrl = item.Folder.ServerRelativeUrl
                };
            }
            var informationProtection = ListDocumentInformationProtectionSnapshotReader.Read(item.FieldValues);
            ProtectedAssetCaptureDecision captureDecision;
            var content = ProtectedAssetCaptureGate.Capture(
                informationProtection,
                protectedAssetPolicy,
                () => ListBinaryArtifactReader.Read(
                    context,
                    item.File,
                    maximumBytes,
                    artifactStore,
                    ListBinaryArtifactReader.MediaType(item.File.Name),
                    item.File.Name),
                out captureDecision);
            if (content?.Artifact != null && item.File.Length != content.Artifact.Length)
            {
                content.Availability = EvidenceAvailability.Partial;
                content.Diagnostics.Add("DocumentMetadataLengthMismatch: metadataLength=" + item.File.Length
                    + "; payloadLength=" + content.Artifact.Length + ".");
            }
            return new ListDocumentSnapshot
            {
                Kind = ListDocumentObjectKind.File,
                Name = item.File.Name,
                ServerRelativeUrl = item.File.ServerRelativeUrl,
                Length = item.File.Length,
                MajorVersion = item.File.MajorVersion,
                MinorVersion = item.File.MinorVersion,
                InformationProtection = informationProtection,
                CaptureDecision = captureDecision,
                Content = content
            };
        }

        private static bool HasAttachments(ListItem item)
        {
            object value;
            return item.FieldValues.TryGetValue("Attachments", out value) && value is bool && (bool)value;
        }

        private static string BuildViewXml(List list)
        {
            var fields = list.Fields.AsEnumerable()
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.InternalName))
                .Select(value => value.InternalName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(value => "<FieldRef Name='" + SecurityElement.Escape(value) + "'/>");
            return "<View Scope='RecursiveAll'><ViewFields>"
                + string.Join(string.Empty, fields)
                + "</ViewFields><RowLimit Paged='TRUE'>5000</RowLimit></View>";
        }

        private static Guid? ReadUniqueId(ListItem item)
        {
            object value;
            if (!item.FieldValues.TryGetValue("GUID", out value) || value == null)
            {
                return null;
            }
            if (value is Guid)
            {
                return (Guid)value;
            }
            Guid parsed;
            return Guid.TryParse(Convert.ToString(value), out parsed) ? parsed : (Guid?)null;
        }
    }
}
