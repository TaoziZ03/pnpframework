using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.ClassicWebParts.Bindings;
using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.References;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal static class PublishingPageJsLinkReferenceProjector
    {
        internal static PublishingPageJsLinkReferenceEvidence ReadEvidence(PublishingPageIngredientEvidenceEnvelope envelope)
        {
            Require(envelope != null
                && string.Equals(envelope.EvidenceSchemaVersion, PublishingPageJsLinkReferenceEvidence.SchemaVersion, StringComparison.Ordinal)
                && string.Equals(envelope.LaneId, "resource.script", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(envelope.HandlerId)
                && envelope.CanonicalPayload.ValueKind == JsonValueKind.Object
                && envelope.EvidenceReferences != null && envelope.EvidenceReferences.Count > 0
                && envelope.EvidenceReferences.All(value => !string.IsNullOrWhiteSpace(value))
                && envelope.EvidenceReferences.SequenceEqual(envelope.EvidenceReferences.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
                && string.Equals(envelope.EvidenceDigest, PublishingPageIngredientEvidenceEnvelope.ComputeDigest(envelope), StringComparison.OrdinalIgnoreCase),
                "The persisted JSLink evidence envelope is missing, unsealed, or belongs to another lane.");
            var evidence = MigrationContractSerializer.Deserialize<PublishingPageJsLinkReferenceEvidence>(envelope.CanonicalPayload.GetRawText());
            Require(evidence != null
                && string.Equals(MigrationContractSerializer.SerializeCanonical(evidence), MigrationContractSerializer.SerializeCanonical(envelope.CanonicalPayload), StringComparison.Ordinal)
                && string.Equals(envelope.IngredientKey, evidence.IngredientId, StringComparison.Ordinal),
                "The persisted JSLink evidence does not match its typed schema or ingredient key.");
            ValidateIdentity(evidence);
            return evidence;
        }

        internal static PageIngredientNode CreateNode(
            PublishingPageCaptureBundle snapshot,
            PublishingPageIngredientEvidenceEnvelope envelope,
            PublishingPageJsLinkReferenceEvidence evidence)
        {
            var sealedEvidence = ReadEvidence(envelope);
            Require(string.Equals(MigrationContractSerializer.SerializeCanonical(evidence), MigrationContractSerializer.SerializeCanonical(sealedEvidence), StringComparison.Ordinal),
                "The JSLink projector input differs from the sealed evidence.");
            ValidateSource(snapshot, evidence);
            var node = new PageIngredientNode
            {
                Id = evidence.IngredientId,
                Kind = PageIngredientKind.Reference,
                KindId = PageIngredientKindIdentity.FromLegacyKind(PageIngredientKind.Reference),
                Subtype = evidence.Subtype,
                SemanticRole = evidence.SemanticRole,
                SourcePredicateId = PublishingPageJsLinkReferenceEvidence.SourcePredicateId,
                PrimaryOwnerLane = "resource.script",
                Label = evidence.Reference.OriginalValue,
                HasContent = true,
                Ownership = PageIngredientOwnership.SourceOwned,
                SourceAuthority = "Persisted Web Part JSLink property",
                EvidenceDigest = evidence.Reference.ContentSha256,
                EvidenceReferences = envelope.EvidenceReferences.ToList(),
                SourceVersionIdentity = PublishingPageIngredientPrimaryOwnerRegistry.SourceVersionIdentity(snapshot)
            };
            node.SourcePageOrListItemIdentity = PublishingPageIngredientPrimaryOwnerRegistry.SourceIdentity(snapshot, node);
            return node;
        }

        internal static bool MatchesSource(PublishingPageCaptureBundle snapshot, PageIngredientNode node)
        {
            try
            {
                var envelopes = (snapshot?.IngredientEvidence ?? Array.Empty<PublishingPageIngredientEvidenceEnvelope>())
                    .Where(value => value != null && string.Equals(value.IngredientKey, node?.Id, StringComparison.Ordinal)).ToArray();
                if (envelopes.Length != 1)
                {
                    return false;
                }
                var evidence = ReadEvidence(envelopes[0]);
                var expected = CreateNode(snapshot, envelopes[0], evidence);
                return node.Kind == expected.Kind
                    && string.Equals(node.KindId, expected.KindId, StringComparison.Ordinal)
                    && string.Equals(node.Subtype, expected.Subtype, StringComparison.Ordinal)
                    && string.Equals(node.SemanticRole, expected.SemanticRole, StringComparison.Ordinal)
                    && string.Equals(node.SourcePredicateId, expected.SourcePredicateId, StringComparison.Ordinal)
                    && string.Equals(node.SourcePageOrListItemIdentity, expected.SourcePageOrListItemIdentity, StringComparison.Ordinal)
                    && string.Equals(node.SourceVersionIdentity, expected.SourceVersionIdentity, StringComparison.Ordinal)
                    && string.Equals(node.PrimaryOwnerLane, expected.PrimaryOwnerLane, StringComparison.Ordinal)
                    && string.Equals(node.EvidenceDigest, expected.EvidenceDigest, StringComparison.OrdinalIgnoreCase)
                    && node.EvidenceReferences != null
                    && node.EvidenceReferences.SequenceEqual(expected.EvidenceReferences, StringComparer.Ordinal);
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is JsonException || exception is System.Xml.XmlException || exception is ArgumentException || exception is InvalidOperationException)
            {
                return false;
            }
        }

        private static void ValidateIdentity(PublishingPageJsLinkReferenceEvidence evidence)
        {
            Require(evidence.SourcePageFileUniqueId != Guid.Empty && evidence.SourceListItemId > 0
                && !string.IsNullOrWhiteSpace(evidence.SourcePageServerRelativeUrl) && !string.IsNullOrWhiteSpace(evidence.SourcePageETag)
                && evidence.HostWebPartId != Guid.Empty && evidence.HostWebPartOrder >= 0
                && evidence.PersistedReferenceOrder >= 0 && !string.IsNullOrWhiteSpace(evidence.PersistedPropertyValue)
                && string.Equals(evidence.ReferenceForm, "JSLink", StringComparison.Ordinal)
                && string.Equals(evidence.Subtype, PublishingPageJsLinkReferenceEvidence.SubtypeId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(evidence.SemanticRole),
                "The persisted JSLink claim tuple, source, host or ordered binding is incomplete.");
            var reference = evidence.Reference;
            Require(reference != null && reference.Kind == PageReferenceKind.Script
                && reference.CaptureStatus == PageCaptureStatus.Captured && reference.IsRenderableResource
                && reference.AuthorizationEvidence == null
                && !string.IsNullOrWhiteSpace(reference.OriginalValue)
                && !string.IsNullOrWhiteSpace(reference.SourceAbsoluteUrl) && !string.IsNullOrWhiteSpace(reference.SourceServerRelativeUrl)
                && string.Equals(reference.Consumer, PublishingPageIngredientIds.WebPart(evidence.HostWebPartId), StringComparison.Ordinal),
                "The captured script reference must belong to the exact persisted host.");
            MigrationArtifactContractValidator.Validate(new ArtifactReference { Sha256 = reference.ContentSha256, Length = reference.ContentLength },
                reference.ContentBase64, null, "JSLink script");
            Require(reference.ContentSha256.All(Uri.IsHexDigit) && IsDigest(evidence.HostEvidenceSha256)
                && (string.Equals(evidence.HostEvidenceFormat, ClassicWebPartPropertyEvidenceValidator.RestExpandedFormat, StringComparison.Ordinal)
                    || string.Equals(evidence.HostEvidenceFormat, ClassicWebPartPropertyEvidenceValidator.NativeExportFormat, StringComparison.Ordinal)),
                "The script or host digest is not SHA-256.");
            var expectedId = PublishingPageJsLinkReferenceEvidence.IngredientIdPrefix
                + evidence.SourcePageFileUniqueId.ToString("D") + ":" + evidence.HostWebPartId.ToString("D")
                + ":JSLink:" + reference.OriginalValue;
            Require(string.Equals(evidence.IngredientId, expectedId, StringComparison.Ordinal),
                "The JSLink ingredient ID does not bind the exact page, host, reference form and raw locator.");
            var ordered = evidence.PersistedPropertyValue.Split('|');
            Require(evidence.PersistedReferenceOrder < ordered.Length
                && string.Equals(ordered[evidence.PersistedReferenceOrder.Value].Trim(), reference.OriginalValue, StringComparison.Ordinal)
                && ordered.Count(value => string.Equals(value.Trim(), reference.OriginalValue, StringComparison.Ordinal)) == 1,
                "The JSLink locator has a wrong persisted order or ambiguous repeated occurrence.");
        }

        private static void ValidateSource(PublishingPageCaptureBundle snapshot, PublishingPageJsLinkReferenceEvidence evidence)
        {
            var source = snapshot?.Source;
            Require(source != null && snapshot.SourceFence != null
                && source.FileUniqueId == evidence.SourcePageFileUniqueId && source.ListItemId == evidence.SourceListItemId
                && snapshot.SourceFence.FileUniqueId == source.FileUniqueId
                && string.Equals(source.PageServerRelativeUrl, evidence.SourcePageServerRelativeUrl, StringComparison.Ordinal)
                && string.Equals(snapshot.SourceFence.ETag, evidence.SourcePageETag, StringComparison.Ordinal),
                "The JSLink source page, list item or ETag is stale or foreign.");
            var hosts = (snapshot.WebParts ?? Array.Empty<ClassicWebPartSnapshot>()).Where(value => value?.Id == evidence.HostWebPartId).ToArray();
            Require(hosts.Length == 1 && evidence.HostWebPartOrder == hosts[0].ZoneIndex,
                "The persisted JSLink host is missing, duplicated or at a different captured order.");
            var host = hosts[0];
            ValidateHostProperty(source, host, evidence);

            var reference = evidence.Reference;
            Require(Uri.TryCreate(source.WebUrl, UriKind.Absolute, out var sourceWeb)
                && Uri.TryCreate(reference.SourceAbsoluteUrl, UriKind.Absolute, out var resource)
                && (resource.Scheme == Uri.UriSchemeHttps || resource.Scheme == Uri.UriSchemeHttp)
                && string.Equals(resource.Authority, sourceWeb.Authority, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Uri.UnescapeDataString(resource.AbsolutePath), reference.SourceServerRelativeUrl, StringComparison.Ordinal),
                "The typed JSLink locator is not a same-site script resource.");
            Require(PageReferenceSnapshotReader.TryResolveUri(
                    new Uri(sourceWeb.GetLeftPart(UriPartial.Authority) + source.PageServerRelativeUrl),
                    sourceWeb, snapshot.SourceTopology?.SiteCollectionUrl, reference.OriginalValue, out var resolved)
                && string.Equals(resolved.AbsoluteUri, reference.SourceAbsoluteUrl, StringComparison.Ordinal),
                "The typed JSLink locator differs from the existing PnP reference normalization.");
            Require(!(snapshot.Dependencies ?? Array.Empty<PageReferenceSnapshot>()).Any(value => value != null
                && string.Equals(value.Consumer, reference.Consumer, StringComparison.Ordinal)
                && (string.Equals(value.OriginalValue, reference.OriginalValue, StringComparison.Ordinal)
                    || string.Equals(value.SourceAbsoluteUrl, reference.SourceAbsoluteUrl, StringComparison.Ordinal))),
                "The persisted JSLink binding is also present as a generic dependency; reconcile its identity before projection.");
        }

        private static void ValidateHostProperty(PageIdentity source, ClassicWebPartSnapshot host, PublishingPageJsLinkReferenceEvidence evidence)
        {
            if (string.Equals(evidence.HostEvidenceFormat, ClassicWebPartPropertyEvidenceValidator.RestExpandedFormat, StringComparison.Ordinal))
            {
                var directValue = ClassicWebPartPropertyEvidenceValidator.ReadDirectProperty(host, "JSLink");
                Require(string.Equals(host.PropertyEvidenceArtifact.Sha256, evidence.HostEvidenceSha256, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(directValue, evidence.PersistedPropertyValue, StringComparison.Ordinal),
                    "The REST property observation does not contain the exact bound direct JSLink value.");
                return;
            }
            Require(!string.IsNullOrWhiteSpace(host.ExportXml)
                && string.Equals(host.ExportSha256, evidence.HostEvidenceSha256, StringComparison.OrdinalIgnoreCase)
                && string.Equals(MigrationDigest.ComputeSha256(host.ExportXml), evidence.HostEvidenceSha256, StringComparison.OrdinalIgnoreCase),
                "The persisted host export differs from the bound host digest.");

            // Reuse PnP's list-Web-Part binding parser. The direct-property guard
            // prevents its XmlDefinition.JSLink fallback from changing reference form.
            var directProperties = XDocument.Parse(host.ExportXml).Descendants()
                .Where(value => value.Name.LocalName == "property" && string.Equals((string)value.Attribute("name"), "JSLink", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(directProperties.Length == 1 && string.Equals(directProperties[0].Value, evidence.PersistedPropertyValue, StringComparison.Ordinal),
                "The exact direct persisted JSLink property is absent or ambiguous.");
            var parsed = ClassicListWebPartBindingParser.Parse(host, source.WebId, source.WebUrl, source.PageServerRelativeUrl);
            Require(parsed.IsExecutable && string.Equals(parsed.Binding.JsLink, evidence.PersistedPropertyValue.Trim(), StringComparison.Ordinal),
                "The existing PnP list-Web-Part validator does not confirm the persisted JSLink binding.");
        }

        private static bool IsDigest(string value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
