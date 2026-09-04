using PnP.Framework.Migration.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    internal static class PageIngredientActionSelectionPolicy
    {
        public static PageIngredientAction Select(
            string ingredientId,
            IEnumerable<PageIngredientActionCandidate> candidates,
            PageIngredientActionSelectionRequest request,
            string snapshotDigest,
            PageIngredientSelectionAudit defaultAudit = null)
        {
            var candidateList = (candidates ?? Enumerable.Empty<PageIngredientActionCandidate>())
                .OrderBy(value => value.CandidateActionId, StringComparer.Ordinal)
                .ToList();
            ValidateCandidateSet(ingredientId, candidateList);
            if (request != null && !string.Equals(request.SnapshotDigest, snapshotDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The ingredient action selection is stale: its snapshot digest does not match the immutable source snapshot.");
            }
            if (request != null && !string.Equals(request.IngredientId, ingredientId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The ingredient action selection names a different ingredient.");
            }

            var selected = request == null
                ? candidateList.SingleOrDefault(value => value.IsDefault)
                : candidateList.SingleOrDefault(value => string.Equals(value.CandidateActionId, request.CandidateActionId, StringComparison.Ordinal));
            if (selected == null)
            {
                throw new InvalidDataException(request == null
                    ? "The policy-filtered candidate set has no default action. Supply an explicit selection."
                    : "The selected ingredient action is not allowed by the active policy.");
            }

            var selectedAction = new PageIngredientSelectedAction
            {
                IngredientId = ingredientId,
                CandidateActionId = selected.CandidateActionId,
                Action = selected.Action,
                Scope = selected.Scope,
                SnapshotDigest = snapshotDigest,
                SelectedBy = request?.SelectedBy ?? defaultAudit?.SelectedBy ?? "policy-default",
                SelectedAtUtc = request?.SelectedAtUtc ?? defaultAudit?.SelectedAtUtc,
                ApprovalReference = request?.ApprovalReference ?? defaultAudit?.ApprovalReference
            };
            var receipt = new PageIngredientActionSelectionReceipt
            {
                IngredientId = ingredientId,
                SnapshotDigest = snapshotDigest,
                CandidateSetDigest = ComputeCandidateSetDigest(candidateList),
                CandidateActionId = selected.CandidateActionId,
                Action = selected.Action,
                PolicyId = selected.PolicyId,
                PolicyVersion = selected.PolicyVersion,
                ReasonCode = selected.ReasonCode,
                Scope = selected.Scope,
                DependencyEffect = selected.DependencyEffect,
                ComparisonRule = selected.ComparisonRule,
                SelectedBy = selectedAction.SelectedBy,
                SelectedAtUtc = selectedAction.SelectedAtUtc,
                ApprovalReference = selectedAction.ApprovalReference
            };
            receipt.ReceiptDigest = ComputeReceiptDigest(receipt);
            return new PageIngredientAction
            {
                ActionId = "action:" + ingredientId,
                IngredientId = ingredientId,
                Capability = selected.Capability,
                Disposition = ToDisposition(selected.Action),
                Realization = selected.Realization,
                PolicyId = selected.PolicyId,
                PolicyVersion = selected.PolicyVersion,
                Reason = selected.Reason,
                CandidateActions = candidateList,
                SelectedAction = selectedAction,
                SelectionReceipt = receipt,
                TerminalStatus = ToTerminalStatus(selected.Action)
            };
        }

        public static void Validate(
            PageIngredientAction action,
            IEnumerable<PageIngredientActionCandidate> expectedCandidates,
            string snapshotDigest)
        {
            if (action == null || action.SelectedAction == null || action.SelectionReceipt == null)
            {
                throw new InvalidDataException("A selectable ingredient action is missing its selected action or selection receipt.");
            }
            var candidates = (expectedCandidates ?? Enumerable.Empty<PageIngredientActionCandidate>())
                .OrderBy(value => value.CandidateActionId, StringComparer.Ordinal)
                .ToList();
            ValidateCandidateSet(action.IngredientId, candidates);
            if (!CanonicalEquals(candidates, action.CandidateActions))
            {
                throw new InvalidDataException("The sealed candidate actions differ from the active policy-filtered candidate set.");
            }
            if (!string.Equals(action.SelectedAction.SnapshotDigest, snapshotDigest, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(action.SelectionReceipt.SnapshotDigest, snapshotDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The selected ingredient action is stale for the source snapshot.");
            }
            var candidate = candidates.SingleOrDefault(value => string.Equals(
                value.CandidateActionId,
                action.SelectedAction.CandidateActionId,
                StringComparison.Ordinal));
            if (candidate == null
                || action.SelectedAction.Action != candidate.Action
                || action.SelectedAction.Scope != candidate.Scope
                || action.Disposition != ToDisposition(candidate.Action)
                || action.TerminalStatus != ToTerminalStatus(candidate.Action)
                || !string.Equals(action.PolicyId, candidate.PolicyId, StringComparison.Ordinal)
                || !string.Equals(action.PolicyVersion, candidate.PolicyVersion, StringComparison.Ordinal)
                || !string.Equals(action.SelectionReceipt.CandidateSetDigest, ComputeCandidateSetDigest(candidates), StringComparison.OrdinalIgnoreCase)
                || !ReceiptMatches(action.SelectionReceipt, action.SelectedAction, candidate)
                || !string.Equals(action.SelectionReceipt.ReceiptDigest, ComputeReceiptDigest(action.SelectionReceipt), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The selected ingredient action or its receipt was stale, tampered with, or no longer allowed by policy.");
            }
        }

        public static string ComputeCandidateSetDigest(IEnumerable<PageIngredientActionCandidate> candidates)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                (candidates ?? Enumerable.Empty<PageIngredientActionCandidate>())
                    .OrderBy(value => value.CandidateActionId, StringComparer.Ordinal)
                    .ToList()));
        }

        public static string ComputeReceiptDigest(PageIngredientActionSelectionReceipt receipt)
        {
            var value = receipt.ReceiptDigest;
            receipt.ReceiptDigest = null;
            try
            {
                return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(receipt));
            }
            finally
            {
                receipt.ReceiptDigest = value;
            }
        }

        private static void ValidateCandidateSet(string ingredientId, IList<PageIngredientActionCandidate> candidates)
        {
            if (string.IsNullOrWhiteSpace(ingredientId)
                || candidates.Count == 0
                || candidates.Any(value => value == null
                    || string.IsNullOrWhiteSpace(value.CandidateActionId)
                    || string.IsNullOrWhiteSpace(value.PolicyId)
                    || string.IsNullOrWhiteSpace(value.PolicyVersion)
                    || string.IsNullOrWhiteSpace(value.ReasonCode))
                || candidates.GroupBy(value => value.CandidateActionId, StringComparer.Ordinal).Any(group => group.Count() > 1)
                || candidates.Count(value => value.IsDefault) != 1)
            {
                throw new InvalidDataException("The policy-filtered candidate action set is incomplete or ambiguous for ingredient '" + ingredientId + "'.");
            }
        }

        private static bool ReceiptMatches(
            PageIngredientActionSelectionReceipt receipt,
            PageIngredientSelectedAction selected,
            PageIngredientActionCandidate candidate)
        {
            return string.Equals(receipt.SchemaVersion, PageIngredientActionSelectionReceipt.ContractVersion, StringComparison.Ordinal)
                && string.Equals(receipt.IngredientId, selected.IngredientId, StringComparison.Ordinal)
                && string.Equals(receipt.CandidateActionId, selected.CandidateActionId, StringComparison.Ordinal)
                && receipt.Action == candidate.Action
                && receipt.Scope == candidate.Scope
                && receipt.DependencyEffect == candidate.DependencyEffect
                && receipt.ComparisonRule == candidate.ComparisonRule
                && string.Equals(receipt.PolicyId, candidate.PolicyId, StringComparison.Ordinal)
                && string.Equals(receipt.PolicyVersion, candidate.PolicyVersion, StringComparison.Ordinal)
                && string.Equals(receipt.ReasonCode, candidate.ReasonCode, StringComparison.Ordinal)
                && string.Equals(receipt.SelectedBy, selected.SelectedBy, StringComparison.Ordinal)
                && receipt.SelectedAtUtc == selected.SelectedAtUtc
                && string.Equals(receipt.ApprovalReference, selected.ApprovalReference, StringComparison.Ordinal);
        }

        private static bool CanonicalEquals(object left, object right)
        {
            return string.Equals(
                MigrationContractSerializer.SerializeCanonical(left),
                MigrationContractSerializer.SerializeCanonical(right),
                StringComparison.Ordinal);
        }

        private static IngredientDisposition ToDisposition(IngredientSelectableAction action)
        {
            switch (action)
            {
                case IngredientSelectableAction.Reproduce: return IngredientDisposition.Preserve;
                case IngredientSelectableAction.Transform: return IngredientDisposition.Transform;
                case IngredientSelectableAction.Reference: return IngredientDisposition.Delegate;
                case IngredientSelectableAction.EvidenceOnly: return IngredientDisposition.EvidenceOnly;
                case IngredientSelectableAction.Exclude: return IngredientDisposition.Exclude;
                case IngredientSelectableAction.Defer: return IngredientDisposition.Defer;
                default: return IngredientDisposition.Undefined;
            }
        }

        private static IngredientTerminalStatus ToTerminalStatus(IngredientSelectableAction action)
        {
            return action == IngredientSelectableAction.EvidenceOnly || action == IngredientSelectableAction.Exclude
                ? IngredientTerminalStatus.SatisfiedByPolicy
                : action == IngredientSelectableAction.Defer
                    ? IngredientTerminalStatus.DecisionRequired
                    : IngredientTerminalStatus.Executable;
        }
    }
}
