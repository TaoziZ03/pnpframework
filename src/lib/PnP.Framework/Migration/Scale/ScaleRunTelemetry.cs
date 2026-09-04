using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PnP.Framework.Migration.Scale
{
    internal sealed class ScaleRunTelemetry
    {
        private readonly ConcurrentDictionary<ScaleRunStage, int> active =
            new ConcurrentDictionary<ScaleRunStage, int>();
        private readonly ConcurrentDictionary<ScaleRunStage, int> maximum =
            new ConcurrentDictionary<ScaleRunStage, int>();
        private int unverified;
        private int maxUnverified;

        public int MaxUnverified => Volatile.Read(ref maxUnverified);

        public void EnterStage(ScaleRunStage stage)
        {
            var value = active.AddOrUpdate(stage, 1, (_, current) => current + 1);
            maximum.AddOrUpdate(stage, value, (_, current) => Math.Max(current, value));
        }

        public void LeaveStage(ScaleRunStage stage)
        {
            active.AddOrUpdate(stage, 0, (_, current) => Math.Max(0, current - 1));
        }

        public int MaxStageConcurrency(ScaleRunStage stage)
        {
            return maximum.TryGetValue(stage, out var value) ? value : 0;
        }

        public void EnterUnverified()
        {
            var value = Interlocked.Increment(ref unverified);
            UpdateMaximum(ref maxUnverified, value);
        }

        public void LeaveUnverified()
        {
            Interlocked.Decrement(ref unverified);
        }

        private static void UpdateMaximum(ref int target, int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (value <= current
                    || Interlocked.CompareExchange(ref target, value, current) == current)
                {
                    return;
                }
            }
        }
    }
}
