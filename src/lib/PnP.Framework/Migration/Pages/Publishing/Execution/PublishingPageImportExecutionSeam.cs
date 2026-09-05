using Microsoft.SharePoint.Client;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Execution
{
    internal sealed class PublishingPageImportExecutionSeam
    {
        public string TargetWebUrl { get; set; }

        public Func<PublishingPageTargetStorageState> ReadTargetPage { get; set; }
    }

    internal sealed class PublishingPageTargetStorageState
    {
        public bool Exists { get; set; }

        public Guid FileUniqueId { get; set; }

        public int ListItemId { get; set; }

        public string VersionLabel { get; set; }

        public FileLevel Level { get; set; }

        public CheckOutType CheckOutType { get; set; }

        public bool HasUniqueRoleAssignments { get; set; }

        public IDictionary<string, object> Properties { get; set; } =
            new Dictionary<string, object>(StringComparer.Ordinal);

        public IDictionary<string, object> Fields { get; set; } =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }
}
