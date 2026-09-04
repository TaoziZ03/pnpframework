using System;

namespace PnP.Framework.Migration.Lists.Items.Protection
{
    internal static class ProtectedAssetIngredientIds
    {
        public static string Asset(Guid sourceWebId, Guid sourceListId, int sourceItemId) =>
            "protected-asset:" + Identity(sourceWebId, sourceListId, sourceItemId);

        public static string DocumentIdentity(Guid sourceWebId, Guid sourceListId, int sourceItemId) =>
            "document-identity:" + Identity(sourceWebId, sourceListId, sourceItemId);

        public static string BinaryPayload(Guid sourceWebId, Guid sourceListId, int sourceItemId) =>
            "binary-payload:" + Identity(sourceWebId, sourceListId, sourceItemId);

        public static string InformationProtectionRelationship(Guid sourceWebId, Guid sourceListId, int sourceItemId) =>
            "information-protection-relationship:" + Identity(sourceWebId, sourceListId, sourceItemId);

        private static string Identity(Guid sourceWebId, Guid sourceListId, int sourceItemId) =>
            sourceWebId.ToString("D") + "/" + sourceListId.ToString("D") + "/" + sourceItemId;
    }
}
