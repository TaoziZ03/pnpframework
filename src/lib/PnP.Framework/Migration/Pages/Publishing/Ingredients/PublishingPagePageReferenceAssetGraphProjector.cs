using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Utilities;
using System;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    // Opt-in through the existing handler context. Historical projections must
    // not acquire new nodes (and different sealed digests) merely on upgrade.
    internal static class PublishingPagePageReferenceAssetGraphProjector
    {
        private static readonly FileExtensionContentTypeProvider MediaTypes = new FileExtensionContentTypeProvider();

        public static PageIngredientNode CreateSourceNode(PublishingPageCaptureBundle snapshot, string referenceId)
        {
            var references = (snapshot?.Dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .Where(value => value != null && string.Equals(value.Id, referenceId, StringComparison.Ordinal))
                .ToArray();
            if (string.IsNullOrWhiteSpace(referenceId) || references.Length != 1)
            {
                throw new InvalidDataException("Page-reference asset projection requires one captured reference ID.");
            }
            return IsAssetReference(references[0])
                ? CreateForPath(snapshot, references[0].SourceServerRelativeUrl)
                : null;
        }

        public static PageIngredientNode FindSourceNode(PublishingPageCaptureBundle snapshot, string ingredientId)
        {
            if (snapshot?.Source == null || ingredientId?.StartsWith("asset:page-reference:", StringComparison.Ordinal) != true)
            {
                return null;
            }
            var reference = (snapshot.Dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .FirstOrDefault(value => IsAssetReference(value)
                    && string.Equals(PublishingPageIngredientIds.PageReferencedAsset(
                        snapshot.Source.FileUniqueId, value.SourceServerRelativeUrl), ingredientId, StringComparison.Ordinal));
            return reference == null ? null : CreateForPath(snapshot, reference.SourceServerRelativeUrl);
        }

        public static bool MatchesSourceNode(PublishingPageCaptureBundle snapshot, PageIngredientNode node)
        {
            var expected = FindSourceNode(snapshot, node?.Id);
            return expected != null
                && node.HasContent
                && node.Ownership == expected.Ownership
                && string.Equals(node.KindId, expected.KindId, StringComparison.Ordinal)
                && string.Equals(node.Subtype, expected.Subtype, StringComparison.Ordinal)
                && string.Equals(node.SemanticRole, expected.SemanticRole, StringComparison.Ordinal)
                && string.Equals(node.SourcePredicateId, expected.SourcePredicateId, StringComparison.Ordinal)
                && string.Equals(node.Label, expected.Label, StringComparison.Ordinal)
                && string.Equals(node.SourceAuthority, expected.SourceAuthority, StringComparison.Ordinal)
                && string.Equals(node.EvidenceDigest, expected.EvidenceDigest, StringComparison.Ordinal)
                && node.EvidenceReferences != null
                && node.EvidenceReferences.SequenceEqual(expected.EvidenceReferences, StringComparer.Ordinal);
        }

        private static PageIngredientNode CreateForPath(PublishingPageCaptureBundle snapshot, string path)
        {
            if (!HasStableSourceFence(snapshot) || string.IsNullOrWhiteSpace(path)
                || !MediaTypes.TryGetContentType(path, out var mediaType))
            {
                return null;
            }

            var references = (snapshot.Dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .Where(value => IsAssetReference(value) && string.Equals(value.SourceServerRelativeUrl, path, StringComparison.Ordinal))
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .ToArray();
            if (references.Length == 0 || references.Any(value => !IsDirectSourceFile(snapshot, value))
                || references.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != references.Length)
            {
                return null;
            }

            var first = references[0];
            byte[] payload = null;
            foreach (var reference in references)
            {
                // Conflicting, unavailable, or denied observations suppress only
                // this payload node. The original Reference nodes remain intact.
                if (!TryReadPayload(reference, out var bytes)
                    || reference.ContentLength != first.ContentLength
                    || !string.Equals(reference.ContentSha256, first.ContentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                payload = bytes;
            }

            var image = references.Any(value => value.Kind == PageReferenceKind.Image);
            if (image ? !IsSupportedImagePayload(mediaType, payload) : !IsSupportedFilePayload(mediaType, payload))
            {
                return null;
            }

            return new PageIngredientNode
            {
                Id = PublishingPageIngredientIds.PageReferencedAsset(snapshot.Source.FileUniqueId, path),
                Kind = PageIngredientKind.Asset,
                KindId = PageIngredientKindIdentity.FromLegacyKind(PageIngredientKind.Asset),
                Subtype = image ? "asset.image" : "asset.page-referenced-file",
                SemanticRole = image ? "rendered-image-bytes" : "page-referenced-file-bytes",
                SourcePredicateId = image ? "asset.typed-image" : "asset.direct-page-file",
                Label = path,
                HasContent = true,
                Ownership = PageIngredientOwnership.SourceOwned,
                SourceAuthority = "Captured PageReferenceSnapshot payload",
                EvidenceDigest = first.ContentSha256.ToLowerInvariant(),
                EvidenceReferences = references.Select(value => PublishingPageIngredientIds.Reference(value.Id)).ToList()
            };
        }

        private static bool IsAssetReference(PageReferenceSnapshot reference)
        {
            return reference != null && (reference.Kind == PageReferenceKind.Image || reference.Kind == PageReferenceKind.Anchor);
        }

        private static bool HasStableSourceFence(PublishingPageCaptureBundle snapshot)
        {
            var source = snapshot?.Source;
            var fence = snapshot?.SourceFence;
            return source != null && fence != null && source.FileUniqueId != Guid.Empty
                && source.FileUniqueId == fence.FileUniqueId
                && !string.IsNullOrWhiteSpace(source.VersionLabel)
                && string.Equals(source.VersionLabel, fence.VersionLabel, StringComparison.Ordinal)
                && source.Length == fence.Length
                && source.ModifiedUtc.ToUniversalTime() == fence.ModifiedUtc.ToUniversalTime();
        }

        private static bool IsDirectSourceFile(PublishingPageCaptureBundle snapshot, PageReferenceSnapshot reference)
        {
            return !string.IsNullOrWhiteSpace(reference.Id)
                && !string.IsNullOrWhiteSpace(reference.Consumer)
                && !string.IsNullOrWhiteSpace(reference.OriginalValue)
                && reference.IsRenderableResource
                && !PageReferenceSnapshotReader.IsSharePointRuntimePath(reference.SourceServerRelativeUrl)
                && Uri.TryCreate(snapshot.Source.WebUrl, UriKind.Absolute, out var source)
                && Uri.TryCreate(reference.SourceAbsoluteUrl, UriKind.Absolute, out var resource)
                && (resource.Scheme == Uri.UriSchemeHttps || resource.Scheme == Uri.UriSchemeHttp)
                && string.Equals(source.GetLeftPart(UriPartial.Authority), resource.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(resource.Query) && string.IsNullOrEmpty(resource.Fragment)
                && string.Equals(Uri.UnescapeDataString(resource.AbsolutePath), reference.SourceServerRelativeUrl, StringComparison.Ordinal);
        }

        private static bool TryReadPayload(PageReferenceSnapshot reference, out byte[] bytes)
        {
            bytes = null;
            if (reference.CaptureStatus != PageCaptureStatus.Captured || reference.AuthorizationEvidence != null
                || reference.ContentBase64 == null || string.IsNullOrWhiteSpace(reference.ContentSha256))
            {
                return false;
            }
            // An explicitly captured empty file is not an absent payload.
            if (reference.ContentBase64.Length == 0)
            {
                bytes = Array.Empty<byte>();
                return reference.ContentLength == 0
                    && string.Equals(reference.ContentSha256, MigrationDigest.ComputeSha256(bytes), StringComparison.OrdinalIgnoreCase);
            }
            try
            {
                bytes = MigrationArtifact.ReadAllBytes(new ArtifactReference
                {
                    Sha256 = reference.ContentSha256,
                    Length = reference.ContentLength
                }, reference.ContentBase64);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        private static bool IsSupportedImagePayload(string mediaType, byte[] bytes)
        {
            // This first source predicate is proved by the PNG canary. A typed
            // img reference, extension, or HTTP-200 HTML denial alone is not PNG
            // payload evidence. Other formats stay reference-only until their
            // predicates have fixtures; full image validation belongs to the lane.
            var header = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 };
            var end = new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 };
            return string.Equals(mediaType, "image/png", StringComparison.Ordinal)
                && bytes != null && bytes.Length >= 45
                && bytes.Take(header.Length).SequenceEqual(header)
                && bytes.Skip(16).Take(4).Any(value => value != 0)
                && bytes.Skip(20).Take(4).Any(value => value != 0)
                && bytes.Skip(bytes.Length - end.Length).SequenceEqual(end);
        }

        private static bool IsFileMediaType(string mediaType)
        {
            return !mediaType.StartsWith("image/", StringComparison.Ordinal)
                && mediaType != "text/html" && mediaType != "application/xhtml+xml"
                && mediaType != "text/css" && mediaType != "text/javascript" && mediaType != "application/javascript";
        }

        private static bool IsSupportedFilePayload(string mediaType, byte[] bytes)
        {
            if (!IsFileMediaType(mediaType) || bytes == null)
            {
                return false;
            }

            // An empty captured file is explicit payload evidence and remains
            // distinct from a missing or reference-only observation. Nonempty
            // Anchor payloads need a format-specific semantic proof; an
            // extension and a digest alone cannot authorize an HTTP-200 error
            // shell as source file bytes.
            if (bytes.Length == 0)
            {
                return true;
            }

            return string.Equals(mediaType, "application/pdf", StringComparison.Ordinal)
                && IsPdfPayload(bytes);
        }

        private static bool IsPdfPayload(byte[] bytes)
        {
            var header = new byte[] { 37, 80, 68, 70, 45 }; // %PDF-
            var end = new byte[] { 37, 37, 69, 79, 70 }; // %%EOF
            if (bytes.Length < 13 || !bytes.Take(header.Length).SequenceEqual(header)
                || bytes[5] < (byte)'1' || bytes[5] > (byte)'9'
                || bytes[6] != (byte)'.' || bytes[7] < (byte)'0' || bytes[7] > (byte)'9')
            {
                return false;
            }

            // PDF permits trailing line endings and other bounded trailing
            // bytes. Bind the proof to an EOF marker near the actual payload
            // end instead of looking for denial text in arbitrary content.
            var firstCandidate = Math.Max(0, bytes.Length - 1024);
            for (var offset = bytes.Length - end.Length; offset >= firstCandidate; offset--)
            {
                if (bytes.Skip(offset).Take(end.Length).SequenceEqual(end))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
