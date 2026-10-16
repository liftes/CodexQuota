using System;
using CodexQuota;

internal static class SchedulerTests
{
    private static int count;

    private static void Check(bool condition, string message)
    {
        count++;
        if (!condition) throw new Exception(message);
    }

    private static QuotaSnapshot Snapshot(double fiveHourUsed, double weeklyUsed, DateTimeOffset? fiveHourReset, DateTimeOffset? weeklyReset)
    {
        return new QuotaSnapshot
        {
            FiveHour = new QuotaWindow { UsedPercent = fiveHourUsed, WindowDurationMins = 300, ResetsAt = fiveHourReset },
            Weekly = new QuotaWindow { UsedPercent = weeklyUsed, WindowDurationMins = 10080, ResetsAt = weeklyReset }
        };
    }

    private static QuotaSnapshot Snapshot(double fiveHourUsed, double weeklyUsed)
    {
        return Snapshot(fiveHourUsed, weeklyUsed, null, null);
    }

    private static int Main()
    {
        try
        {
            DateTimeOffset now_ = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
            QuotaPollScheduler adaptive_ = new QuotaPollScheduler(60, true, now_);
            Check(adaptive_.IsDue(now_) && !adaptive_.IsDue(now_.AddTicks(-1)), "New scheduler is due immediately at the requested time.");
            Check(adaptive_.BaseSeconds == 60 && adaptive_.Adaptive, "Requested base interval and adaptive mode are retained.");
            adaptive_.RecordSuccess(Snapshot(10, 20), now_);
            Check(adaptive_.CurrentDelaySeconds == 60 && adaptive_.NextDueAt == now_.AddSeconds(60), "Initial success waits the base interval.");
            Check(adaptive_.LastSuccessAt == now_, "Last success is recorded independently of future scheduling.");
            Check(!adaptive_.IsDue(now_.AddSeconds(59)) && adaptive_.IsDue(now_.AddSeconds(60)), "Due boundary is inclusive.");
            for (int index_ = 1; index_ <= 4; index_++)
            {
                DateTimeOffset tick_ = now_.AddMinutes(index_);
                adaptive_.RecordSuccess(Snapshot(10, 20), tick_);
                int expected_ = index_ >= 4 ? 300 : index_ >= 2 ? 120 : 60;
                Check(adaptive_.CurrentDelaySeconds == expected_ && adaptive_.NextDueAt == tick_.AddSeconds(expected_), "Unchanged read " + index_ + " applies its expected adaptive delay.");
            }
            adaptive_.RecordSuccess(Snapshot(11, 20), now_.AddMinutes(5));
            Check(adaptive_.CurrentDelaySeconds == 60, "Five-hour quota use restores base cadence.");
            adaptive_.RecordSuccess(Snapshot(11, 21), now_.AddMinutes(6));
            Check(adaptive_.CurrentDelaySeconds == 60, "Weekly quota use restores base cadence.");
            adaptive_.RecordSuccess(Snapshot(11, 21), now_.AddMinutes(7));
            adaptive_.RecordSuccess(Snapshot(11, 21), now_.AddMinutes(8));
            Check(adaptive_.CurrentDelaySeconds == 120, "Two newly unchanged reads slow down after quota changed.");

            QuotaPollScheduler fixed_ = new QuotaPollScheduler(60, false, now_);
            for (int index_ = 0; index_ < 7; index_++)
            {
                DateTimeOffset tick_ = now_.AddMinutes(index_);
                fixed_.RecordSuccess(Snapshot(10, 20), tick_);
                Check(fixed_.CurrentDelaySeconds == 60 && fixed_.NextDueAt == tick_.AddSeconds(60), "Fixed read " + index_ + " never reduces polling frequency.");
            }
            DateTimeOffset fixedDue_ = fixed_.NextDueAt;
            fixed_.MarkActivity(now_.AddHours(1));
            Check(fixed_.NextDueAt == fixedDue_, "Activity does not override fixed cadence.");

            QuotaPollScheduler activity_ = new QuotaPollScheduler(60, true, now_);
            activity_.MarkActivity(now_.AddSeconds(5));
            Check(activity_.NextDueAt == now_, "Activity before first success does not postpone the first request.");
            activity_.RecordSuccess(Snapshot(10, 20), now_);
            activity_.RecordSuccess(Snapshot(10, 20), now_.AddMinutes(1));
            activity_.RecordSuccess(Snapshot(10, 20), now_.AddMinutes(2));
            activity_.MarkActivity(now_.AddMinutes(2).AddSeconds(10));
            Check(activity_.CurrentDelaySeconds == 60 && activity_.NextDueAt == now_.AddMinutes(3), "Adaptive activity restores the base interval from last success.");
            activity_.RecordSuccess(Snapshot(10, 20), now_.AddMinutes(3));
            Check(activity_.CurrentDelaySeconds == 60, "Activity resets the unchanged counter.");
            activity_.RecordSuccess(Snapshot(10, 20), now_.AddMinutes(4));
            activity_.RecordSuccess(Snapshot(10, 20), now_.AddMinutes(5));
            activity_.MarkActivity(now_.AddMinutes(6).AddSeconds(10));
            Check(activity_.NextDueAt == now_.AddMinutes(6).AddSeconds(10), "Activity makes an already-overdue base poll due now, without using a past deadline.");
            activity_.RecordFailure(now_.AddMinutes(7));
            DateTimeOffset failedDue_ = activity_.NextDueAt;
            activity_.MarkActivity(now_.AddMinutes(7).AddSeconds(1));
            Check(activity_.NextDueAt == failedDue_, "Activity cannot bypass failure backoff.");

            QuotaPollScheduler failure_ = new QuotaPollScheduler(60, true, now_);
            int[] expectedFailures_ = new[] { 60, 120, 240, 300, 300 };
            for (int index_ = 0; index_ < expectedFailures_.Length; index_++)
            {
                DateTimeOffset tick_ = now_.AddMinutes(index_ * 10);
                failure_.RecordFailure(tick_);
                Check(failure_.CurrentDelaySeconds == expectedFailures_[index_] && failure_.NextDueAt == tick_.AddSeconds(expectedFailures_[index_]), "Failure " + (index_ + 1) + " uses bounded exponential backoff.");
            }
            failure_.RecordSuccess(Snapshot(10, 20), now_.AddHours(1));
            Check(failure_.CurrentDelaySeconds == 60, "Successful changed data recovers from error backoff.");
            failure_.RecordFailure(now_.AddHours(1).AddMinutes(1));
            Check(failure_.CurrentDelaySeconds == 60, "Successful read resets consecutive failure count.");

            QuotaPollScheduler reset_ = new QuotaPollScheduler(60, true, now_);
            reset_.RecordSuccess(Snapshot(10, 20, now_.AddSeconds(20), null), now_);
            Check(reset_.NextDueAt == now_.AddSeconds(21), "Server reset deadline schedules a confirmation one second after reset.");
            reset_.RecordSuccess(Snapshot(10, 20, now_.AddSeconds(20), now_.AddSeconds(10)), now_);
            Check(reset_.NextDueAt == now_.AddSeconds(11), "Earlier reset window wins.");
            reset_.RecordSuccess(Snapshot(10, 20, now_.AddSeconds(1), null), now_);
            Check(reset_.NextDueAt == now_.AddSeconds(5), "Very near reset respects a minimum five-second interval.");
            reset_.RecordSuccess(Snapshot(10, 20, now_.AddMinutes(10), null), now_);
            Check(reset_.NextDueAt > now_ && reset_.NextDueAt <= now_.AddSeconds(300), "Far reset cannot postpone the regular poll.");
            reset_.RecordSuccess(Snapshot(10, 20, now_, now_.AddSeconds(-1)), now_);
            Check(reset_.NextDueAt == now_.AddSeconds(60) && !reset_.IsDue(now_), "Expired reset timestamps use regular cadence and cannot loop requests.");
            DateTimeOffset later_ = now_.AddSeconds(60);
            reset_.RecordSuccess(Snapshot(10, 20, now_, now_.AddSeconds(-1)), later_);
            Check(reset_.NextDueAt >= later_.AddSeconds(60) && !reset_.IsDue(later_), "Repeated stale reset data remains safely scheduled into the future.");
            Check(reset_.LastSuccessAt == later_, "Local reset scheduling does not fabricate a renewed success timestamp.");
            QuotaPollScheduler fixedReset_ = new QuotaPollScheduler(60, false, now_);
            fixedReset_.RecordSuccess(Snapshot(10, 20, now_.AddSeconds(10), null), now_);
            Check(fixedReset_.NextDueAt == now_.AddSeconds(11), "Reset confirmation is available in fixed mode too.");
            Check(new QuotaPollScheduler(1, true, now_).BaseSeconds == 30 && new QuotaPollScheduler(999, true, now_).BaseSeconds == 300, "Base interval normalization retains safe bounds.");
            fixedReset_.RecordSuccess(Snapshot(10,20,DateTimeOffset.MaxValue,null),now_);
            Check(fixedReset_.NextDueAt == now_.AddSeconds(60),"Maximum server timestamp cannot overflow reset confirmation scheduling.");
            Console.WriteLine("PASS: " + count + " deterministic polling cadence, activity, backoff and reset scheduling checks.");
            return 0;
        }
        catch (Exception exception_)
        {
            Console.Error.WriteLine("FAIL: " + exception_.Message);
            return 1;
        }
    }
}
