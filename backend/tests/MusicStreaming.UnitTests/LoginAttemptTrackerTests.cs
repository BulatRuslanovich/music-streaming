// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Services;
using Xunit;

namespace MusicStreaming.UnitTests;

public class LoginAttemptTrackerTests
{
    [Fact]
    public void An_account_locks_once_the_configured_number_of_attempts_is_spent()
    {
        var (tracker, _) = Tracker(attempts: 3);

        tracker.RecordFailure("bulat");
        tracker.RecordFailure("bulat");
        Assert.Null(tracker.LockoutRemaining("bulat"));

        tracker.RecordFailure("bulat");
        Assert.NotNull(tracker.LockoutRemaining("bulat"));
    }

    [Fact]
    public void The_lock_only_covers_the_account_that_was_guessed_at()
    {
        var (tracker, _) = Tracker(attempts: 2);

        tracker.RecordFailure("bulat");
        tracker.RecordFailure("bulat");

        Assert.NotNull(tracker.LockoutRemaining("bulat"));
        Assert.Null(tracker.LockoutRemaining("someone-else"));
    }

    [Fact]
    public void The_lock_expires_after_the_configured_window()
    {
        var (tracker, clock) = Tracker(attempts: 2, minutes: 15);

        tracker.RecordFailure("bulat");
        tracker.RecordFailure("bulat");
        Assert.NotNull(tracker.LockoutRemaining("bulat"));

        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Null(tracker.LockoutRemaining("bulat"));
    }

    [Fact]
    public void Occasional_typos_spread_over_time_never_add_up_to_a_lock()
    {
        var (tracker, clock) = Tracker(attempts: 3, minutes: 15);

        for (var day = 0; day < 5; day++)
        {
            tracker.RecordFailure("bulat");
            clock.Advance(TimeSpan.FromDays(1));
        }

        Assert.Null(tracker.LockoutRemaining("bulat"));
    }

    [Fact]
    public void Signing_in_successfully_clears_the_attempts_behind_it()
    {
        var (tracker, _) = Tracker(attempts: 3);

        tracker.RecordFailure("bulat");
        tracker.RecordFailure("bulat");
        tracker.RecordSuccess("bulat");
        tracker.RecordFailure("bulat");

        Assert.Null(tracker.LockoutRemaining("bulat"));
    }

    [Fact]
    public void Setting_the_attempt_limit_to_zero_turns_the_lock_off()
    {
        var (tracker, _) = Tracker(attempts: 0);

        for (var attempt = 0; attempt < 50; attempt++)
            tracker.RecordFailure("bulat");

        Assert.Null(tracker.LockoutRemaining("bulat"));
    }

    private static (LoginAttemptTracker Tracker, TestClock Clock) Tracker(
        int attempts, int minutes = 15)
    {
        var clock = new TestClock();
        var options = Options.Create(new SecurityOptions
        {
            AccountLockoutAttempts = attempts,
            AccountLockoutMinutes = minutes,
        });

        return (new LoginAttemptTracker(options, clock), clock);
    }
}
