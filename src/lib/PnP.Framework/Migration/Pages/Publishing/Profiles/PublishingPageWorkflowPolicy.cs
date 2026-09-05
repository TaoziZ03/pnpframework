using PnP.Framework.Migration.Pages.Cohorts;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Publishing.Profiles
{
    public sealed class PublishingPageWorkflowPolicy
    {
        private readonly HashSet<string> stockPageLayoutFileNames;
        private readonly HashSet<string> fieldsHandledByPageWriter;
        private readonly HashSet<string> recognizedPageFields;

        public PublishingPageWorkflowPolicy(
            string workflowId,
            string sourceContentTypeName,
            string sourceContentTypeIdPrefix,
            string preferredTargetPageLayoutFileName,
            IEnumerable<string> stockPageLayoutFileNames,
            IEnumerable<string> fieldsHandledByPageWriter,
            IEnumerable<string> recognizedPageFields,
            Func<string, ValidationCohortAssessment> assessValidationCohort)
        {
            if (string.IsNullOrWhiteSpace(workflowId))
            {
                throw new ArgumentException("A workflow ID is required.", nameof(workflowId));
            }
            if (string.IsNullOrWhiteSpace(sourceContentTypeName))
            {
                throw new ArgumentException("A source content type name is required.", nameof(sourceContentTypeName));
            }
            if (string.IsNullOrWhiteSpace(sourceContentTypeIdPrefix))
            {
                throw new ArgumentException("A source content type ID prefix is required.", nameof(sourceContentTypeIdPrefix));
            }
            if (string.IsNullOrWhiteSpace(preferredTargetPageLayoutFileName))
            {
                throw new ArgumentException("A preferred target Page Layout is required.", nameof(preferredTargetPageLayoutFileName));
            }

            WorkflowId = workflowId;
            SourceContentTypeName = sourceContentTypeName;
            SourceContentTypeIdPrefix = sourceContentTypeIdPrefix;
            PreferredTargetPageLayoutFileName = preferredTargetPageLayoutFileName;
            this.stockPageLayoutFileNames = Copy(stockPageLayoutFileNames);
            this.fieldsHandledByPageWriter = Copy(fieldsHandledByPageWriter);
            this.recognizedPageFields = Copy(recognizedPageFields);
            StockPageLayoutFileNames = ReadOnly(this.stockPageLayoutFileNames);
            FieldsHandledByPageWriter = ReadOnly(this.fieldsHandledByPageWriter);
            RecognizedPageFields = ReadOnly(this.recognizedPageFields);
            AssessValidationCohort = assessValidationCohort
                ?? throw new ArgumentNullException(nameof(assessValidationCohort));
        }

        public string WorkflowId { get; }

        public string SourceContentTypeName { get; }

        public string SourceContentTypeIdPrefix { get; }

        public string PreferredTargetPageLayoutFileName { get; }

        public IReadOnlyCollection<string> StockPageLayoutFileNames { get; }

        public IReadOnlyCollection<string> FieldsHandledByPageWriter { get; }

        public IReadOnlyCollection<string> RecognizedPageFields { get; }

        public Func<string, ValidationCohortAssessment> AssessValidationCohort { get; }

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

            return stockPageLayoutFileNames.Contains(fileName);
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

        internal PublishingPageWorkflowPolicy Snapshot()
        {
            return new PublishingPageWorkflowPolicy(
                WorkflowId,
                SourceContentTypeName,
                SourceContentTypeIdPrefix,
                PreferredTargetPageLayoutFileName,
                stockPageLayoutFileNames,
                fieldsHandledByPageWriter,
                recognizedPageFields,
                AssessValidationCohort);
        }

        private static HashSet<string> Copy(IEnumerable<string> values)
        {
            return new HashSet<string>(
                (values ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
        }

        private static IReadOnlyCollection<string> ReadOnly(IEnumerable<string> values)
        {
            return new ReadOnlyCollection<string>(
                values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }
}
