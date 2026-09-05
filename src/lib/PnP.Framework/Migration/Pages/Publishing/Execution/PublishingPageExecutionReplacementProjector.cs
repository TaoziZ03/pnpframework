using PnP.Framework.Migration.Pages.Content;
using PnP.Framework.Migration.Pages.Capture;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.References;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Execution
{
    /// <summary>
    /// Projects the sealed replacement set onto the admitted reference frontier.
    /// A partial reference transaction must not let a broad Web-path replacement
    /// silently rewrite a reference whose ingredient was deferred or blocked.
    /// </summary>
    internal static class PublishingPageExecutionReplacementProjector
    {
        public static IList<PageTextReplacement> Project(
            PublishingPageMigrationPackage package,
            PublishingPageExecutionScope executionScope)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }
            if (executionScope == null)
            {
                throw new ArgumentNullException(nameof(executionScope));
            }

            var actions = (package.Plan?.DependencyActions ?? Array.Empty<PageReferenceAction>())
                .Where(IsReplacementAction)
                .ToArray();
            var selectedIds = new HashSet<string>(
                executionScope.ReferenceActions(package)
                    .Where(IsReplacementAction)
                    .Select(value => value.SnapshotDependencyId),
                StringComparer.Ordinal);
            var dependencies = package.Snapshot?.Dependencies ?? Array.Empty<PageReferenceSnapshot>();
            var dependencyById = dependencies
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.Id))
                .GroupBy(value => value.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var hasUnavailablePreservedSourceResource = actions.Any(value =>
                value.Disposition == PageReferenceDisposition.PreserveExternal
                && dependencyById.TryGetValue(value.SnapshotDependencyId, out var dependency)
                && dependency.IsRenderableResource
                && (dependency.CaptureStatus == PageCaptureStatus.Failed
                    || string.IsNullOrWhiteSpace(dependency.ContentSha256)));
            if (!hasUnavailablePreservedSourceResource
                && actions.All(value => selectedIds.Contains(value.SnapshotDependencyId)))
            {
                return (package.Plan?.Replacements ?? Array.Empty<PageTextReplacement>()).ToList();
            }

            var actionById = actions
                .GroupBy(value => value.SnapshotDependencyId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var exactSources = new HashSet<string>(
                dependencies
                .Where(value => value != null
                    && !string.IsNullOrWhiteSpace(value.Id)
                    && !string.IsNullOrWhiteSpace(value.OriginalValue)
                    && actionById.ContainsKey(value.Id))
                .GroupBy(value => value.OriginalValue, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.All(value => selectedIds.Contains(value.Id)))
                .Select(group => group.Key),
                StringComparer.OrdinalIgnoreCase);

            return (package.Plan?.Replacements ?? Array.Empty<PageTextReplacement>())
                .Where(value => value != null && exactSources.Contains(value.Source ?? string.Empty))
                .ToList();
        }

        private static bool IsReplacementAction(PageReferenceAction action)
        {
            return action != null
                && !string.IsNullOrWhiteSpace(action.SnapshotDependencyId)
                && (action.Disposition == PageReferenceDisposition.PreserveExternal
                    || action.Disposition == PageReferenceDisposition.RewriteToTarget
                    || action.Disposition == PageReferenceDisposition.MaterializeAtTarget);
        }
    }
}
