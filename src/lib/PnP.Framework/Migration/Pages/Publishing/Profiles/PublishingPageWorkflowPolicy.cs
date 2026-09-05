using PnP.Framework.Migration.Pages.Cohorts;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public sealed class PublishingPageWorkflowPolicy
    {
        public string WorkflowId { get; set; }

        public string PreferredTargetPageLayoutFileName { get; set; }

        public ISet<string> StockPageLayoutFileNames { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ISet<string> FieldsHandledByPageWriter { get; set; }

        public ISet<string> RecognizedPageFields { get; set; }

        public Func<string, ValidationCohortAssessment> AssessValidationCohort { get; set; }

        public bool IsStockLayout(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (string.Equals(fileName, PreferredTargetPageLayoutFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (StockPageLayoutFileNames != null && StockPageLayoutFileNames.Contains(fileName))
            {
                return true;
            }

            return false;
        }

        public PublishingPageWorkflowSelection Select(string sourceContentTypeId)
        {
            if (string.IsNullOrWhiteSpace(WorkflowId) || AssessValidationCohort == null)
            {
                throw new InvalidOperationException("The Publishing Page workflow policy is incomplete.");
            }

            return new PublishingPageWorkflowSelection
            {
                WorkflowId = WorkflowId,
                ValidationCohort = AssessValidationCohort(sourceContentTypeId)
            };
        }
    }
}
