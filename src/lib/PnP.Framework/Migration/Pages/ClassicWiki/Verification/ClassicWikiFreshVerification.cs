using PnP.Framework.Migration.Pages.ClassicWebParts;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Planning;
using PnP.Framework.Migration.Pages.Fields;
using PnP.Framework.Migration.Pages.Packaging;
using PnP.Framework.Migration.Pages.References;
using PnP.Framework.Migration.Pages.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    internal static class ClassicWikiFreshVerification
    {
        public static ClassicWikiComparisonResult Evaluate(
            ClassicWikiMigrationPackage package,
            ClassicWikiFreshTargetEvidence evidence)
        {
            if (package?.Plan?.TargetLocation == null) throw new ArgumentNullException(nameof(package));
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));

            var result = new ClassicWikiComparisonResult();
            var target = evidence.Recapture?.Snapshot;
            if (!evidence.IndependentContext)
            {
                result.Differences.Add("Fresh verification did not use an independent target context.");
            }
            if (target == null)
            {
                result.Differences.Add("Fresh target recapture evidence is missing.");
                return result;
            }

            CompareTargetIdentity(package, target, result);
            CompareWikiField(package, target, result);
            CompareTitle(package.Plan.FieldPlan?.Title, target.Fields, result);
            CompareContentType(target.Source?.ContentTypeId, target.Source?.ContentTypeName, result);
            CompareLibrary(package.Plan.TargetLocation, target, result);
            CompareRuntime(target.Runtime, result);
            CompareOwnership(package, evidence.FileProperties, result);
            CompareWebParts(package.Plan.WebParts, target.WebParts, result);
            CompareDependencies(package.Plan.Dependencies, target.Dependencies, result);
            CompareLifecycle(package, target, result);
            CompareSecurity(package.Plan.SecurityPlan, target.Security, result);

            if ((target.Blockers?.Count ?? 0) > 0)
            {
                result.Differences.Add("Fresh target recapture has blockers: " + string.Join("; ", target.Blockers));
            }

            result.Passed = result.Differences.Count == 0;
            return result;
        }

        public static bool HasExplicitExclusions(ClassicWikiMigrationPackage package)
        {
            return (package?.Plan?.FieldPlan?.DeferredFieldNames?.Count ?? 0) > 0
                || string.Equals(package?.Plan?.SecurityPlan?.Disposition, "Deferred", StringComparison.OrdinalIgnoreCase);
        }

        private static void CompareTargetIdentity(
            ClassicWikiMigrationPackage package,
            Capture.ClassicWikiCaptureBundle target,
            ClassicWikiComparisonResult result)
        {
            var plan = package.Plan;
            var location = plan.TargetLocation;
            if (location.TargetWebId != Guid.Empty
                && target.Source != null
                && target.Source.WebId == location.TargetWebId
                && target.Source.FileUniqueId != Guid.Empty
                && target.Source.ListItemId > 0
                && SameAbsoluteUrl(target.Source.WebUrl, location.TargetWebUrl)
                && RequiredPathEquals(target.Source.PageServerRelativeUrl, plan.TargetPageServerRelativeUrl))
            {
                result.TargetIdentityMatched = true;
                result.CanariesPassed.Add("TargetWebAndPageIdentity");
            }
            else
            {
                result.Differences.Add(
                    $"Target identity mismatch: expected Web '{location.TargetWebUrl}' ({location.TargetWebId:D}) and page '{plan.TargetPageServerRelativeUrl}', observed '{target.Source?.WebUrl}' ({target.Source?.WebId:D}) and '{target.Source?.PageServerRelativeUrl}'.");
            }
        }

        private static void CompareWikiField(
            ClassicWikiMigrationPackage package,
            Capture.ClassicWikiCaptureBundle target,
            ClassicWikiComparisonResult result)
        {
            var plan = package.Plan.WikiFieldPlan;
            var actualValue = target.WikiField;
            var actualDigest = target.WikiFieldSha256;
            var actualDigestValid = !string.IsNullOrWhiteSpace(actualDigest)
                && string.Equals(actualDigest, ClassicWikiDigest.ComputeSha256(actualValue ?? string.Empty), StringComparison.OrdinalIgnoreCase);
            var planDigestValid = plan != null
                && !string.IsNullOrWhiteSpace(plan.ExpectedStoredSha256)
                && string.Equals(
                    plan.ExpectedStoredSha256,
                    ClassicWikiDigest.ComputeSha256(plan.ExactValue ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase);
            var exact = planDigestValid
                && actualDigestValid
                && string.Equals(plan.ExpectedStoredSha256, actualDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(plan.ExactValue ?? string.Empty, actualValue ?? string.Empty, StringComparison.Ordinal);
            var entitySafe = planDigestValid
                && actualDigestValid
                && string.Equals(plan.EntitySafeValue ?? string.Empty, actualValue ?? string.Empty, StringComparison.Ordinal);
            if (exact || entitySafe)
            {
                result.WikiContentMatched = true;
                result.BracketNormalizationMatched = entitySafe && !exact;
                result.CanariesPassed.Add(exact ? "ExactWikiFieldMatch" : "BracketNormalizationMatch");
            }
            else
            {
                result.Differences.Add($"WikiField fresh readback does not match the sealed value/digest. Actual SHA '{actualDigest}'.");
            }
        }

        private static void CompareTitle(string expectedTitle, IList<PageFieldValueSnapshot> fields, ClassicWikiComparisonResult result)
        {
            var title = fields?.FirstOrDefault(value =>
                value != null && string.Equals(value.InternalName, "Title", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(expectedTitle)
                && title != null
                && string.Equals(expectedTitle, title.Value, StringComparison.Ordinal))
            {
                result.FieldsMatched = true;
                result.CanariesPassed.Add("PlannedFieldValueFidelity");
            }
            else
            {
                result.Differences.Add($"Title field mismatch: expected '{expectedTitle}', observed '{title?.Value}'.");
            }
        }

        private static void CompareContentType(string contentTypeId, string contentTypeName, ClassicWikiComparisonResult result)
        {
            if (!string.IsNullOrWhiteSpace(contentTypeId)
                && contentTypeId.StartsWith(ClassicWikiPackageContract.DefaultContentTypeId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(contentTypeName))
            {
                result.ContentTypeMatched = true;
                result.CanariesPassed.Add("ClassicWikiContentTypeIdentity");
            }
            else
            {
                result.Differences.Add($"Fresh Content Type is not a classic Wiki Page Content Type: '{contentTypeId}'/'{contentTypeName}'.");
            }
        }

        private static void CompareLibrary(
            ClassicWikiTargetLocationPlan expected,
            Capture.ClassicWikiCaptureBundle target,
            ClassicWikiComparisonResult result)
        {
            if ((expected.TargetLibraryTemplate == 101 || expected.TargetLibraryTemplate == 119)
                && target.LibraryBaseTemplate == expected.TargetLibraryTemplate
                && RequiredPathEquals(target.LibraryServerRelativeUrl, expected.TargetLibraryServerRelativeUrl)
                && ClassicWikiComparison.RequiredEquals(target.LibraryTitle, expected.TargetLibraryTitle))
            {
                result.LibraryMatched = true;
                result.CanariesPassed.Add("TargetLibraryIdentity");
            }
            else
            {
                result.Differences.Add(
                    $"Target library mismatch: expected '{expected.TargetLibraryTemplate}:{expected.TargetLibraryServerRelativeUrl}:{expected.TargetLibraryTitle}', observed '{target.LibraryBaseTemplate}:{target.LibraryServerRelativeUrl}:{target.LibraryTitle}'.");
            }
        }

        private static void CompareRuntime(PageRuntimeSnapshot runtime, ClassicWikiComparisonResult result)
        {
            if (runtime != null
                && runtime.ResolutionState == PageRuntimeResolutionState.Resolved
                && string.Equals(runtime.AdapterId, PageRuntimeAdapterIds.Wiki, StringComparison.Ordinal))
            {
                result.RuntimeMatched = true;
                result.CanariesPassed.Add("ClassicWikiRuntimeIdentity");
            }
            else
            {
                result.Differences.Add($"Fresh page runtime is not resolved as '{PageRuntimeAdapterIds.Wiki}'.");
            }
        }

        private static void CompareOwnership(
            ClassicWikiMigrationPackage package,
            IDictionary<string, object> properties,
            ClassicWikiComparisonResult result)
        {
            if (properties != null
                && ClassicWikiTargetOwnership.MatchesApprovedPlan(
                    properties,
                    package.Plan.OriginalIdentifier,
                    package.SnapshotDigest,
                    package.PlanDigest))
            {
                result.OwnershipMatched = true;
                result.CanariesPassed.Add("MigrationOwnershipFidelity");
            }
            else
            {
                result.Differences.Add("Fresh ownership properties do not match the sealed source snapshot and plan.");
            }
        }

        private static void CompareWebParts(
            IList<ClassicWikiWebPartPlacementPlan> expected,
            IList<ClassicWebPartSnapshot> actual,
            ClassicWikiComparisonResult result)
        {
            var plans = expected ?? Array.Empty<ClassicWikiWebPartPlacementPlan>();
            var snapshots = actual ?? Array.Empty<ClassicWebPartSnapshot>();
            if (plans.Count != snapshots.Count)
            {
                result.Differences.Add($"WebPart count mismatch: expected {plans.Count}, observed {snapshots.Count}.");
                return;
            }

            var unused = snapshots.ToList();
            foreach (var plan in plans)
            {
                var expectedDigest = string.IsNullOrWhiteSpace(plan.Xml) ? null : PageDigest.ComputeSha256(plan.Xml);
                var match = unused.FirstOrDefault(value =>
                    ClassicWikiComparison.RequiredEquals(value.TypeName, plan.TypeName)
                    && ClassicWikiComparison.RequiredEquals(value.ZoneId, plan.ZoneId)
                    && value.ZoneIndex == plan.TargetZoneIndex
                    && value.Hidden == plan.Hidden
                    && ClassicWikiComparison.RequiredEquals(value.ExportSha256, expectedDigest));
                if (match == null)
                {
                    result.Differences.Add($"WebPart type/export/zone/hidden mismatch for '{plan.Title}'.");
                    return;
                }
                unused.Remove(match);
            }

            result.WebPartsMatched = true;
            result.CanariesPassed.Add("WebPartValueFidelity");
        }

        private static void CompareDependencies(
            IList<ClassicWikiDependencyPlan> expected,
            IList<PageReferenceSnapshot> actual,
            ClassicWikiComparisonResult result)
        {
            var plans = expected ?? Array.Empty<ClassicWikiDependencyPlan>();
            var snapshots = actual ?? Array.Empty<PageReferenceSnapshot>();
            if (plans.Count != snapshots.Count)
            {
                result.Differences.Add($"Dependency count mismatch: expected {plans.Count}, observed {snapshots.Count}.");
                return;
            }

            var unused = snapshots.ToList();
            foreach (var plan in plans)
            {
                var expectedId = !string.IsNullOrWhiteSpace(plan.Consumer) && !string.IsNullOrWhiteSpace(plan.TargetAbsoluteUrl)
                    ? PageDigest.ComputeSha256(plan.Consumer + "\n" + plan.TargetAbsoluteUrl)
                    : null;
                var match = unused.FirstOrDefault(value =>
                    value.Kind == plan.Kind
                    && ClassicWikiComparison.RequiredEquals(value.Id, expectedId)
                    && ClassicWikiComparison.RequiredEquals(value.Consumer, plan.Consumer)
                    && ClassicWikiComparison.RequiredEquals(value.OriginalValue, plan.TargetOriginalValue)
                    && RequiredUrlEquals(value.SourceAbsoluteUrl, plan.TargetAbsoluteUrl)
                    && OptionalPathEquals(value.SourceServerRelativeUrl, plan.TargetServerRelativeUrl));
                if (match == null)
                {
                    result.Differences.Add($"Dependency exact-semantics mismatch for '{plan.Consumer}'/'{plan.TargetOriginalValue}'.");
                    return;
                }
                unused.Remove(match);
            }

            result.DependenciesMatched = true;
            result.CanariesPassed.Add("DependencyExactSemantics");
        }

        private static void CompareLifecycle(
            ClassicWikiMigrationPackage package,
            Capture.ClassicWikiCaptureBundle target,
            ClassicWikiComparisonResult result)
        {
            var actual = target.Lifecycle;
            if (package.Plan.LifecyclePolicy == ClassicWikiLifecyclePolicy.Publish
                && actual != null
                && target.LibraryEnableVersioning
                && string.Equals(actual.Level, "Published", StringComparison.OrdinalIgnoreCase)
                && string.Equals(actual.CheckOutType, "None", StringComparison.OrdinalIgnoreCase)
                && IsPublishedMajorVersion(target.Source?.VersionLabel)
                && (!target.LibraryEnableModeration || actual.ModerationStatus == 0))
            {
                result.LifecycleMatched = true;
                result.CanariesPassed.Add("LifecycleFidelity");
            }
            else
            {
                result.Differences.Add("Fresh lifecycle evidence is missing or differs from the sealed publish policy.");
            }
        }

        internal static bool IsPublishedMajorVersion(string versionLabel)
        {
            if (string.IsNullOrWhiteSpace(versionLabel))
            {
                return false;
            }

            var separator = versionLabel.LastIndexOf('.');
            return separator > 0
                && int.TryParse(versionLabel.Substring(separator + 1), out var minor)
                && minor == 0;
        }

        private static void CompareSecurity(
            ClassicWikiSecurityPlan expected,
            Security.PageSecuritySnapshot actual,
            ClassicWikiComparisonResult result)
        {
            if (expected != null
                && actual != null
                && (string.Equals(expected.Disposition, "Inherit", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(expected.Disposition, "Deferred", StringComparison.OrdinalIgnoreCase))
                && !actual.HasUniqueRoleAssignments
                && (actual.RoleAssignments?.Count ?? 0) == 0)
            {
                result.SecurityMatched = true;
                result.CanariesPassed.Add("SecurityPolicyFidelity");
            }
            else
            {
                result.Differences.Add("Fresh security evidence is missing or differs from the sealed target security policy.");
            }
        }

        private static bool SameAbsoluteUrl(string left, string right)
        {
            return Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
                && Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
                && string.Equals(
                    leftUri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
                    rightUri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool RequiredUrlEquals(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && Uri.TryCreate(left, UriKind.Absolute, out var leftUri)
                && Uri.TryCreate(right, UriKind.Absolute, out var rightUri)
                && string.Equals(leftUri.Scheme, rightUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(leftUri.Host, rightUri.Host, StringComparison.OrdinalIgnoreCase)
                && leftUri.Port == rightUri.Port
                && string.Equals(Uri.UnescapeDataString(leftUri.AbsolutePath), Uri.UnescapeDataString(rightUri.AbsolutePath), StringComparison.OrdinalIgnoreCase)
                && string.Equals(leftUri.Query, rightUri.Query, StringComparison.Ordinal)
                && string.Equals(leftUri.Fragment, rightUri.Fragment, StringComparison.Ordinal);
        }

        private static bool RequiredPathEquals(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private static bool OptionalPathEquals(string left, string right)
        {
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)
                || RequiredPathEquals(left, right);
        }
    }
}
