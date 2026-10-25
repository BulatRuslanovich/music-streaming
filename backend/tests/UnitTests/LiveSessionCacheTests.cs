// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace UnitTests;

public class LiveSessionCacheTests
{
    private readonly LiveSessionCache _sessions = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void An_unseen_session_is_not_known_alive() =>
        Assert.False(_sessions.IsKnownAlive(Guid.NewGuid()));

    [Fact]
    public void A_remembered_session_is_known_alive()
    {
        var session = Guid.NewGuid();

        _sessions.RememberAlive(session, Guid.NewGuid());

        Assert.True(_sessions.IsKnownAlive(session));
    }

    [Fact]
    public void Signing_out_forgets_only_that_session()
    {
        var user = Guid.NewGuid();
        var (phone, laptop) = (Guid.NewGuid(), Guid.NewGuid());
        _sessions.RememberAlive(phone, user);
        _sessions.RememberAlive(laptop, user);

        _sessions.Forget(phone);

        Assert.False(_sessions.IsKnownAlive(phone));
        Assert.True(_sessions.IsKnownAlive(laptop));
    }

    [Fact]
    public void Revoking_a_user_forgets_all_their_sessions_and_nobody_elses()
    {
        var (user, other) = (Guid.NewGuid(), Guid.NewGuid());
        var (phone, laptop, theirs) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _sessions.RememberAlive(phone, user);
        _sessions.RememberAlive(laptop, user);
        _sessions.RememberAlive(theirs, other);

        _sessions.ForgetUser(user);

        Assert.False(_sessions.IsKnownAlive(phone));
        Assert.False(_sessions.IsKnownAlive(laptop));
        Assert.True(_sessions.IsKnownAlive(theirs));
    }

    [Fact]
    public void A_session_signed_in_after_a_revocation_is_remembered_again()
    {
        var user = Guid.NewGuid();
        _sessions.RememberAlive(Guid.NewGuid(), user);
        _sessions.ForgetUser(user);

        var fresh = Guid.NewGuid();
        _sessions.RememberAlive(fresh, user);

        Assert.True(_sessions.IsKnownAlive(fresh));
    }
}
