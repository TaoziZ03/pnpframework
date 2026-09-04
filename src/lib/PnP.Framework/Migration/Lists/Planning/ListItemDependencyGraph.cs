using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Items;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Lists.Planning
{
    public enum ListItemDependencyKind
    {
        LookupValue = 1,
        FolderPath = 2
    }

    public sealed class ListItemDependencyEdge
    {
        public ListItemDependencyKind Kind { get; set; }

        public Guid ConsumerSourceWebId { get; set; }

        public Guid ConsumerSourceListId { get; set; }

        public int ConsumerSourceItemId { get; set; }

        public string ConsumerFieldInternalName { get; set; }

        public bool ConsumerFieldRequired { get; set; }

        public Guid ProviderSourceWebId { get; set; }

        public Guid ProviderSourceListId { get; set; }

        public int ProviderSourceItemId { get; set; }

        public string Key => KeyFor(
            Kind,
            ConsumerSourceListId,
            ConsumerSourceItemId,
            ConsumerFieldInternalName,
            ProviderSourceListId,
            ProviderSourceItemId);

        public string ConsumerItemKey => ItemKey(ConsumerSourceListId, ConsumerSourceItemId);

        public string ProviderItemKey => ItemKey(ProviderSourceListId, ProviderSourceItemId);

        public static string KeyFor(
            ListItemDependencyKind kind,
            Guid consumerSourceListId,
            int consumerSourceItemId,
            string consumerFieldInternalName,
            Guid providerSourceListId,
            int providerSourceItemId)
        {
            return kind + "\u001f"
                + consumerSourceListId.ToString("D") + "\u001f"
                + consumerSourceItemId + "\u001f"
                + (consumerFieldInternalName ?? string.Empty).ToUpperInvariant() + "\u001f"
                + providerSourceListId.ToString("D") + "\u001f"
                + providerSourceItemId;
        }

        public static string ItemKey(Guid sourceListId, int sourceItemId)
        {
            return sourceListId.ToString("D") + "\u001f" + sourceItemId;
        }
    }

    internal static class ListItemDependencyGraph
    {
        public static IList<ListItemDependencyEdge> Build(
            IEnumerable<ListDependencySnapshot> sources,
            IEnumerable<ListLookupDependency> lookupDependencies)
        {
            var lists = (sources ?? Array.Empty<ListDependencySnapshot>())
                .Where(value => value != null)
                .ToDictionary(value => value.SourceListId);
            var result = new Dictionary<string, ListItemDependencyEdge>(StringComparer.Ordinal);
            AddLookupEdges(lists, lookupDependencies, result);
            AddFolderEdges(lists.Values, result);
            return result.Values
                .OrderBy(value => value.ProviderSourceListId)
                .ThenBy(value => value.ProviderSourceItemId)
                .ThenBy(value => value.ConsumerSourceListId)
                .ThenBy(value => value.ConsumerSourceItemId)
                .ThenBy(value => value.ConsumerFieldInternalName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static IList<ListItemDependencyEdge> PotentialClosure(
            IEnumerable<ListItemDependencyEdge> edges,
            IEnumerable<string> seedItemKeys)
        {
            var values = (edges ?? Array.Empty<ListItemDependencyEdge>())
                .Where(value => value != null)
                .ToArray();
            var reachable = new HashSet<string>(
                seedItemKeys ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var selected = new Dictionary<string, ListItemDependencyEdge>(StringComparer.Ordinal);
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var edge in values.Where(value => reachable.Contains(value.ProviderItemKey)))
                {
                    if (!selected.ContainsKey(edge.Key))
                    {
                        selected.Add(edge.Key, edge);
                        changed = true;
                    }
                    if (reachable.Add(edge.ConsumerItemKey))
                    {
                        changed = true;
                    }
                }
            }
            return selected.Values
                .OrderBy(value => value.ProviderSourceListId)
                .ThenBy(value => value.ProviderSourceItemId)
                .ThenBy(value => value.ConsumerSourceListId)
                .ThenBy(value => value.ConsumerSourceItemId)
                .ThenBy(value => value.ConsumerFieldInternalName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddLookupEdges(
            IDictionary<Guid, ListDependencySnapshot> lists,
            IEnumerable<ListLookupDependency> lookupDependencies,
            IDictionary<string, ListItemDependencyEdge> result)
        {
            foreach (var dependency in lookupDependencies ?? Array.Empty<ListLookupDependency>())
            {
                if (dependency == null
                    || !lists.TryGetValue(dependency.SourceListId, out var consumer)
                    || !lists.TryGetValue(dependency.LookupListId, out var provider))
                {
                    continue;
                }
                var field = consumer.Fields.SingleOrDefault(value => value != null
                    && value.Id == dependency.FieldId
                    && value.SourceLookupListId == provider.SourceListId);
                if (field == null)
                {
                    continue;
                }
                var providerItemIds = new HashSet<int>(provider.Items
                    .Where(value => value != null)
                    .Select(value => value.SourceItemId));
                foreach (var item in consumer.Items.Where(value => value != null))
                {
                    foreach (var lookupId in item.Values
                                 .Where(value => value != null
                                     && string.Equals(value.InternalName, field.InternalName, StringComparison.OrdinalIgnoreCase)
                                     && (value.Kind == ListItemValueKind.Lookup
                                         || value.Kind == ListItemValueKind.LookupCollection))
                                 .SelectMany(value => value.LookupValues
                                     ?? Array.Empty<ListItemLookupValueSnapshot>())
                                 .Where(value => value != null && providerItemIds.Contains(value.LookupId))
                                 .Select(value => value.LookupId)
                                 .Distinct())
                    {
                        Add(result, new ListItemDependencyEdge
                        {
                            Kind = ListItemDependencyKind.LookupValue,
                            ConsumerSourceWebId = consumer.SourceWebId,
                            ConsumerSourceListId = consumer.SourceListId,
                            ConsumerSourceItemId = item.SourceItemId,
                            ConsumerFieldInternalName = field.InternalName,
                            ConsumerFieldRequired = field.Required,
                            ProviderSourceWebId = provider.SourceWebId,
                            ProviderSourceListId = provider.SourceListId,
                            ProviderSourceItemId = lookupId
                        });
                    }
                }
            }
        }

        private static void AddFolderEdges(
            IEnumerable<ListDependencySnapshot> lists,
            IDictionary<string, ListItemDependencyEdge> result)
        {
            foreach (var list in lists)
            {
                var folders = list.Items
                    .Where(value => value?.Document?.Kind == ListDocumentObjectKind.Folder)
                    .Select(value => new
                    {
                        Item = value,
                        Path = NormalizePath(value.Document.ServerRelativeUrl)
                    })
                    .Where(value => !string.IsNullOrWhiteSpace(value.Path))
                    .ToArray();
                foreach (var item in list.Items.Where(value => value?.Document != null))
                {
                    var path = NormalizePath(item.Document.ServerRelativeUrl);
                    var parent = folders
                        .Where(value => value.Item.SourceItemId != item.SourceItemId
                            && IsDescendant(path, value.Path))
                        .OrderByDescending(value => value.Path.Length)
                        .FirstOrDefault();
                    if (parent == null)
                    {
                        continue;
                    }
                    Add(result, new ListItemDependencyEdge
                    {
                        Kind = ListItemDependencyKind.FolderPath,
                        ConsumerSourceWebId = list.SourceWebId,
                        ConsumerSourceListId = list.SourceListId,
                        ConsumerSourceItemId = item.SourceItemId,
                        ProviderSourceWebId = list.SourceWebId,
                        ProviderSourceListId = list.SourceListId,
                        ProviderSourceItemId = parent.Item.SourceItemId
                    });
                }
            }
        }

        private static void Add(
            IDictionary<string, ListItemDependencyEdge> result,
            ListItemDependencyEdge edge)
        {
            if (!result.ContainsKey(edge.Key))
            {
                result.Add(edge.Key, edge);
            }
        }

        private static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            var normalized = Uri.UnescapeDataString(value).Replace('\\', '/').TrimEnd('/');
            return normalized.Length == 0 ? "/" : normalized;
        }

        private static bool IsDescendant(string candidate, string parent)
        {
            return !string.IsNullOrWhiteSpace(candidate)
                && !string.IsNullOrWhiteSpace(parent)
                && candidate.StartsWith(parent.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
