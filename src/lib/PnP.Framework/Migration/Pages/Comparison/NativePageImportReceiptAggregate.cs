using PnP.Framework.Migration.Execution;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Pages.ClassicWiki.Execution;
using PnP.Framework.Migration.Pages.ClassicWiki.Packaging;
using PnP.Framework.Migration.Pages.Publishing.Execution;
using PnP.Framework.Migration.Pages.Publishing.Packaging;
using PnP.Framework.Migration.Verification;
using System;
using System.IO;
using System.Linq;

namespace PnP.Framework.Migration.Pages.Comparison
{
    public static class NativePageImportReceiptContract
    {
        public const string AggregateSchemaVersion = "pnp-native-page-import-receipt-aggregate/v1";
        public const string ClassicWikiFamily = "classic-wiki";
        public const string PublishingFamily = "publishing";
    }

    /// <summary>
    /// Lossless one-of envelope for family-specific native import receipts.
    /// It preserves the original typed payload; it does not translate Wiki into Publishing.
    /// </summary>
    public sealed class NativePageImportReceiptAggregate
    {
        public string SchemaVersion { get; set; } = NativePageImportReceiptContract.AggregateSchemaVersion;

        public string PageFamily { get; set; }

        public string ReceiptSchemaVersion { get; set; }

        public string ReceiptDigestSha256 { get; set; }

        public ClassicWikiImportReceipt ClassicWikiReceipt { get; set; }

        public PublishingPageImportReceipt PublishingReceipt { get; set; }
    }

    /// <summary>
    /// Family-neutral binding consumed by page compare orchestration after the
    /// aggregate and its typed native receipt have passed validation.
    /// </summary>
    public sealed class NativePageImportCompareBinding
    {
        public string AggregateSchemaVersion { get; set; }

        public string PageFamily { get; set; }

        public string ReceiptSchemaVersion { get; set; }

        public string ReceiptDigestSha256 { get; set; }

        public string AdmittedPlanDigestSha256 { get; set; }

        public string SourceVersionDigestSha256 { get; set; }

        public ReproOperationIds Operations { get; set; }

        public Guid MutationOperationId { get; set; }

        public string ApprovedPlanDigest { get; set; }

        public string TargetWebUrl { get; set; }

        public string TargetPageServerRelativeUrl { get; set; }

        public Guid TargetFileUniqueId { get; set; }

        public int TargetListItemId { get; set; }

        public string TargetVersionLabel { get; set; }

        public MigrationExecutionStatus ExecutionStatus { get; set; }

        public bool PartialExecution { get; set; }

        public bool MutationStarted { get; set; }

        public int NativeStepCount { get; set; }

        public bool FreshReadbackPassed { get; set; }

        public StorageVerificationStatus StorageVerificationStatus { get; set; }

        public RuntimeVerificationStatus RuntimeVerificationStatus { get; set; }
    }

