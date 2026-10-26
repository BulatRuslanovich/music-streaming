// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Xunit;
using App.Common;
using App.Dtos;
using App.Services.Integrations;
using Infrastructure.Integrations;

namespace UnitTests;

public class YandexMusicTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_file_link_is_signed_like_the_web_client()
    {
        // Эталон посчитан тем же алгоритмом, что в веб-клиенте Яндекса (HMAC-SHA256, base64 без «=»).
        var sign = YandexMusicClient.Sign("7tvSmFbyf5hJnIHhCimDDD", "1761500000" + "12345" + "lossless" + "flac-mp4mp3" + "raw");

        Assert.Equal("ogHUl0+hxgeA/1o8u1Bsyqs8CAI+LGy7yBgBXUD1po0", sign);
    }

    [Fact]
    public void The_same_source_cannot_run_twice_at_once()
    {
        var queue = new YandexImportQueue(new FixedClock(Now));

        queue.Enqueue(YandexImportKind.Likes, null, "token");

        Assert.Throws<ConflictException>(() => queue.Enqueue(YandexImportKind.Likes, null, "token"));
        queue.Enqueue(YandexImportKind.Album, "42", "token");
    }

    [Fact]
    public void Removing_a_running_import_cancels_it_and_removing_a_finished_one_forgets_it()
    {
        var queue = new YandexImportQueue(new FixedClock(Now));
        var job = queue.Enqueue(YandexImportKind.Album, "42", "token");

        Assert.True(queue.Remove(job.Id));
        Assert.True(job.Cancelled.IsCancellationRequested);
        Assert.Single(queue.List());

        job.Finish();
        Assert.Equal(YandexImportStatus.Cancelled, job.ToDto().Status);

        Assert.True(queue.Remove(job.Id));
        Assert.Empty(queue.List());
    }

    [Fact]
    public void Progress_counts_every_outcome_as_done()
    {
        var job = new YandexImportQueue(new FixedClock(Now)).Enqueue(YandexImportKind.Likes, null, "token");

        job.Start(null, 3);
        job.Added();
        job.Skipped();
        job.Failed("Artist — Title", "Nope");

        var dto = job.ToDto();
        Assert.Equal((3, 1, 1), (dto.Done, dto.Added, dto.Skipped));
        Assert.Equal("Artist — Title", Assert.Single(dto.Failures).Title);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
