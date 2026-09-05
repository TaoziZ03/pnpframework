using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Profiles;
using PnP.Framework.Migration.Pages.Publishing.Verification;
using PnP.Framework.Migration.Schema.ContentTypes;
using PnP.Framework.Migration.Taxonomy;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PnP.Framework.Migration.Pages.Publishing.Layouts
{
    public static class PublishingPageLayoutPlanFactory
    {
        public static PublishingPageLayoutMaterializationPlan Create(
            PublishingPageLayoutSnapshot layout,
            Uri sourcePageWebUrl,
            Uri targetPageWebUrl,
            Uri targetSiteCollectionUrl,
            PublishingPageWorkflowPolicy workflowPolicy,
            IEnumerable<TaxonomyTargetMapping> taxonomyMappings = null,
            IMigrationArtifactStore artifactStore = null,
            bool allowExternalResourceReferences = true)
        {
            if (workflowPolicy == null)
            {
                throw new ArgumentNullException(nameof(workflowPolicy));
            }

            return CreateCore(
                layout,
                sourcePageWebUrl,
                targetPageWebUrl,
                targetSiteCollectionUrl,
                workflowPolicy.PreferredTargetPageLayoutFileName,
                taxonomyMappings,
                artifactStore,
                allowExternalResourceReferences,
                workflowPolicy.StockPageLayoutFileNames,
                workflowPolicy);
        }

        public static PublishingPageLayoutMaterializationPlan Create(
            PublishingPageLayoutSnapshot layout,
            Uri sourcePageWebUrl,
            Uri targetPageWebUrl,
            Uri targetSiteCollectionUrl,
            string reviewedStockFileName,
            IEnumerable<TaxonomyTargetMapping> taxonomyMappings = null,
            IMigrationArtifactStore artifactStore = null,
            bool allowExternalResourceReferences = true,
            IEnumerable<string> recognizedStockFileNames = null)
        {
            return CreateCore(
                layout,
                sourcePageWebUrl,
                targetPageWebUrl,
                targetSiteCollectionUrl,
                reviewedStockFileName,
                taxonomyMappings,
                artifactStore,
                allowExternalResourceReferences,
                recognizedStockFileNames,
                null);
        }

        private static PublishingPageLayoutMaterializationPlan CreateCore(
            PublishingPageLayoutSnapshot layout,
            Uri sourcePageWebUrl,
            Uri targetPageWebUrl,
            Uri targetSiteCollectionUrl,
            string reviewedStockFileName,
            IEnumerable<TaxonomyTargetMapping> taxonomyMappings,
            IMigrationArtifactStore artifactStore,
            bool allowExternalResourceReferences,
            IEnumerable<string> recognizedStockFileNames,
            PublishingPageWorkflowPolicy workflowPolicy)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            RequireAbsoluteHttps(sourcePageWebUrl, nameof(sourcePageWebUrl));
            RequireAbsoluteHttps(targetPageWebUrl, nameof(targetPageWebUrl));
            RequireAbsoluteHttps(targetSiteCollectionUrl, nameof(targetSiteCollectionUrl));
            if (string.IsNullOrWhiteSpace(reviewedStockFileName))
            {
                throw new ArgumentException("A reviewed stock Page Layout file name is required.", nameof(reviewedStockFileName));
            }

            var sourceFileName = !string.IsNullOrWhiteSpace(layout.FileName)
                ? layout.FileName
                : Path.GetFileName(layout.ServerRelativeUrl ?? layout.Url ?? string.Empty);
            var isNativeStock = PublishingPageNativeLayoutCatalog.TryGetProfile(sourceFileName, out var nativeStockProfile);
            var isAllowedStock = string.Equals(sourceFileName, reviewedStockFileName, StringComparison.OrdinalIgnoreCase)
                || recognizedStockFileNames != null
                    && recognizedStockFileNames.Any(name => string.Equals(sourceFileName, name, StringComparison.OrdinalIgnoreCase));
            var isReviewedStock = layout.CustomizedPageStatus == 1
                && isAllowedStock;
            if (isReviewedStock)
            {
                if (workflowPolicy != null
                    && (!isNativeStock || !NativeProfileMatchesPolicy(nativeStockProfile, workflowPolicy)))
                {
                    return Block(layout, sourceFileName,
                        $"The source stock Page Layout '{sourceFileName}' is not in the exact native layout family allowed by workflow '{workflowPolicy.WorkflowId}'.");
                }
                if (isNativeStock && !LayoutAssociationMatchesNativeProfile(layout, nativeStockProfile))
                {
                    return Block(layout, sourceFileName,
                        $"The source stock Page Layout '{sourceFileName}' has missing or conflicting associated content type evidence.");
                }

                var targetStockFileName = sourceFileName ?? reviewedStockFileName;
                var stockTargetPath = BuildTargetPath(targetSiteCollectionUrl, targetStockFileName);

                return new PublishingPageLayoutMaterializationPlan
                {
                    Disposition = PublishingPageLayoutMaterializationDisposition.ReuseTargetStock,
                    SourceUrl = layout.Url,
                    SourceServerRelativeUrl = layout.ServerRelativeUrl,
                    SourceFileName = sourceFileName,
                    SourceBytes = layout.Bytes,
                    AssociatedContentTypeName = nativeStockProfile?.AssociatedContentTypeName ?? layout.AssociatedContentTypeName,
                    AssociatedContentTypeId = nativeStockProfile?.AssociatedContentTypeId ?? layout.AssociatedContentTypeId,
                    TargetFileName = targetStockFileName,
                    TargetPageLayoutName = Path.GetFileNameWithoutExtension(targetStockFileName),
                    TargetServerRelativeUrl = stockTargetPath,
                    RequiredFieldBindings = RequiredFieldBindings(layout),
                    RequiredRegistrations = layout.Registrations.ToList(),
                    Zones = layout.Zones.ToList(),
                    ResourceReferences = layout.ResourceReferences.ToList(),
                    TargetBytes = null,
                    Reason = $"Reuse the reviewed uncustomized target stock {targetStockFileName} Page Layout."
                };
            }

            if (isAllowedStock
                && PublishingPageNativeLayoutCatalog.TryGetUnavailableSourceSubstitution(
                layout,
                sourceFileName,
                out var nativeProfile)
                && (workflowPolicy == null || NativeProfileMatchesPolicy(nativeProfile, workflowPolicy)))
            {
                return new PublishingPageLayoutMaterializationPlan
                {
                    Disposition = PublishingPageLayoutMaterializationDisposition.ReuseTargetStock,
                    SourceUrl = layout.Url,
                    SourceServerRelativeUrl = layout.ServerRelativeUrl,
                    SourceFileName = sourceFileName,
                    SourceBytes = layout.Bytes,
                    AssociatedContentTypeName = nativeProfile.AssociatedContentTypeName,
                    AssociatedContentTypeId = nativeProfile.AssociatedContentTypeId,
                    TargetFileName = nativeProfile.FileName,
                    TargetPageLayoutName = Path.GetFileNameWithoutExtension(nativeProfile.FileName),
                    TargetServerRelativeUrl = BuildTargetPath(targetSiteCollectionUrl, nativeProfile.FileName),
                    RequiredFieldBindings = RequiredFieldBindings(layout),
                    RequiredRegistrations = layout.Registrations.ToList(),
                    Zones = layout.Zones.ToList(),
                    ResourceReferences = layout.ResourceReferences.ToList(),
                    TargetBytes = layout.Bytes,
                    Reason = $"Require the reviewed target-runtime stock {nativeProfile.FileName} Page Layout and its native '{nativeProfile.AssociatedContentTypeName}' association. "
                        + $"Source bytes remain unavailable ({layout.EvidenceState}); this is an explicit target-runtime substitution, not a source-byte equality claim."
                };
            }

            if (layout.EvidenceState != PublishingPageLayoutEvidenceState.Readable
                || layout.Availability != EvidenceAvailability.Captured
                || layout.Bytes == null)
            {
                return Block(layout, sourceFileName,
                    $"The source Page Layout cannot be materialized because its evidence state is {layout.EvidenceState} and availability is {layout.Availability}.");
            }

            if (string.IsNullOrWhiteSpace(layout.AssociatedContentTypeName)
                || string.IsNullOrWhiteSpace(layout.AssociatedContentTypeId))
            {
                return Block(layout, sourceFileName,
                    "The readable source Page Layout has no associated content type name or ID evidence.");
            }
            if (workflowPolicy != null
                && !PublishingPageContentTypeIdentity.MatchesSiteContentType(
                    layout.AssociatedContentTypeId,
                    workflowPolicy.SourceContentTypeIdPrefix))
            {
                return Block(layout, sourceFileName,
                    $"The source Page Layout content type '{layout.AssociatedContentTypeId}' is outside workflow '{workflowPolicy.WorkflowId}' content type family '{workflowPolicy.SourceContentTypeIdPrefix}'.");
            }

            var sourceBytes = MigrationArtifact.ReadAllBytes(layout.Bytes, layout.ContentBase64, artifactStore);
            var sourceSiteCollectionUrl = ResolveSourceSiteCollectionUrl(layout);
            var resourcePlans = layout.ResourceArtifacts
                .Select(resource => PublishingPageLayoutResourcePlanner.Create(
                    resource,
                    sourcePageWebUrl,
                    sourceSiteCollectionUrl,
                    targetPageWebUrl,
                    targetSiteCollectionUrl,
                    allowExternalResourceReferences))
                .OrderBy(value => value.SourceReference, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var rewrites = resourcePlans
                .Where(value => value.Disposition == PublishingPageLayoutResourceMaterializationDisposition.CreateOrReuseOwned)
                .Select(value => new PublishingPageLayoutResourceRewrite
                {
                    SourceReference = value.SourceReference,
                    TargetReference = value.TargetReference
                })
                .GroupBy(value => value.SourceReference + "" + value.TargetReference, StringComparer.Ordinal)
                .Select(value => value.First())
                .ToList();
            var targetBytes = PublishingPageLayoutResourceRewriter.Rewrite(sourceBytes, rewrites);
            var targetFileName = BuildOwnedFileName(sourceFileName, layout.Bytes.Sha256);
            var contentTypePlan = CreateContentTypePlan(layout.AssociatedContentTypeSchema, taxonomyMappings);
            return new PublishingPageLayoutMaterializationPlan
            {
                Disposition = resourcePlans.Any(value => value.Disposition == PublishingPageLayoutResourceMaterializationDisposition.Block)
                    || contentTypePlan == null
                    || contentTypePlan.Disposition == ContentTypeMaterializationDisposition.Block
                        ? PublishingPageLayoutMaterializationDisposition.Block
                        : PublishingPageLayoutMaterializationDisposition.CreateOwned,
                SourceUrl = layout.Url,
                SourceServerRelativeUrl = layout.ServerRelativeUrl,
                SourceFileName = sourceFileName,
                SourceBytes = layout.Bytes,
                AssociatedContentTypeName = layout.AssociatedContentTypeName,
                AssociatedContentTypeId = layout.AssociatedContentTypeId,
                TargetFileName = targetFileName,
                TargetPageLayoutName = Path.GetFileNameWithoutExtension(targetFileName),
                TargetServerRelativeUrl = BuildTargetPath(targetSiteCollectionUrl, targetFileName),
                RequiredFieldBindings = RequiredFieldBindings(layout),
                RequiredRegistrations = layout.Registrations.ToList(),
                Zones = layout.Zones.ToList(),
                ResourceReferences = layout.ResourceReferences.ToList(),
                ContentTypeSchema = contentTypePlan,
                TargetBytes = MigrationArtifact.Describe(targetBytes, "application/vnd.ms-aspx", targetFileName),
                ResourceMaterializations = resourcePlans,
                ResourceRewrites = rewrites,
                Reason = "Create or exactly reuse a digest-owned target Page Layout, its required field/content-type closure, and rendering resources before creating the page."
            };
        }

        public static string BuildOwnedFileName(string sourceFileName, string sourceBytesSha256)
        {
            if (string.IsNullOrWhiteSpace(sourceFileName) || string.IsNullOrWhiteSpace(sourceBytesSha256))
            {
                throw new ArgumentException("Source file name and byte digest are required.");
            }

            var stem = Regex.Replace(Path.GetFileNameWithoutExtension(sourceFileName).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (stem.Length == 0)
            {
                stem = "layout";
            }

            if (stem.Length > 48)
            {
                stem = stem.Substring(0, 48).TrimEnd('-');
            }

            var digestPrefix = sourceBytesSha256.Length <= 12 ? sourceBytesSha256 : sourceBytesSha256.Substring(0, 12);
            return $"pnp-{stem}-{digestPrefix}.aspx";
        }

        public static string BuildTargetPath(Uri targetSiteCollectionUrl, string fileName)
        {
            RequireAbsoluteHttps(targetSiteCollectionUrl, nameof(targetSiteCollectionUrl));
            if (string.IsNullOrWhiteSpace(fileName)
                || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
                || !fileName.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A single ASPX file name is required.", nameof(fileName));
            }

            return targetSiteCollectionUrl.AbsolutePath.TrimEnd('/') + "/_catalogs/masterpage/" + fileName;
        }

        private static IList<string> RequiredFieldBindings(PublishingPageLayoutSnapshot layout)
        {
            return layout.Controls
                .Select(value => value.FieldName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static PublishingPageLayoutMaterializationPlan Block(
            PublishingPageLayoutSnapshot layout,
            string sourceFileName,
            string reason)
        {
            return new PublishingPageLayoutMaterializationPlan
            {
                Disposition = PublishingPageLayoutMaterializationDisposition.Block,
                SourceUrl = layout.Url,
                SourceServerRelativeUrl = layout.ServerRelativeUrl,
                SourceFileName = sourceFileName,
                SourceBytes = layout.Bytes,
                AssociatedContentTypeName = layout.AssociatedContentTypeName,
                AssociatedContentTypeId = layout.AssociatedContentTypeId,
                RequiredFieldBindings = RequiredFieldBindings(layout),
                RequiredRegistrations = layout.Registrations.ToList(),
                Zones = layout.Zones.ToList(),
                ResourceReferences = layout.ResourceReferences.ToList(),
                Reason = reason
            };
        }

        private static Uri ResolveSourceSiteCollectionUrl(PublishingPageLayoutSnapshot layout)
        {
            if (!string.IsNullOrWhiteSpace(layout.OwnerSiteCollectionUrl))
            {
                Uri owner;
                if (!Uri.TryCreate(layout.OwnerSiteCollectionUrl, UriKind.Absolute, out owner)
                    || !string.Equals(owner.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The captured Page Layout owner Site Collection URL is invalid.");
                }

                return owner;
            }

            Uri sourceLayoutUrl;
            if (!Uri.TryCreate(layout.Url, UriKind.Absolute, out sourceLayoutUrl))
            {
                throw new InvalidDataException("An absolute source Page Layout URL is required to map site-collection resources.");
            }

            var marker = sourceLayoutUrl.AbsolutePath.IndexOf("/_catalogs/masterpage/", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                throw new InvalidDataException("The source Page Layout is not under a site-collection master page gallery.");
            }

            var path = sourceLayoutUrl.AbsolutePath.Substring(0, marker);
            return new Uri(sourceLayoutUrl.GetLeftPart(UriPartial.Authority) + (string.IsNullOrEmpty(path) ? "/" : path));
        }

        private static ContentTypeMaterializationPlan CreateContentTypePlan(
            ContentTypeSchemaSnapshot schema,
            IEnumerable<TaxonomyTargetMapping> taxonomyMappings)
        {
            if (schema == null)
            {
                return null;
            }

            if (schema.EvidenceState == ContentTypeSchemaEvidenceState.Readable
                && (schema.Availability == EvidenceAvailability.Captured
                    || schema.Availability == EvidenceAvailability.Conflict))
            {
                return ContentTypeSchemaPlanner.CreateRequiredClosure(schema, taxonomyMappings);
            }

            ContentTypeMaterializationPlan targetRuntimeRequirement;
            return ContentTypeSchemaPlanner.TryCreateTargetRuntimeRequirement(schema, out targetRuntimeRequirement)
                ? targetRuntimeRequirement
                : null;
        }

        private static bool NativeProfileMatchesPolicy(
            PublishingPageNativeLayoutProfile profile,
            PublishingPageWorkflowPolicy policy)
        {
            return profile != null
                && policy != null
                && policy.IsStockLayout(profile.FileName)
                && string.Equals(
                    profile.AssociatedContentTypeName,
                    policy.SourceContentTypeName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    profile.AssociatedContentTypeId,
                    policy.SourceContentTypeIdPrefix,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool LayoutAssociationMatchesNativeProfile(
            PublishingPageLayoutSnapshot layout,
            PublishingPageNativeLayoutProfile profile)
        {
            return layout != null
                && profile != null
                && !string.IsNullOrWhiteSpace(layout.AssociatedContentTypeName)
                && !string.IsNullOrWhiteSpace(layout.AssociatedContentTypeId)
                && string.Equals(
                    layout.AssociatedContentTypeName,
                    profile.AssociatedContentTypeName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    layout.AssociatedContentTypeId,
                    profile.AssociatedContentTypeId,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void RequireAbsoluteHttps(Uri value, string parameterName)
        {
            if (value == null || !value.IsAbsoluteUri || !string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("An absolute HTTPS URL is required.", parameterName);
            }
        }
    }
}
