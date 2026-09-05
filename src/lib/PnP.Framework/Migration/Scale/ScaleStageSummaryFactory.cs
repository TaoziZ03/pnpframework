using System.Linq;

namespace PnP.Framework.Migration.Scale
{
    internal static class ScaleStageSummaryFactory
    {
        public static ScaleStageRunSummary Create(
            ScaleRunStage stage,
            int attempt,
            double elapsedMilliseconds,
            ScaleStageExecutionResult result,
            bool resumeSkipped)
        {
            return new ScaleStageRunSummary
            {
                Stage = stage,
                AttemptCount = attempt,
                Outcome = result.Outcome,
                ResumeSkipped = resumeSkipped,
                Verified = result.Verified,
                DurationMilliseconds = elapsedMilliseconds,
                RequestCount = result.Requests.Count,
                RequestDurationsMilliseconds = result.Requests.Select(value => value.DurationMilliseconds).ToList(),
                Http429Count = result.Requests.Count(value => value.HttpStatusCode == 429),
                Http503Count = result.Requests.Count(value => value.HttpStatusCode == 503),
                RetryAfterWaitMilliseconds = result.Requests.Sum(value => value.RetryAfterWaitMilliseconds),
                UnexpectedDifferenceCount = result.UnexpectedDifferenceCount,
                SharedReceiptReuseCount = result.SharedReceiptReuseCount,
                ArtifactSetDigest = ScaleRunIdentity.ComputeArtifactSetDigest(result.Artifacts),
                DiagnosticCode = result.DiagnosticCode,
                Ingredients = result.Ingredients.ToList()
            };
        }

        public static ScaleStageRunSummary Terminal(
            ScaleRunStage stage,
            ScaleStageOutcome outcome,
            string diagnostic)
        {
            return new ScaleStageRunSummary
            {
                Stage = stage,
                Outcome = outcome,
                DiagnosticCode = diagnostic
            };
        }
    }
}
