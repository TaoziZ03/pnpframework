using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Ingredients;
using PnP.Framework.Migration.Verification;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Comparison
{
    /// <summary>
    /// Lane handlers implement this narrow seam to contribute one fresh target
    /// observation for each material ingredient that they own. Registration order
    /// is not observable: the catalog orders by OrderGroup and HandlerId.
    /// </summary>
    public interface IPublishingPageIngredientVerificationContributor
    {
        string HandlerId { get; }

        int OrderGroup { get; }

        IngredientCompareObservation Observe(PublishingPageIngredientVerificationContext context);
    }

    public sealed class PublishingPageIngredientVerificationContext
    {
        internal PublishingPageIngredientVerificationContext(
            PublishingPageCompareRequest request,
            PageIngredientNode ingredient,
            PageIngredientAction action)
        {
            Request = request;
            Ingredient = ingredient;
            Action = action;
        }

        public PublishingPageCompareRequest Request { get; }

        public PublishingPageMigrationPackage Package => Request.Package;

        public PublishingPageImportReceipt ImportReceipt => Request.ImportReceipt;

        public RuntimeVerificationManifest RuntimeManifest => Package.Plan.RuntimeVerification;

        public RuntimeVerificationReceipt RuntimeReceipt => Request.RuntimeReceipt;

        public PageIngredientNode Ingredient { get; }

        public PageIngredientAction Action { get; }

        public string PlanDigest => Package.PlanDigest;

        public string TargetIdentity => Action.TargetIdentity;

        public RuntimeVerificationRequirement GetRuntimeRequirement(string requirementId)
        {
            if (string.IsNullOrWhiteSpace(requirementId))
            {
                throw new ArgumentException("A runtime requirement ID is required.", nameof(requirementId));
            }

            var matches = (RuntimeManifest?.Requirements ?? Array.Empty<RuntimeVerificationRequirement>())
                .Where(value => value != null
                    && string.Equals(value.Id, requirementId, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1)
            {
                throw new InvalidDataException(
                    "The sealed runtime manifest must contain the requested requirement exactly once.");
            }
            return matches[0];
        }

        public RuntimeVerificationResult GetRuntimeResult(string requirementId)
        {
            GetRuntimeRequirement(requirementId);
            var matches = (RuntimeReceipt?.Results ?? Array.Empty<RuntimeVerificationResult>())
                .Where(value => value != null
                    && string.Equals(value.RequirementId, requirementId, StringComparison.Ordinal))
                .ToList();
            if (matches.Count > 1)
            {
                throw new InvalidDataException(
                    "The runtime receipt contains duplicate results for the ingredient requirement.");
            }
            return matches.SingleOrDefault();
        }
    }

    /// <summary>
    /// Immutable, deterministic registry for ingredient verification contributors.
    /// It is intentionally independent of the built-in projection switch so future
    /// lane handlers can plug into Compare without editing that switch.
    /// </summary>
    public sealed class PublishingPageIngredientVerificationCatalog
    {
        private readonly IList<IPublishingPageIngredientVerificationContributor> contributors;
        private readonly PublishingPageIngredientHandlerCatalog ownerCatalog;

        public PublishingPageIngredientVerificationCatalog(
            PublishingPageIngredientHandlerCatalog ownerCatalog,
            IEnumerable<IPublishingPageIngredientVerificationContributor> contributors)
        {
            this.ownerCatalog = ownerCatalog ?? throw new ArgumentNullException(nameof(ownerCatalog));
            if (contributors == null)
            {
                throw new ArgumentNullException(nameof(contributors));
            }

            var values = contributors.ToList();
            if (values.Any(value => value == null
                || string.IsNullOrWhiteSpace(value.HandlerId)))
            {
                throw new InvalidDataException(
                    "Every ingredient verification contributor requires a stable handler ID.");
            }
            var duplicate = values
                .GroupBy(value => value.HandlerId, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() != 1);
            if (duplicate != null)
            {
                throw new InvalidDataException(
                    "Ingredient verification contributor handler IDs must be unique.");
            }
            var unknownHandler = values.FirstOrDefault(value =>
                ownerCatalog.Handlers.Count(handler => string.Equals(
                    handler.Descriptor.HandlerId,
                    value.HandlerId,
                    StringComparison.Ordinal)) != 1);
            if (unknownHandler != null)
            {
                throw new InvalidDataException(
                    "Every verification contributor must consume one registered product handler identity.");
            }

            this.contributors = values
                .OrderBy(value => value.OrderGroup)
                .ThenBy(value => value.HandlerId, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        public IList<string> HandlerIds => contributors.Select(value => value.HandlerId).ToList().AsReadOnly();

        public IList<IngredientCompareObservation> Contribute(PublishingPageCompareRequest request)
        {
            if (request?.Package?.Plan?.IngredientGraph?.Nodes == null
                || request.Package.Plan.IngredientActions == null
                || request.ImportReceipt == null
                || request.PlannedTargetIdentity == null)
            {
                throw new InvalidDataException(
                    "Ingredient verification contributions require a sealed plan, import receipt, and target identity.");
            }

            var observations = new List<IngredientCompareObservation>();
            foreach (var node in request.Package.Plan.IngredientGraph.Nodes
                         .Where(value => value?.HasContent == true)
                         .OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                var productCandidates = ownerCatalog.Handlers
                    .Where(value => value.Descriptor.Owns(node.Id))
                    .ToList();
                if (productCandidates.Count == 0)
                {
                    continue;
                }
                var primaryOwner = ownerCatalog.PrimaryOwnerRegistry.Resolve(
                    request.Package.Snapshot,
                    node);
                var productOwners = productCandidates
                    .Where(value => string.Equals(
                            value.Descriptor.Lane.LaneId,
                            primaryOwner.PrimaryOwnerLane,
                            StringComparison.Ordinal))
                    .ToList();
                if (productOwners.Count == 0)
                {
                    continue;
                }
                if (productOwners.Count != 1)
                {
                    throw new InvalidDataException(
                        "The product owner resolver returned multiple handlers for material ingredient: " + node.Id);
                }
                var owners = contributors.Where(value => string.Equals(
                    value.HandlerId,
                    productOwners[0].Descriptor.HandlerId,
                    StringComparison.Ordinal)).ToList();
                if (owners.Count == 0)
                {
                    continue;
                }
                if (owners.Count != 1)
                {
                    throw new InvalidDataException(
                        "A material ingredient has more than one verification contributor for its product owner: " + node.Id);
                }

                var actions = request.Package.Plan.IngredientActions
                    .Where(value => value != null
                        && string.Equals(value.IngredientId, node.Id, StringComparison.Ordinal))
                    .ToList();
                if (actions.Count != 1)
                {
                    throw new InvalidDataException(
                        "A contributed material ingredient must have exactly one sealed action: " + node.Id);
                }

                var context = new PublishingPageIngredientVerificationContext(request, node, actions[0]);
                var observation = owners[0].Observe(context);
                BindAndValidate(observation, context);
                observations.Add(observation);
            }
            return observations;
        }

        private static void BindAndValidate(
            IngredientCompareObservation observation,
            PublishingPageIngredientVerificationContext context)
        {
            if (observation == null)
            {
                throw new InvalidDataException(
                    "An ingredient verification contributor returned no observation.");
            }
            if (!string.Equals(observation.IngredientId, context.Ingredient.Id, StringComparison.Ordinal)
                || !observation.Material)
            {
                throw new InvalidDataException(
                    "A contributor must return one material observation for its exact source ingredient.");
            }
            if (observation.Lineage == null)
            {
                throw new InvalidDataException(
                    "A contributed observation requires source, action, target, plan, and evidence lineage.");
            }

            BindExact(
                observation.Lineage.SourceIngredientId,
                context.Ingredient.Id,
                "source ingredient");
            observation.Lineage.SourceIngredientId = context.Ingredient.Id;
            BindExact(observation.Lineage.ActionId, context.Action.ActionId, "action");
            observation.Lineage.ActionId = context.Action.ActionId;
            BindExact(observation.Lineage.TargetIdentity, context.Action.TargetIdentity, "target identity");
            observation.Lineage.TargetIdentity = context.Action.TargetIdentity;
            BindDigest(observation.Lineage.PlanDigestSha256, context.PlanDigest, "plan digest");
            observation.Lineage.PlanDigestSha256 = context.PlanDigest;

            if (string.IsNullOrWhiteSpace(observation.Lineage.SourceArtifactDigestSha256)
                && IsDigest(context.Ingredient.EvidenceDigest))
            {
                observation.Lineage.SourceArtifactDigestSha256 = context.Ingredient.EvidenceDigest;
            }
            RequireDigest(observation.Lineage.RequestedUrlHashSha256, "requested URL hash");
            RequireDigest(observation.Lineage.SourceArtifactDigestSha256, "source evidence digest");
            RequireDigest(observation.Lineage.TargetEvidenceDigestSha256, "target evidence digest");
            if (!string.IsNullOrWhiteSpace(context.Ingredient.EvidenceDigest))
            {
                BindDigest(
                    observation.Lineage.SourceArtifactDigestSha256,
                    context.Ingredient.EvidenceDigest,
                    "source evidence digest");
            }

            if (!IsEvidenceState(observation.TargetEvidenceState))
            {
                throw new InvalidDataException(
                    "A contributed observation requires a supported target evidence state.");
            }
            if (!observation.ObservedAtUtc.HasValue
                || observation.ObservedAtUtc.Value < context.ImportReceipt.StartedAtUtc)
            {
                throw new InvalidDataException(
                    "A contributed target observation must be a fresh read made after execution started.");
            }

            var evidenceRefs = new SortedSet<string>(
                observation.Lineage.EvidenceRefs ?? Array.Empty<string>(),
                StringComparer.Ordinal)
            {
                "sha256:" + observation.Lineage.SourceArtifactDigestSha256,
                "sha256:" + observation.Lineage.TargetEvidenceDigestSha256
            };
            var requirementIds = RuntimeRequirementIds(observation);
            foreach (var requirementId in requirementIds)
            {
                context.GetRuntimeRequirement(requirementId);
                var result = context.GetRuntimeResult(requirementId);
                if (result != null)
                {
                    RequireDigest(result.EvidenceArtifactSha256, "runtime evidence digest");
                    evidenceRefs.Add("sha256:" + result.EvidenceArtifactSha256);
                }
            }
            observation.RuntimeRequirementIds = requirementIds.Count == 0
                ? null
                : requirementIds;
            observation.RuntimeRequirementId = requirementIds.Count == 0
                ? null
                : requirementIds[0];
            observation.Lineage.EvidenceRefs = evidenceRefs.ToList();
        }

        internal static IList<string> RuntimeRequirementIds(IngredientCompareObservation observation)
        {
            var values = new SortedSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(observation?.RuntimeRequirementId))
            {
                values.Add(observation.RuntimeRequirementId);
            }
            foreach (var value in observation?.RuntimeRequirementIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new InvalidDataException("Runtime requirement IDs cannot be empty.");
                }
                values.Add(value);
            }
            return values.ToList();
        }

        internal static bool IsEvidenceState(string value)
        {
            return string.Equals(value, IngredientTargetEvidenceStates.Fresh, StringComparison.Ordinal)
                || string.Equals(value, IngredientTargetEvidenceStates.Partial, StringComparison.Ordinal)
                || string.Equals(value, IngredientTargetEvidenceStates.Missing, StringComparison.Ordinal)
                || string.Equals(value, IngredientTargetEvidenceStates.Denied, StringComparison.Ordinal)
                || string.Equals(value, IngredientTargetEvidenceStates.Expired, StringComparison.Ordinal)
                || string.Equals(value, IngredientTargetEvidenceStates.Unknown, StringComparison.Ordinal);
        }

        private static void BindExact(string supplied, string expected, string name)
        {
            if (!string.IsNullOrWhiteSpace(supplied)
                && !string.Equals(supplied, expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The contributed " + name + " does not match the sealed plan.");
            }
            if (string.IsNullOrWhiteSpace(expected))
            {
                throw new InvalidDataException(
                    "The sealed " + name + " is missing.");
            }
        }

        private static void BindDigest(string supplied, string expected, string name)
        {
            if (!IsDigest(expected)
                || (!string.IsNullOrWhiteSpace(supplied)
                    && !string.Equals(supplied, expected, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "The contributed " + name + " does not match the sealed SHA-256 value.");
            }
        }

        private static void RequireDigest(string value, string name)
        {
            if (!IsDigest(value))
            {
                throw new InvalidDataException(
                    "The contributed " + name + " must be a SHA-256 digest.");
            }
        }

        private static bool IsDigest(string value)
        {
            return value != null
                && value.Length == 64
                && value.All(character => (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'));
        }
    }
}
