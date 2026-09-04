using PnP.Framework.Migration.Pages.Cohorts;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    internal sealed class PublishingPageWorkflowPolicy
    {
        public string WorkflowId { get; set; }

        public string PreferredTargetPageLayoutFileName { get; set; }

        public ISet<string> FieldsHandledByPageWriter { get; set; }

        public ISet<string> RecognizedPageFields { get; set; }

        public Func<string, ValidationCohortAssessment> AssessValidationCohort { get; set; }

        /// <summary>
        /// Requires source membership in this workflow's reviewed validation
        /// cohort before planning approval or import. The shared Publishing
        /// runtime may still be reusable by a different workflow policy.
        /// </summary>
        public bool RequireIncludedValidationCohort { get; set; }

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
