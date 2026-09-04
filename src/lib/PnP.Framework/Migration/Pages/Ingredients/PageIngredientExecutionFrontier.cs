using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    public enum PageIngredientExecutionState
    {
        Executable = 1,
        ExcludedByApprovedDisposition = 2,
        Deferred = 3,
        AuthorizationBlocked = 4,
        SkippedByDeferredDependency = 5,
        SkippedByAuthorizationDependency = 6,
        SatisfiedByPolicy = 7
    }

    public sealed class PageIngredientExecutionDecision
    {
        public string IngredientId { get; set; }

        public PageIngredientExecutionState State { get; set; }

        public IList<string> CauseIngredientIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// Sealed, dependency-aware execution boundary for one page package. A direct
    /// Defer or authorization Block removes only that ingredient and the retained
    /// consumers that require it. Independent ingredients remain executable.
    /// Policy-approved exclusions are terminal decisions, not authorization facts.
    /// </summary>
    public sealed class PageIngredientExecutionFrontier
    {
        public string SchemaVersion { get; set; } = "pnp-page-ingredient-execution-frontier/v1";

        public IList<PageIngredientExecutionDecision> Decisions { get; set; } = new List<PageIngredientExecutionDecision>();

        [JsonIgnore]
        public bool HasExecutableIngredients => Decisions.Any(value =>
            value != null && value.State == PageIngredientExecutionState.Executable);

        [JsonIgnore]
        public bool HasDeferredIngredients => Decisions.Any(value => value != null
            && (value.State == PageIngredientExecutionState.Deferred
                || value.State == PageIngredientExecutionState.SkippedByDeferredDependency));

        [JsonIgnore]
        public bool HasAuthorizationBlockedIngredients => Decisions.Any(value => value != null
            && (value.State == PageIngredientExecutionState.AuthorizationBlocked
                || value.State == PageIngredientExecutionState.SkippedByAuthorizationDependency));

        [JsonIgnore]
        public bool HasPolicySatisfiedIngredients => Decisions.Any(value => value != null
            && value.State == PageIngredientExecutionState.SatisfiedByPolicy);

        [JsonIgnore]
        public bool IsPartial => HasExecutableIngredients
            && (HasDeferredIngredients || HasAuthorizationBlockedIngredients);

        [JsonIgnore]
        public bool ShouldStopWholeItem => !HasExecutableIngredients
            && (HasDeferredIngredients || HasAuthorizationBlockedIngredients);

        [JsonIgnore]
        public IList<string> ExecutableIngredientIds => IngredientIds(PageIngredientExecutionState.Executable);

        [JsonIgnore]
        public IList<string> AuthorizationBlockedIngredientIds => Decisions
            .Where(value => value != null
                && (value.State == PageIngredientExecutionState.AuthorizationBlocked
                    || value.State == PageIngredientExecutionState.SkippedByAuthorizationDependency))
            .Select(value => value.IngredientId)
            .ToList();

        [JsonIgnore]
        public IList<string> DecisionRequiredIngredientIds => Decisions
            .Where(value => value != null
                && (value.State == PageIngredientExecutionState.Deferred
                    || value.State == PageIngredientExecutionState.SkippedByDeferredDependency))
            .Select(value => value.IngredientId)
            .ToList();

        public bool IsExecutable(string ingredientId)
        {
            return Decisions.Any(value => value != null
                && string.Equals(value.IngredientId, ingredientId, StringComparison.Ordinal)
                && value.State == PageIngredientExecutionState.Executable);
        }

        public PageIngredientExecutionState? GetState(string ingredientId)
        {
            var decision = Decisions.FirstOrDefault(value => value != null
                && string.Equals(value.IngredientId, ingredientId, StringComparison.Ordinal));
            return decision?.State;
        }

        private IList<string> IngredientIds(PageIngredientExecutionState state)
        {
            return Decisions
                .Where(value => value != null && value.State == state)
                .Select(value => value.IngredientId)
                .ToList();
        }
    }
}
