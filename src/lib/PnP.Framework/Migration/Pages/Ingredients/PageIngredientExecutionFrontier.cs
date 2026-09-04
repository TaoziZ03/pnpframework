using System.Collections.Generic;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Ingredients
{
    public sealed class PageIngredientExecutionFrontier
    {
        public IList<string> ExecutableIngredientIds { get; set; } = new List<string>();

        public IList<string> AuthorizationBlockedIngredientIds { get; set; } = new List<string>();

        public IList<string> DecisionRequiredIngredientIds { get; set; } = new List<string>();

        public bool ShouldStopWholeItem => ExecutableIngredientIds.Count == 0
            && (AuthorizationBlockedIngredientIds.Count > 0 || DecisionRequiredIngredientIds.Count > 0);

        public static PageIngredientExecutionFrontier Create(IEnumerable<PageIngredientAction> actions)
        {
            var values = (actions ?? Enumerable.Empty<PageIngredientAction>()).Where(value => value != null).ToList();
            return new PageIngredientExecutionFrontier
            {
                ExecutableIngredientIds = values.Where(value => value.TerminalStatus == IngredientTerminalStatus.Executable
                        && value.Disposition != IngredientDisposition.Block
                        && value.Disposition != IngredientDisposition.Defer)
                    .Select(value => value.IngredientId).ToList(),
                AuthorizationBlockedIngredientIds = values.Where(value => value.TerminalStatus == IngredientTerminalStatus.AuthorizationBlocked)
                    .Select(value => value.IngredientId).ToList(),
                DecisionRequiredIngredientIds = values.Where(value => value.TerminalStatus == IngredientTerminalStatus.DecisionRequired)
                    .Select(value => value.IngredientId).ToList()
            };
        }
    }
}
