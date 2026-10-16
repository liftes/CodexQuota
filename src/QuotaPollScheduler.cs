using System;

namespace CodexQuota
{
    public sealed class QuotaPollScheduler
    {
        public int BaseSeconds { get; private set; }
        public bool Adaptive { get; private set; }
        public DateTimeOffset NextDueAt { get; private set; }
        public DateTimeOffset? LastSuccessAt { get; private set; }
        public int CurrentDelaySeconds { get; private set; }
        private int unchangedCount;
        private int errorCount;
        private QuotaSnapshot lastSnapshot;
        public QuotaPollScheduler(int baseSeconds, bool adaptive, DateTimeOffset now)
        {
            Configure(baseSeconds, adaptive, now); NextDueAt = now;
        }
        public void Configure(int baseSeconds, bool adaptive, DateTimeOffset now)
        {
            BaseSeconds = Math.Max(30,Math.Min(300,baseSeconds)); Adaptive = adaptive;
            unchangedCount = 0; errorCount = 0; CurrentDelaySeconds = BaseSeconds;
            NextDueAt = LastSuccessAt.HasValue ? LastSuccessAt.Value.AddSeconds(BaseSeconds) : now;
        }
        public bool IsDue(DateTimeOffset now) { return now >= NextDueAt; }
        public void RecordSuccess(QuotaSnapshot snapshot, DateTimeOffset now)
        {
            bool changed_ = lastSnapshot == null || !SameWindow(lastSnapshot.FiveHour,snapshot.FiveHour) || !SameWindow(lastSnapshot.Weekly,snapshot.Weekly);
            unchangedCount = changed_ ? 0 : unchangedCount + 1;
            errorCount = 0; lastSnapshot = snapshot; LastSuccessAt = now;
            int factor_ = !Adaptive ? 1 : unchangedCount >= 4 ? 5 : unchangedCount >= 2 ? 2 : 1;
            CurrentDelaySeconds = Math.Min(300,BaseSeconds * factor_);
            NextDueAt = now.AddSeconds(CurrentDelaySeconds);
            ScheduleReset(snapshot.FiveHour,now); ScheduleReset(snapshot.Weekly,now);
        }
        public void RecordFailure(DateTimeOffset now)
        {
            errorCount = Math.Min(errorCount + 1,4);
            CurrentDelaySeconds = Math.Min(300,BaseSeconds * (1 << (errorCount - 1)));
            NextDueAt = now.AddSeconds(CurrentDelaySeconds);
        }
        public void MarkActivity(DateTimeOffset now)
        {
            if (!Adaptive || errorCount > 0 || !LastSuccessAt.HasValue) return;
            unchangedCount = 0; CurrentDelaySeconds = BaseSeconds;
            DateTimeOffset due_ = LastSuccessAt.Value.AddSeconds(BaseSeconds);
            if (due_ < NextDueAt) NextDueAt = due_ < now ? now : due_;
        }
        private void ScheduleReset(QuotaWindow quota, DateTimeOffset now)
        {
            if (quota == null || !quota.ResetsAt.HasValue || quota.ResetsAt.Value <= now) return;
            if (quota.ResetsAt.Value >= DateTimeOffset.MaxValue.AddSeconds(-1)) return;
            DateTimeOffset due_ = quota.ResetsAt.Value.AddSeconds(1);
            if (due_ < now.AddSeconds(5)) due_ = now.AddSeconds(5);
            if (due_ < NextDueAt) NextDueAt = due_;
        }
        private static bool SameWindow(QuotaWindow first, QuotaWindow second)
        {
            if (first == null || second == null) return first == null && second == null;
            return first.UsedPercent == second.UsedPercent && first.WindowDurationMins == second.WindowDurationMins && first.ResetsAt == second.ResetsAt;
        }
    }
}
