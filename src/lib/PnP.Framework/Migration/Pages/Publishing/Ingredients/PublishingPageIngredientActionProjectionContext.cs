using PnP.Framework.Migration.Pages.Ingredients;
using PnP.Framework.Migration.Pages.Publishing.Capture;
using PnP.Framework.Migration.Pages.Publishing.Planning;
using System;
using System.Collections.Generic;
using System.IO;

namespace PnP.Framework.Migration.Pages.Publishing.Ingredients
{
    public sealed class PublishingPageIngredientActionProjectionContext
    {
        private readonly IDictionary<string, PageIngredientAction> actions;
        private readonly PageIngredientHandlerDescriptor descriptor;

        internal PublishingPageIngredientActionProjectionContext(
            PublishingPageCaptureBundle snapshot,
            PublishingPageMigrationPlan plan,
            CanonicalPageIngredientGraph graph,
            IDictionary<string, PageIngredientAction> actions,
            PageIngredientHandlerDescriptor descriptor)
        {
            Snapshot = PublishingPageIngredientReadOnlyView.Clone(snapshot);
            Plan = PublishingPageIngredientReadOnlyView.Clone(plan);
            IngredientGraph = PublishingPageIngredientReadOnlyView.Clone(graph);
            PageFamily = PublishingPageIngredientPageFamily.Resolve(snapshot);
            HandlerId = descriptor?.HandlerId;
            LaneId = descriptor?.Lane?.LaneId;
            this.actions = actions;
            this.descriptor = descriptor;
        }

        public PublishingPageCaptureBundle Snapshot { get; }

        public PublishingPageMigrationPlan Plan { get; }

        public CanonicalPageIngredientGraph IngredientGraph { get; }

        public string PageFamily { get; }

        public string HandlerId { get; }

        public string LaneId { get; }

        public void AddAction(PageIngredientAction action)
        {
            if (action == null
                || string.IsNullOrWhiteSpace(action.ActionId)
                || string.IsNullOrWhiteSpace(action.IngredientId))
            {
                throw new InvalidDataException($"Handler '{descriptor.HandlerId}' produced a null or unidentified action.");
            }
            if (!descriptor.Owns(action.IngredientId))
            {
                throw new InvalidDataException(
                    $"Handler '{descriptor.HandlerId}' does not own action ingredient '{action.IngredientId}'.");
            }
            if (actions.ContainsKey(action.IngredientId))
            {
                throw new InvalidDataException(
                    $"Ingredient handler projection produced duplicate action for '{action.IngredientId}'.");
            }
            foreach (var existing in actions.Values)
            {
                if (string.Equals(existing?.ActionId, action.ActionId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Ingredient handler projection produced duplicate action ID '{action.ActionId}'.");
                }
            }
            actions.Add(action.IngredientId, action);
        }
    }
}
