using PnP.Framework.Migration.Lists.Items.Protection;
using PnP.Framework.Migration.Pages.Ingredients;
using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    internal static class PublishingPageProtectedAssetCandidatePolicy
    {
        public static IList<PageIngredientActionCandidate> Candidates(
            PublishingPageProtectedAssetContext context,
            PageIngredientKind kind)
        {
            var microsoft = context.Policy.Profile == ProtectedAssetCaptureProfile.MicrosoftTenantMetadataOnly;
            var payloadCaptured = context.Document.Content?.Artifact != null;
            var result = new List<PageIngredientActionCandidate>();
            if (kind == PageIngredientKind.ProtectedAsset)
            {
                if (!microsoft && payloadCaptured)
                {
                    result.Add(Candidate("reproduce", IngredientSelectableAction.Reproduce, IngredientActionScope.Subtree, true,
                        context, "ProtectedAssetFidelityAllowed", "reproduce-protected-asset", IngredientDependencyEffect.Retained, IngredientComparisonRule.Exact));
                }
                result.Add(Candidate("evidence-only", IngredientSelectableAction.EvidenceOnly, IngredientActionScope.Self, microsoft,
                    context, "ProtectedAssetBoundaryRetained", "retain-protected-asset-evidence", IngredientDependencyEffect.SatisfiedByPolicy, IngredientComparisonRule.EvidenceOnly));
                result.Add(Candidate("exclude-payload", IngredientSelectableAction.Exclude, IngredientActionScope.PayloadOnly, false,
                    context, "ProtectedAssetPayloadExcluded", "exclude-protected-payload", IngredientDependencyEffect.SatisfiedByPolicy, IngredientComparisonRule.ExpectedAbsent));
                result.Add(Candidate("defer", IngredientSelectableAction.Defer, IngredientActionScope.Self, !microsoft && !payloadCaptured,
                    context, "ProtectedAssetDecisionDeferred", "none", IngredientDependencyEffect.Deferred, IngredientComparisonRule.NotEvaluated));
            }
            else if (kind == PageIngredientKind.DocumentIdentity)
            {
                if (!microsoft)
                {
                    result.Add(Candidate("reproduce", IngredientSelectableAction.Reproduce, IngredientActionScope.Self, true,
                        context, "DocumentIdentityFidelityAllowed", "reproduce-document-identity", IngredientDependencyEffect.Retained, IngredientComparisonRule.Exact));
                }
                result.Add(Candidate("evidence-only", IngredientSelectableAction.EvidenceOnly, IngredientActionScope.Self, microsoft,
                    context, "DocumentIdentityEvidenceOnly", "retain-document-identity-evidence", IngredientDependencyEffect.SatisfiedByPolicy, IngredientComparisonRule.EvidenceOnly));
                result.Add(Candidate("defer", IngredientSelectableAction.Defer, IngredientActionScope.Self, false,
                    context, "DocumentIdentityDecisionDeferred", "none", IngredientDependencyEffect.Deferred, IngredientComparisonRule.NotEvaluated));
            }
            else if (kind == PageIngredientKind.BinaryPayload)
            {
                if (!microsoft && payloadCaptured)
                {
                    result.Add(Candidate("reproduce", IngredientSelectableAction.Reproduce, IngredientActionScope.Self, true,
                        context, "ProtectedPayloadFidelityAllowed", "copy-captured-protected-payload-create-only", IngredientDependencyEffect.Retained, IngredientComparisonRule.Exact));
                }
                result.Add(Candidate("exclude", IngredientSelectableAction.Exclude, IngredientActionScope.Self, microsoft,
                    context, microsoft ? "MicrosoftProtectedAssetExportDenied" : "ProtectedPayloadApprovedExclusion",
                    "no-target-mutation", IngredientDependencyEffect.SatisfiedByPolicy, IngredientComparisonRule.ExpectedAbsent));
                result.Add(Candidate("evidence-only", IngredientSelectableAction.EvidenceOnly, IngredientActionScope.Self, false,
                    context, "ProtectedPayloadEvidenceOnly", "retain-payload-metadata-evidence", IngredientDependencyEffect.SatisfiedByPolicy, IngredientComparisonRule.EvidenceOnly));
                result.Add(Candidate("defer", IngredientSelectableAction.Defer, IngredientActionScope.Self, !microsoft && !payloadCaptured,
                    context, "ProtectedPayloadDecisionDeferred", "none", IngredientDependencyEffect.Deferred, IngredientComparisonRule.NotEvaluated));
            }
            else if (kind == PageIngredientKind.InformationProtectionRelationship)
            {
                if (!microsoft && context.Document.InformationProtection?.State == ProtectedAssetProtectionState.Protected)
                {
                    result.Add(Candidate("reproduce", IngredientSelectableAction.Reproduce, IngredientActionScope.Self, true,
                        context, "InformationProtectionFidelityAllowed", "reproduce-information-protection-relationship", IngredientDependencyEffect.Retained, IngredientComparisonRule.Exact));
                }
                result.Add(Candidate("evidence-only", IngredientSelectableAction.EvidenceOnly, IngredientActionScope.Self, microsoft,
                    context, "InformationProtectionRelationshipEvidenceOnly", "retain-information-protection-evidence", IngredientDependencyEffect.SatisfiedByPolicy, IngredientComparisonRule.EvidenceOnly));
                result.Add(Candidate("defer", IngredientSelectableAction.Defer, IngredientActionScope.Self,
                    !microsoft && context.Document.InformationProtection?.State != ProtectedAssetProtectionState.Protected,
                    context, "InformationProtectionDecisionDeferred", "none", IngredientDependencyEffect.Deferred, IngredientComparisonRule.NotEvaluated));
            }
            var ingredientId = context.IngredientId(kind);
            return result.Select(value =>
            {
                value.CandidateActionId = ingredientId + ":" + value.CandidateActionId;
                return value;
            }).ToList();
        }

        private static PageIngredientActionCandidate Candidate(
            string suffix,
            IngredientSelectableAction action,
            IngredientActionScope scope,
            bool isDefault,
            PublishingPageProtectedAssetContext context,
            string reasonCode,
            string realization,
            IngredientDependencyEffect dependencyEffect,
            IngredientComparisonRule comparisonRule)
        {
            return new PageIngredientActionCandidate
            {
                CandidateActionId = suffix,
                Action = action,
                Scope = scope,
                Capability = action == IngredientSelectableAction.Defer ? IngredientCapability.Unknown : IngredientCapability.Available,
                Realization = realization,
                PolicyId = context.Policy.PolicyId,
                PolicyVersion = "1",
                ReasonCode = reasonCode,
                Reason = reasonCode,
                DependencyEffect = dependencyEffect,
                ComparisonRule = comparisonRule,
                IsDefault = isDefault
            };
        }
    }
}
