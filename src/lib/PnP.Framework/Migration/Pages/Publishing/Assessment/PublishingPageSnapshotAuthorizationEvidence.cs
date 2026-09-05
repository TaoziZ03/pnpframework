using PnP.Framework.Migration.Evidence;
using PnP.Framework.Migration.Pages.Assessment;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Pages.References;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Assessment
{
    internal static class PublishingPageSnapshotAuthorizationEvidence
    {
        public static PageAssessmentEvidence Merge(
            PublishingPageCaptureBundle snapshot,
            PageAssessmentEvidence supplemental)
        {
            var layoutEvidence = snapshot?.Layout?.AuthorizationEvidence;
            var referenceEvidence = (snapshot?.Dependencies ?? Array.Empty<PageReferenceSnapshot>())
                .Where(value => value?.AuthorizationEvidence != null)
                .ToArray();
            if (layoutEvidence == null && referenceEvidence.Length == 0)
            {
                return supplemental;
            }

            var result = new PageAssessmentEvidence
            {
                SchemaVersion = supplemental?.SchemaVersion ?? "pnp-page-assessment-evidence/v1",
                AuthorizationFailures = (supplemental?.AuthorizationFailures
                        ?? Array.Empty<PageIngredientAuthorizationEvidence>())
                    .Where(value => value != null)
                    .Select(Copy)
                    .ToList(),
                TaxonomyAssetReviewPlan = supplemental?.TaxonomyAssetReviewPlan
            };
            if (layoutEvidence != null)
            {
                LiteralHttpAuthorizationEvidence.Validate(layoutEvidence);
                Add(
                    result.AuthorizationFailures,
                    PublishingPageIngredientIds.Layout,
                    layoutEvidence,
                    "snapshot.layout.authorizationEvidence");
                Add(
                    result.AuthorizationFailures,
                    PublishingPageIngredientIds.ContentType,
                    layoutEvidence,
                    "snapshot.layout.authorizationEvidence");
            }
            foreach (var reference in referenceEvidence)
            {
                PageReferenceAuthorizationEvidence.ValidateSource(snapshot.Source, reference);
                Add(
                    result.AuthorizationFailures,
                    PublishingPageIngredientIds.Reference(reference.Id),
                    reference.AuthorizationEvidence,
                    "snapshot.dependencies[].authorizationEvidence");
            }
            return result;
        }

        private static void Add(
            IList<PageIngredientAuthorizationEvidence> failures,
            string ingredientId,
            LiteralHttpAuthorizationEvidence source,
            string evidenceSource)
        {
            var matches = failures.Where(value =>
                    string.Equals(value.IngredientId, ingredientId, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length > 1)
            {
                throw new InvalidDataException(
                    $"Duplicate authorization evidence exists for ingredient '{ingredientId}'.");
            }
            var existing = matches.SingleOrDefault();
            if (existing != null)
            {
                if (existing.HttpStatusCode != source.HttpStatusCode
                    || !string.Equals(existing.RequestUri, source.RequestUri, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.EvidenceSha256, source.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Conflicting authorization evidence exists for ingredient '{ingredientId}'.");
                }
                return;
            }

            failures.Add(new PageIngredientAuthorizationEvidence
            {
                IngredientId = ingredientId,
                Operation = source.Operation,
                RequestUri = source.RequestUri,
                HttpStatusCode = source.HttpStatusCode,
                ObservedAtUtc = source.ObservedAtUtc,
                EvidenceSource = evidenceSource,
                EvidenceSha256 = source.EvidenceSha256
            });
        }

        private static PageIngredientAuthorizationEvidence Copy(
            PageIngredientAuthorizationEvidence source)
        {
            return new PageIngredientAuthorizationEvidence
            {
                IngredientId = source.IngredientId,
                Operation = source.Operation,
                RequestUri = source.RequestUri,
                HttpStatusCode = source.HttpStatusCode,
                ObservedAtUtc = source.ObservedAtUtc,
                EvidenceSource = source.EvidenceSource,
                EvidenceSha256 = source.EvidenceSha256
            };
        }
    }
}
