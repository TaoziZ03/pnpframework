using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Pages.Content;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Taxonomy;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Verification
{
    internal static class PublishingPageFieldFreshReadbackVerifier
    {
        public static IList<PageFieldImportResult> Verify(
            ListItem targetItem,
            IEnumerable<PageFieldValueSnapshot> fields,
            IEnumerable<PageFieldAction> actions,
            IEnumerable<PageTextReplacement> replacements,
            IEnumerable<PageFieldImportResult> writeResults)
        {
            return Verify(targetItem?.FieldValues, fields, actions, replacements, writeResults);
        }

        public static IList<PageFieldImportResult> Verify(
            IDictionary<string, object> targetFieldValues,
            IEnumerable<PageFieldValueSnapshot> fields,
            IEnumerable<PageFieldAction> actions,
            IEnumerable<PageTextReplacement> replacements,
            IEnumerable<PageFieldImportResult> writeResults)
        {
            if (targetFieldValues == null)
            {
                throw new ArgumentNullException(nameof(targetFieldValues));
            }
            var fieldByName = (fields ?? Array.Empty<PageFieldValueSnapshot>())
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.InternalName))
                .ToDictionary(value => value.InternalName, StringComparer.OrdinalIgnoreCase);
            var writeResultByName = (writeResults ?? Array.Empty<PageFieldImportResult>())
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.InternalName))
                .GroupBy(value => value.InternalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(value => value.Key, value => value.First(), StringComparer.OrdinalIgnoreCase);
            var results = new List<PageFieldImportResult>();
            foreach (var action in (actions ?? Array.Empty<PageFieldAction>())
                         .Where(value => value != null && value.WillApply)
                         .OrderBy(value => value.SourceInternalName, StringComparer.OrdinalIgnoreCase))
            {
                writeResultByName.TryGetValue(action.SourceInternalName, out var writeResult);
                var result = new PageFieldImportResult
                {
                    InternalName = action.SourceInternalName,
                    PlannedDisposition = action.Disposition,
                    Attempted = true,
                    TaxonomyRelationships = writeResult?.TaxonomyRelationships
                        ?? new List<TaxonomyRelationshipMaterializationReceipt>()
                };
                results.Add(result);

                if (!fieldByName.TryGetValue(action.SourceInternalName, out var field))
                {
                    result.Message = "The sealed source field is missing during fresh readback verification.";
                    continue;
                }
                if (action.Disposition == PageFieldDisposition.ApplyTaxonomyRelationships)
                {
                    result.Succeeded = writeResult != null && writeResult.Attempted && writeResult.Succeeded;
                    result.Message = result.Succeeded
                        ? "The taxonomy write completed; relationship state is verified separately by fresh readback."
                        : "No successful taxonomy materialization evidence is available for fresh readback verification.";
                    continue;
                }
                if (writeResult != null && writeResult.Attempted && !writeResult.Succeeded)
                {
                    result.Message = "The field write failed before fresh readback.";
                    continue;
                }

                targetFieldValues.TryGetValue(action.TargetInternalName, out var actual);
                result.Succeeded = Matches(field, actual, replacements);
                result.Message = result.Succeeded
                    ? "Fresh target readback matches the sealed rewritten field value."
                    : "Fresh target readback differs from the sealed rewritten field value.";
            }
            return results;
        }

        public static bool LayoutMatches(ListItem targetItem, string expectedServerRelativeUrl)
        {
            return LayoutMatches(targetItem?.FieldValues, expectedServerRelativeUrl);
        }

        public static bool LayoutMatches(
            IDictionary<string, object> targetFieldValues,
            string expectedServerRelativeUrl)
        {
            if (targetFieldValues == null || string.IsNullOrWhiteSpace(expectedServerRelativeUrl)
                || !targetFieldValues.TryGetValue("PublishingPageLayout", out var value)
                || value == null)
            {
                return false;
            }

            var actual = value is FieldUrlValue urlValue ? urlValue.Url : Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(actual))
            {
                return false;
            }
            if (Uri.TryCreate(actual, UriKind.Absolute, out var absolute))
            {
                actual = Uri.UnescapeDataString(absolute.AbsolutePath);
            }
            return string.Equals(
                actual.TrimEnd('/'),
                expectedServerRelativeUrl.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool Matches(
            PageFieldValueSnapshot field,
            object actual,
            IEnumerable<PageTextReplacement> replacements)
        {
            switch (field.Kind)
            {
                case PageFieldValueKind.String:
                    return string.Equals(
                        PageTextTransformer.Rewrite(field.Value, replacements),
                        Convert.ToString(actual, CultureInfo.InvariantCulture),
                        StringComparison.Ordinal);
                case PageFieldValueKind.StringCollection:
                    var observed = actual as string[]
                        ?? (actual as IEnumerable<string>)?.ToArray()
                        ?? Array.Empty<string>();
                    return observed.SequenceEqual(field.StringValues ?? Array.Empty<string>(), StringComparer.Ordinal);
                case PageFieldValueKind.Boolean:
                    return bool.TryParse(field.Value, out var expectedBoolean)
                        && actual is bool actualBoolean
                        && actualBoolean == expectedBoolean;
                case PageFieldValueKind.Number:
                    return double.TryParse(field.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var expectedNumber)
                        && actual != null
                        && Math.Abs(Convert.ToDouble(actual, CultureInfo.InvariantCulture) - expectedNumber) < 0.0000001d;
                case PageFieldValueKind.DateTime:
                    return DateTime.TryParse(field.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expectedDate)
                        && actual is DateTime actualDate
                        && actualDate.ToUniversalTime() == expectedDate.ToUniversalTime();
                case PageFieldValueKind.Guid:
                    return Guid.TryParse(field.Value, out var expectedGuid)
                        && Guid.TryParse(Convert.ToString(actual, CultureInfo.InvariantCulture), out var actualGuid)
                        && actualGuid == expectedGuid;
                case PageFieldValueKind.Url:
                    var actualUrl = actual as FieldUrlValue;
                    return actualUrl != null
                        && string.Equals(
                            PageTextTransformer.Rewrite(field.UrlValue?.Url, replacements),
                            actualUrl.Url,
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals(field.UrlValue?.Description, actualUrl.Description, StringComparison.Ordinal);
                default:
                    return false;
            }
        }
    }
}
