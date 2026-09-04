using System;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    public enum IngredientComparisonOutcome
    {
        Exact = 1,
        ExpectedDifference = 2,
        UnexpectedDifference = 3,
        NotCompared = 4
    }

    public enum IngredientDifferenceKind
    {
        None = 0,
        ExpectedAbsent = 1,
        UnexpectedAbsent = 2,
        UnexpectedPresent = 3
    }

    public sealed class PageIngredientComparisonResult
    {
        public string IngredientId { get; set; }

        public string Path { get; set; }

        public bool SourcePresent { get; set; }

        public bool TargetPresent { get; set; }

        public IngredientComparisonOutcome Outcome { get; set; }

        public IngredientDifferenceKind Difference { get; set; }

        public string ReasonCode { get; set; }

        public string PolicyId { get; set; }

        public PageIngredientActionSelectionReceipt SelectionReceipt { get; set; }
    }

    internal static class PageIngredientComparisonPolicy
    {
        public static PageIngredientComparisonResult ComparePresence(
            PageIngredientAction action,
            bool sourcePresent,
            bool targetPresent,
            string path)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            var expectedAbsent = action.SelectionReceipt?.ComparisonRule == IngredientComparisonRule.ExpectedAbsent
                && action.SelectedAction?.Action == IngredientSelectableAction.Exclude;
            var evidenceOnly = action.SelectionReceipt?.ComparisonRule == IngredientComparisonRule.EvidenceOnly
                && action.SelectedAction?.Action == IngredientSelectableAction.EvidenceOnly;
            return new PageIngredientComparisonResult
            {
                IngredientId = action.IngredientId,
                Path = path,
                SourcePresent = sourcePresent,
                TargetPresent = targetPresent,
                Outcome = evidenceOnly
                    ? IngredientComparisonOutcome.NotCompared
                    : expectedAbsent
                        ? !targetPresent
                            ? IngredientComparisonOutcome.ExpectedDifference
                            : IngredientComparisonOutcome.UnexpectedDifference
                        : sourcePresent == targetPresent
                            ? IngredientComparisonOutcome.Exact
                            : IngredientComparisonOutcome.UnexpectedDifference,
                Difference = evidenceOnly
                    ? IngredientDifferenceKind.None
                    : expectedAbsent
                        ? targetPresent ? IngredientDifferenceKind.UnexpectedPresent : IngredientDifferenceKind.ExpectedAbsent
                        : sourcePresent && !targetPresent
                            ? IngredientDifferenceKind.UnexpectedAbsent
                            : !sourcePresent && targetPresent
                                ? IngredientDifferenceKind.UnexpectedPresent
                                : IngredientDifferenceKind.None,
                ReasonCode = action.SelectionReceipt?.ReasonCode,
                PolicyId = action.SelectionReceipt?.PolicyId,
                SelectionReceipt = action.SelectionReceipt
            };
        }


        public static PageIngredientComparisonResult ComparePresence(
            PageIngredientActionSelectionReceipt receipt,
            bool sourcePresent,
            bool targetPresent,
            string path)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }
            return ComparePresence(new PageIngredientAction
            {
                IngredientId = receipt.IngredientId,
                SelectedAction = new PageIngredientSelectedAction
                {
                    IngredientId = receipt.IngredientId,
                    CandidateActionId = receipt.CandidateActionId,
                    Action = receipt.Action,
                    Scope = receipt.Scope,
                    SnapshotDigest = receipt.SnapshotDigest
                },
                SelectionReceipt = receipt
            }, sourcePresent, targetPresent, path);
        }
    }
}
