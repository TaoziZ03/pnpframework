using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.ContentTypes;
using PnP.Framework.Migration.Lists.Fields;
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

        public bool ConsumerListFieldRequired { get; set; }

        public string ConsumerContentTypeId { get; set; }

        public bool ConsumerContentTypeResolved { get; set; }

        public bool ConsumerContentTypeFieldLinkRequired { get; set; }

        public bool ConsumerEffectiveRequired { get; set; }

        public bool ConsumerRequirementKnown { get; set; }

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
            var adjacency = values
                .GroupBy(value => value.ProviderItemKey, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.Ordinal);
            var pendingProviders = new Queue<string>(reachable.OrderBy(value => value, StringComparer.Ordinal));
            var processedProviders = new HashSet<string>(StringComparer.Ordinal);
            while (pendingProviders.Count > 0)
            {
                var providerItemKey = pendingProviders.Dequeue();
                if (!processedProviders.Add(providerItemKey)
                    || !adjacency.TryGetValue(providerItemKey, out var outgoing))
                {
                    continue;
                }
                foreach (var edge in outgoing)
                {
                    if (!selected.ContainsKey(edge.Key))
                    {
                        selected.Add(edge.Key, edge);
                    }
                    if (reachable.Add(edge.ConsumerItemKey))
                    {
                        pendingProviders.Enqueue(edge.ConsumerItemKey);
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
                    var requirement = ResolveRequirement(consumer, item, field);
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
                            ConsumerListFieldRequired = requirement.ListFieldRequired,
                            ConsumerContentTypeId = requirement.ContentTypeId,
                            ConsumerContentTypeResolved = requirement.ContentTypeResolved,
                            ConsumerContentTypeFieldLinkRequired = requirement.ContentTypeFieldLinkRequired,
                            ConsumerEffectiveRequired = requirement.EffectiveRequired,
                            ConsumerRequirementKnown = requirement.RequirementKnown,
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
                var foldersByPath = new Dictionary<string, ListItemSnapshot>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in list.Items
                    .Where(value => value?.Document?.Kind == ListDocumentObjectKind.Folder)
                    .Select(value => new
                    {
                        Item = value,
                        Path = NormalizePath(value.Document.ServerRelativeUrl)
                    })
                    .Where(value => !string.IsNullOrWhiteSpace(value.Path)))
                {
                    if (!foldersByPath.ContainsKey(folder.Path))
                    {
                        foldersByPath.Add(folder.Path, folder.Item);
                    }
                    else if (foldersByPath[folder.Path]?.SourceItemId != folder.Item.SourceItemId)
                    {
                        foldersByPath[folder.Path] = null;
                    }
                }
                foreach (var item in list.Items.Where(value => value?.Document != null))
                {
                    var path = NormalizePath(item.Document.ServerRelativeUrl);
                    var parent = FindNearestParentFolder(
                        path,
                        item.SourceItemId,
                        foldersByPath);
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
                        ProviderSourceItemId = parent.SourceItemId,
                        ConsumerRequirementKnown = true
                    });
                }
            }
        }

        private static RequirementEvidence ResolveRequirement(
            ListDependencySnapshot consumer,
            ListItemSnapshot item,
            ListFieldSnapshot field)
        {
            var result = new RequirementEvidence
            {
                ListFieldRequired = field.Required,
                EffectiveRequired = field.Required,
                RequirementKnown = field.Required
            };
            var contentTypeValues = (item.Values ?? Array.Empty<ListItemValueSnapshot>())
                .Where(value => value != null
                    && string.Equals(value.InternalName, "ContentTypeId", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (contentTypeValues.Length != 1
                || contentTypeValues[0].Availability != PnP.Framework.Migration.Evidence.EvidenceAvailability.Captured)
            {
                return result;
            }
            var contentTypeValue = contentTypeValues[0];
            result.ContentTypeId = contentTypeValue.ScalarValue ?? contentTypeValue.RawValue;
            if (string.IsNullOrWhiteSpace(result.ContentTypeId))
            {
                return result;
            }
            var matches = (consumer.ContentTypes ?? Array.Empty<ListContentTypeSnapshot>())
                .Where(value => value != null
                    && string.Equals(value.Id, result.ContentTypeId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1
                || !ListContentTypeEvidence.IsCaptured(matches[0])
                || matches[0].FieldLinks == null)
            {
                return result;
            }
            var fieldLinks = matches[0].FieldLinks
                .Where(value => value != null
                    && (value.FieldId == field.Id
                        || string.Equals(value.InternalName, field.InternalName, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (fieldLinks.Length > 1)
            {
                return result;
            }
            result.ContentTypeResolved = true;
            result.ContentTypeFieldLinkRequired = fieldLinks.Length == 1 && fieldLinks[0].Required;
            result.EffectiveRequired = result.ListFieldRequired || result.ContentTypeFieldLinkRequired;
            result.RequirementKnown = true;
            return result;
        }

        private static ListItemSnapshot FindNearestParentFolder(
            string candidatePath,
            int candidateItemId,
            IReadOnlyDictionary<string, ListItemSnapshot> foldersByPath)
        {
            var parentPath = ParentPath(candidatePath);
            while (!string.IsNullOrWhiteSpace(parentPath))
            {
                if (foldersByPath.TryGetValue(parentPath, out var parent)
                    && parent != null
                    && parent.SourceItemId != candidateItemId)
                {
                    return parent;
                }
                parentPath = ParentPath(parentPath);
            }
            return null;
        }

        private static string ParentPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "/", StringComparison.Ordinal))
            {
                return null;
            }
            var separator = value.LastIndexOf('/');
            if (separator <= 0)
            {
                return "/";
            }
            return value.Substring(0, separator);
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

        private sealed class RequirementEvidence
        {
            public bool ListFieldRequired { get; set; }

            public string ContentTypeId { get; set; }

            public bool ContentTypeResolved { get; set; }

            public bool ContentTypeFieldLinkRequired { get; set; }

            public bool EffectiveRequired { get; set; }

            public bool RequirementKnown { get; set; }
        }
    }
}
