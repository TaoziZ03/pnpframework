using PnP.Framework.Migration.Lists.Capture;
using PnP.Framework.Migration.Lists.Planning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PnP.Framework.Migration.Schema.ContentTypes;
using PnP.Framework.Migration.Schema.ContentTypes.Packaging;
using PnP.Framework.Migration.Features;
using PnP.Framework.Migration.Lists.Items;

namespace PnP.Framework.Migration.Lists.Packaging
{
    internal static class ListMigrationPlanValidator
    {
        public static void Validate(IEnumerable<ListDependencySnapshot> snapshots, ListMigrationPlanSet plan)
        {
            Validate(snapshots, plan, null);
        }

        public static void Validate(
            IEnumerable<ListDependencySnapshot> snapshots,
            ListMigrationPlanSet plan,
            DroppedLookupValuePolicy droppedLookupValuePolicy)
        {
            DroppedLookupValuePolicy.Validate(droppedLookupValuePolicy);
            var sources = (snapshots ?? Enumerable.Empty<ListDependencySnapshot>()).ToArray();
            if (sources.Length == 0 && plan == null)
            {
                return;
            }
            if (plan == null || plan.Lists == null || plan.OrderedSourceListIds == null || plan.Issues == null)
            {
                throw new InvalidDataException("Captured List dependencies require a complete List migration plan.");
            }
            var sourceIds = new HashSet<Guid>(sources.Select(value => value.SourceListId));
            var sourceById = sources.ToDictionary(value => value.SourceListId);
            var plannedIds = new HashSet<Guid>(plan.Lists.Select(value => value == null ? Guid.Empty : value.SourceListId));
            if (plan.Lists.Any(value => value == null) || sourceIds.Count != plannedIds.Count || !sourceIds.SetEquals(plannedIds))
            {
                throw new InvalidDataException("The List migration plan must contain exactly one plan for every captured List dependency.");
            }
            foreach (var list in plan.Lists)
            {
                if (list.Fields == null || list.Views == null || list.ViewRenderingResources == null
                    || list.SiteContentTypes == null || list.RequiredFeatures == null || list.Issues == null
                    || string.IsNullOrWhiteSpace(list.OriginalIdentifier) || string.IsNullOrWhiteSpace(list.TargetSiteCollectionUrl)
                    || !string.Equals(ListMigrationPlanFactory.ComputePlanDigest(list), list.PlanDigest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A List materialization plan is incomplete or its semantic digest differs: " + list.SourceListId.ToString("D"));
                }
                if (list.Disposition != ListMaterializationDisposition.Block && (list.TargetProbe == null || !list.TargetProbe.IsAdmitted))
                {
                    throw new InvalidDataException("An executable List plan has no admitted target probe: " + list.SourceListId.ToString("D"));
                }
                foreach (var contentType in list.SiteContentTypes)
                {
                    if (contentType == null || contentType.Schema == null
                        || !string.Equals(ContentTypeClosurePlanner.ComputeDigest(contentType), contentType.PlanDigest, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("A site content type closure node is incomplete or its digest differs.");
                    }
                    ContentTypeSchemaContractValidator.ValidatePlan(contentType.Schema);
                    if (list.Disposition != ListMaterializationDisposition.Block
                        && !contentType.DeferredUntilTopologyMaterialization
                        && (contentType.TargetAdmission == null || !contentType.TargetAdmission.IsEligible))
                    {
                        throw new InvalidDataException("An executable site content type plan has no admitted target analysis: " + contentType.Schema.ContentTypeId);
                    }
                }
                ValidateViewRenderingResources(sourceById[list.SourceListId], list);
                ValidateFeatures(sourceById[list.SourceListId], list);
                ValidateProtectedDocumentExclusions(sourceById[list.SourceListId], list);
            }
            ValidateDroppedLookupValueDependencies(sources, plan.Lists, droppedLookupValuePolicy);
            if (!string.Equals(ListMigrationPlanFactory.ComputeSetDigest(plan), plan.PlanDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The List migration plan-set digest differs from its sealed content.");
            }
        }

        private static void ValidateViewRenderingResources(ListDependencySnapshot source, ListMaterializationPlan list)
        {
            var expected = source.ViewRenderingResources
                .ToDictionary(value => value.Id, StringComparer.Ordinal);
            var actual = list.ViewRenderingResources
                .ToDictionary(value => value == null ? string.Empty : value.SourceResourceId, StringComparer.Ordinal);
            if (actual.ContainsKey(string.Empty)
                || expected.Count != actual.Count
                || !new HashSet<string>(expected.Keys, StringComparer.Ordinal).SetEquals(actual.Keys))
            {
                throw new InvalidDataException("The View rendering-resource plan does not exactly cover the captured resource inventory: "
                    + list.SourceListId.ToString("D") + ".");
            }
            foreach (var pair in expected)
            {
                var planned = actual[pair.Key];
                if (!string.Equals(planned.SourceAbsoluteUrl, pair.Value.SourceAbsoluteUrl, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(planned.SourceServerRelativeUrl, pair.Value.SourceServerRelativeUrl, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(planned.SourceArtifact?.Sha256, pair.Value.Artifact?.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("A View rendering-resource plan differs from its sealed source evidence: " + pair.Key + ".");
                }
            }
        }

        private static void ValidateFeatures(ListDependencySnapshot source, ListMaterializationPlan list)
        {
            var expected = ContentTypeRuntimeCatalog.CreateFeatureRequirements(
                source.ContentTypes.Select(value => value.ParentId),
                source.SiteContentTypes,
                list.TargetSiteCollectionUrl).ToDictionary(value => value.FeatureId);
            var actual = list.RequiredFeatures.ToDictionary(value => value == null ? Guid.Empty : value.FeatureId);
            if (actual.ContainsKey(Guid.Empty) || expected.Count != actual.Count || !new HashSet<Guid>(expected.Keys).SetEquals(actual.Keys))
            {
                throw new InvalidDataException("The List platform-feature plan does not exactly cover its conditional target-runtime content types: "
                    + list.SourceListId.ToString("D") + ".");
            }
            foreach (var pair in expected)
            {
                var observed = actual[pair.Key];
                var semanticMatch = observed.Scope == pair.Value.Scope
                    && observed.DependencyOrder == pair.Value.DependencyOrder
                    && observed.Disposition == pair.Value.Disposition
                    && string.Equals(observed.Name, pair.Value.Name, StringComparison.Ordinal)
                    && string.Equals(observed.TargetWebUrl, pair.Value.TargetWebUrl, StringComparison.OrdinalIgnoreCase)
                    && observed.DependsOnFeatureIds.SequenceEqual(pair.Value.DependsOnFeatureIds)
                    && observed.RequiredByContentTypeIds.SequenceEqual(pair.Value.RequiredByContentTypeIds, StringComparer.OrdinalIgnoreCase)
                    && observed.ExpectedContentTypeIds.SequenceEqual(pair.Value.ExpectedContentTypeIds, StringComparer.OrdinalIgnoreCase);
                if (!semanticMatch)
                {
                    throw new InvalidDataException("The platform-feature plan differs from the captured content-type requirement: "
                        + pair.Key.ToString("D") + ".");
                }
                if (list.Disposition != ListMaterializationDisposition.Block
                    && (observed.TargetProbe == null || !observed.TargetProbe.IsAdmitted))
                {
                    throw new InvalidDataException("An executable platform-feature plan has no admitted target probe: "
                        + pair.Key.ToString("D") + ".");
                }
            }
        }

        private static void ValidateProtectedDocumentExclusions(
            ListDependencySnapshot source,
            ListMaterializationPlan list)
        {
            var expected = source.Items
                .Where(value => value?.Document?.Kind == ListDocumentObjectKind.File
                    && value.Document.CaptureDecision?.IsMetadataOnly == true)
                .ToDictionary(value => value.SourceItemId);
            var actual = (list.ApprovedProtectedDocumentExclusions
                    ?? Array.Empty<ListProtectedDocumentExclusionPlan>())
                .GroupBy(value => value == null ? 0 : value.SourceItemId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            if (actual.ContainsKey(0)
                || actual.Any(value => value.Value.Length != 1)
                || expected.Count != actual.Count
                || !new HashSet<int>(expected.Keys).SetEquals(actual.Keys))
            {
                throw new InvalidDataException(
                    "The protected-document exclusion plan does not exactly cover the metadata-only source decisions for List "
                    + list.SourceListId.ToString("D") + ".");
            }

            foreach (var pair in expected)
            {
                var document = pair.Value.Document;
                var decision = document.CaptureDecision;
                var planned = actual[pair.Key][0];
                var expectedTargetPath = list.TargetRootFolderServerRelativeUrl.TrimEnd('/')
                    + document.ServerRelativeUrl.Substring(source.RootFolderServerRelativeUrl.TrimEnd('/').Length);
                if (!string.Equals(planned.SourceServerRelativeUrl, document.ServerRelativeUrl, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(planned.TargetServerRelativeUrl, expectedTargetPath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(planned.PolicyId, decision.PolicyId, StringComparison.Ordinal)
                    || !string.Equals(planned.CaptureDecisionDigest, decision.DecisionDigest, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(planned.ReasonCode, decision.ReasonCode, StringComparison.Ordinal)
                    || !string.Equals(planned.Reason, decision.Reason, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "A protected-document exclusion differs from its sealed source capture decision for item "
                        + pair.Key + ".");
                }
            }
        }

        private static void ValidateDroppedLookupValueDependencies(
            IEnumerable<ListDependencySnapshot> sources,
            IEnumerable<ListMaterializationPlan> plans,
            DroppedLookupValuePolicy policy)
        {
            var sourceByList = sources.ToDictionary(value => value.SourceListId);
            var planByList = plans.ToDictionary(value => value.SourceListId);
            var excludedByList = planByList.ToDictionary(
                value => value.Key,
                value => new HashSet<int>((value.Value.ApprovedProtectedDocumentExclusions
                    ?? Array.Empty<ListProtectedDocumentExclusionPlan>())
                    .Select(exclusion => exclusion.SourceItemId)));
            var expected = new Dictionary<string, ExpectedDroppedLookupDependency>(StringComparer.Ordinal);
            foreach (var consumer in sourceByList.Values)
            {
                var lookupFields = consumer.Fields
                    .Where(value => value?.SourceLookupListId.HasValue == true)
                    .ToDictionary(value => value.InternalName, StringComparer.OrdinalIgnoreCase);
                foreach (var item in consumer.Items.Where(value => value != null))
                {
                    foreach (var value in item.Values.Where(value => value != null
                                 && (value.Kind == ListItemValueKind.Lookup
                                     || value.Kind == ListItemValueKind.LookupCollection)))
                    {
                        if (!lookupFields.TryGetValue(value.InternalName, out var field)
                            || !sourceByList.TryGetValue(field.SourceLookupListId.Value, out var provider)
                            || !excludedByList.TryGetValue(provider.SourceListId, out var excludedIds))
                        {
                            continue;
                        }
                        foreach (var droppedId in value.LookupValues
                                     .Where(lookup => lookup != null && excludedIds.Contains(lookup.LookupId))
                                     .Select(lookup => lookup.LookupId)
                                     .Distinct())
                        {
                            var entry = new ExpectedDroppedLookupDependency
                            {
                                ConsumerListId = consumer.SourceListId,
                                ConsumerItemId = item.SourceItemId,
                                ConsumerFieldInternalName = value.InternalName,
                                ProviderWebId = provider.SourceWebId,
                                ProviderListId = provider.SourceListId,
                                ProviderItemId = droppedId
                            };
                            expected.Add(entry.Key, entry);
                        }
                    }
                }
            }

            var actual = new Dictionary<string, ListDroppedLookupValueDependencyPlan>(StringComparer.Ordinal);
            foreach (var list in plans)
            {
                foreach (var value in list.DroppedLookupValueDependencies
                             ?? Array.Empty<ListDroppedLookupValueDependencyPlan>())
                {
                    if (value == null)
                    {
                        throw new InvalidDataException("A dropped lookup-value dependency plan is null.");
                    }
                    var key = ExpectedDroppedLookupDependency.KeyFor(
                        list.SourceListId,
                        value.ConsumerSourceItemId,
                        value.ConsumerFieldInternalName,
                        value.LookupSourceListId,
                        value.DroppedLookupSourceItemId);
                    if (actual.ContainsKey(key)
                        || !Enum.IsDefined(typeof(DroppedLookupValueDisposition), value.Disposition)
                        || string.IsNullOrWhiteSpace(value.Reason)
                        || value.Disposition != DroppedLookupValueDisposition.NeedsPolicyDecision
                            && string.IsNullOrWhiteSpace(value.PolicyId)
                        || value.Disposition != (policy?.Disposition
                            ?? DroppedLookupValueDisposition.NeedsPolicyDecision)
                        || !string.Equals(value.PolicyId, policy?.PolicyId, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("A dropped lookup-value dependency plan is duplicate, incomplete, or has no reviewed policy.");
                    }
                    actual.Add(key, value);
                }
            }
            if (!new HashSet<string>(expected.Keys, StringComparer.Ordinal).SetEquals(actual.Keys))
            {
                throw new InvalidDataException(
                    "The dropped lookup-value dependency plans do not exactly cover captured lookup values that reference approved protected-document exclusions.");
            }
            foreach (var pair in expected)
            {
                var observed = actual[pair.Key];
                if (observed.LookupSourceWebId != pair.Value.ProviderWebId
                    || observed.LookupSourceListId != pair.Value.ProviderListId)
                {
                    throw new InvalidDataException(
                        "A dropped lookup-value dependency plan differs from its captured provider identity.");
                }
            }
        }

        private sealed class ExpectedDroppedLookupDependency
        {
            public Guid ConsumerListId { get; set; }

            public int ConsumerItemId { get; set; }

            public string ConsumerFieldInternalName { get; set; }

            public Guid ProviderWebId { get; set; }

            public Guid ProviderListId { get; set; }

            public int ProviderItemId { get; set; }

            public string Key => KeyFor(
                ConsumerListId,
                ConsumerItemId,
                ConsumerFieldInternalName,
                ProviderListId,
                ProviderItemId);

            public static string KeyFor(
                Guid consumerListId,
                int consumerItemId,
                string consumerFieldInternalName,
                Guid providerListId,
                int providerItemId)
            {
                return consumerListId.ToString("D") + "\u001f"
                    + consumerItemId + "\u001f"
                    + (consumerFieldInternalName ?? string.Empty).ToUpperInvariant() + "\u001f"
                    + providerListId.ToString("D") + "\u001f"
                    + providerItemId;
            }
        }
    }
}
