using Microsoft.SharePoint.Client;
using PnP.Framework.Migration.Pages.Publishing.Lifecycle;

namespace PnP.Framework.Migration.Pages.Publishing.Verification
{
    internal sealed class PublishingPageLifecycleVerificationResult
    {
        public bool Matched { get; set; }

        public bool LevelMatched { get; set; }

        public bool CheckOutMatched { get; set; }

        public bool ModerationMatched { get; set; }

        public int? ExpectedModerationStatus { get; set; }

        public string Message { get; set; }
    }

    internal static class PublishingPageLifecycleVerifier
    {
        private const int ApprovedModerationStatus = 0;
        private const int DraftModerationStatus = 3;

        public static PublishingPageLifecycleVerificationResult Verify(
            PublishingPageTargetLifecycle expectedLifecycle,
            bool? targetModerationEnabled,
            FileLevel actualLevel,
            CheckOutType actualCheckOutType,
            int? actualModerationStatus)
        {
            var result = new PublishingPageLifecycleVerificationResult
            {
                LevelMatched = expectedLifecycle == PublishingPageTargetLifecycle.Published
                    ? actualLevel == FileLevel.Published
                    : actualLevel == FileLevel.Draft,
                CheckOutMatched = actualCheckOutType == CheckOutType.None
            };
            if (!targetModerationEnabled.HasValue)
            {
                result.ModerationMatched = false;
                result.Message = "The target Pages-library moderation contract is unavailable.";
            }
            else if (targetModerationEnabled.Value)
            {
                result.ExpectedModerationStatus = expectedLifecycle == PublishingPageTargetLifecycle.Published
                    ? ApprovedModerationStatus
                    : DraftModerationStatus;
                result.ModerationMatched = actualModerationStatus.HasValue
                    && actualModerationStatus.Value == result.ExpectedModerationStatus.Value;
                result.Message = result.ModerationMatched
                    ? "The moderation-enabled target exposes the expected lifecycle moderation status."
                    : $"The moderation-enabled target requires status {result.ExpectedModerationStatus.Value}, but observed {(actualModerationStatus.HasValue ? actualModerationStatus.Value.ToString() : "unknown")}.";
            }
            else
            {
                // Moderation is explicitly disabled, so the field is not an
                // independent lifecycle state. Farms may return no value or the
                // neutral Approved value; any active moderation state conflicts.
                result.ModerationMatched = !actualModerationStatus.HasValue
                    || actualModerationStatus.Value == ApprovedModerationStatus;
                result.Message = result.ModerationMatched
                    ? "The target Pages library explicitly disables moderation."
                    : $"The moderation-disabled target exposes active moderation status {actualModerationStatus.Value}.";
            }

            result.Matched = result.LevelMatched
                && result.CheckOutMatched
                && result.ModerationMatched;
            return result;
        }
    }
}