    public static class NativePageImportReceiptAggregateFactory
    {
        public static NativePageImportReceiptAggregate Create(ClassicWikiImportReceipt receipt)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }
            return new NativePageImportReceiptAggregate
            {
                PageFamily = NativePageImportReceiptContract.ClassicWikiFamily,
                ReceiptSchemaVersion = receipt.SchemaVersion,
                ReceiptDigestSha256 = ComputeDigest(receipt),
                ClassicWikiReceipt = receipt
            };
        }

        public static NativePageImportReceiptAggregate Create(PublishingPageImportReceipt receipt)
        {
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }
            return new NativePageImportReceiptAggregate
            {
                PageFamily = NativePageImportReceiptContract.PublishingFamily,
                ReceiptSchemaVersion = receipt.SchemaVersion,
                ReceiptDigestSha256 = ComputeDigest(receipt),
                PublishingReceipt = receipt
            };
        }

        private static string ComputeDigest<T>(T receipt)
        {
            return MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(receipt));
        }
    }

    public static class NativePageImportReceiptAggregateValidator
    {
        public static NativePageImportCompareBinding ValidateForCompare(
            NativePageImportReceiptAggregate aggregate,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256)
        {
            var receipt = ValidateCarrier(aggregate, admittedPlan);
            if (aggregate.ClassicWikiReceipt != null)
            {
                ClassicWikiImportReceiptValidator.ValidateAdmittedExecution(
                    aggregate.ClassicWikiReceipt,
                    admittedPlan,
                    admittedPlanDigestSha256);
            }
            else
            {
                PublishingPageImportReceiptValidator.ValidateAdmittedExecution(
                    aggregate.PublishingReceipt,
                    admittedPlan,
                    admittedPlanDigestSha256);
            }
            return CreateBinding(aggregate, receipt, admittedPlan, admittedPlanDigestSha256);
        }

        /// <summary>
        /// Validates a typed native receipt as immutable terminal evidence without
        /// granting successful Compare admission.
        /// </summary>
        internal static NativePageImportCompareBinding ValidateForTerminalEvidence(
            NativePageImportReceiptAggregate aggregate,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256)
        {
            var receipt = ValidateCarrier(aggregate, admittedPlan);
            AdmittedPageImportReceiptValidator.ValidateExecutionEvidence(
                receipt,
                admittedPlan,
                admittedPlanDigestSha256);
            return CreateBinding(aggregate, receipt, admittedPlan, admittedPlanDigestSha256);
        }

        private static IAdmittedPageImportReceipt ValidateCarrier(
            NativePageImportReceiptAggregate aggregate,
            AdmittedReproExecutionPlan admittedPlan)
        {
            Require(aggregate != null, "A native page import receipt aggregate is required.");
            Require(admittedPlan != null, "A target-admitted repro plan is required.");
            Require(string.Equals(
                    aggregate.SchemaVersion,
                    NativePageImportReceiptContract.AggregateSchemaVersion,
                    StringComparison.Ordinal),
                "The native page import receipt aggregate schema is unsupported.");

            var hasClassicWiki = aggregate.ClassicWikiReceipt != null;
            var hasPublishing = aggregate.PublishingReceipt != null;
            Require(hasClassicWiki != hasPublishing,
                "The native page import receipt aggregate must contain exactly one typed receipt.");

            IAdmittedPageImportReceipt receipt;
            if (hasClassicWiki)
            {
                Require(string.Equals(aggregate.PageFamily, NativePageImportReceiptContract.ClassicWikiFamily, StringComparison.Ordinal),
                    "The native page import receipt family is foreign to its typed Wiki receipt.");
                Require(string.Equals(aggregate.ReceiptSchemaVersion, ClassicWikiPackageContract.ReceiptSchemaVersion, StringComparison.Ordinal),
                    "The classic wiki receipt schema binding is unsupported.");
                receipt = aggregate.ClassicWikiReceipt;
            }
            else
            {
                Require(string.Equals(aggregate.PageFamily, NativePageImportReceiptContract.PublishingFamily, StringComparison.Ordinal),
                    "The native page import receipt family is foreign to its typed Publishing receipt.");
                Require(string.Equals(aggregate.ReceiptSchemaVersion, PublishingPagePackageContract.ReceiptSchemaVersion, StringComparison.Ordinal),
                    "The publishing receipt schema binding is unsupported.");
                receipt = aggregate.PublishingReceipt;
            }

            Require(string.Equals(aggregate.ReceiptSchemaVersion, receipt.SchemaVersion, StringComparison.Ordinal),
                "The native receipt schema binding does not match its typed receipt.");
            ValidateDigest(aggregate.ReceiptDigestSha256, "native import receipt digest");
            var computedDigest = hasClassicWiki
                ? MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                    aggregate.ClassicWikiReceipt))
                : MigrationDigest.ComputeSha256(MigrationContractSerializer.SerializeCanonical(
                    aggregate.PublishingReceipt));
            Require(string.Equals(computedDigest, aggregate.ReceiptDigestSha256, StringComparison.OrdinalIgnoreCase),
                "The native page import receipt aggregate is corrupt or stale.");
            Require(string.Equals(
                    CanonicalTargetIdentity(receipt.TargetWebUrl, receipt.TargetPageServerRelativeUrl),
                    admittedPlan.TargetIdentity,
                    StringComparison.Ordinal),
                "The native import receipt target identity is foreign to the admitted plan.");
            return receipt;
        }

        private static NativePageImportCompareBinding CreateBinding(
            NativePageImportReceiptAggregate aggregate,
            IAdmittedPageImportReceipt receipt,
            AdmittedReproExecutionPlan admittedPlan,
            string admittedPlanDigestSha256)
        {
            return new NativePageImportCompareBinding
            {
                AggregateSchemaVersion = aggregate.SchemaVersion,
                PageFamily = aggregate.PageFamily,
                ReceiptSchemaVersion = receipt.SchemaVersion,
                ReceiptDigestSha256 = aggregate.ReceiptDigestSha256,
                AdmittedPlanDigestSha256 = admittedPlanDigestSha256,
                SourceVersionDigestSha256 = admittedPlan.SourceVersion.VersionDigestSha256,
                Operations = Copy(receipt.Operations),
                MutationOperationId = receipt.OperationId,
                ApprovedPlanDigest = receipt.ApprovedPlanDigest,
                TargetWebUrl = receipt.TargetWebUrl,
                TargetPageServerRelativeUrl = receipt.TargetPageServerRelativeUrl,
                TargetFileUniqueId = receipt.TargetFileUniqueId,
                TargetListItemId = receipt.TargetListItemId,
                TargetVersionLabel = receipt.TargetVersionLabel,
                ExecutionStatus = receipt.ExecutionStatus,
                PartialExecution = receipt.PartialExecution,
                MutationStarted = receipt.MutationStarted,
                NativeStepCount = receipt.Steps?.Count ?? 0,
                FreshReadbackPassed = receipt.FreshReadbackPassed,
                StorageVerificationStatus = receipt.StorageVerificationStatus,
                RuntimeVerificationStatus = receipt.RuntimeVerificationStatus
            };
        }

        private static ReproOperationIds Copy(ReproOperationIds operations)
        {
            return operations == null ? null : new ReproOperationIds
            {
                MutationOperationId = operations.MutationOperationId,
                ReadbackOperationId = operations.ReadbackOperationId,
                RuntimeOperationId = operations.RuntimeOperationId,
                CleanupOperationId = operations.CleanupOperationId
            };
        }

        private static string CanonicalTargetIdentity(string webUrl, string pageServerRelativeUrl)
        {
            Require(Uri.TryCreate(webUrl, UriKind.Absolute, out var web)
                && !string.IsNullOrWhiteSpace(pageServerRelativeUrl),
                "The native import receipt target identity is incomplete.");
            return web.GetLeftPart(UriPartial.Authority).TrimEnd('/')
                + "/"
                + pageServerRelativeUrl.TrimStart('/');
        }

        private static void ValidateDigest(string value, string name)
        {
            Require(value != null && value.Length == 64 && value.All(IsHex),
                "The " + name + " must be a SHA-256 digest.");
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidDataException(message);
            }
        }
    }
}
