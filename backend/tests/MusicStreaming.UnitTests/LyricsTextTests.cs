using MusicStreaming.Application.Common;
using MusicStreaming.Domain.Entities;
using Xunit;

namespace MusicStreaming.UnitTests;

public class LyricsTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void Nothing_in_the_tag_means_no_lyrics(string? raw) =>
        Assert.True(LyricsText.Parse(raw).IsEmpty);

    [Fact]
    public void Plain_text_stays_plain()
    {
        var parsed = LyricsText.Parse("First line\r\nSecond line");

        Assert.Empty(parsed.Lines);
        Assert.Equal("First line\nSecond line", parsed.Plain);
    }

    [Fact]
    public void Lrc_timestamps_become_timed_lines()
    {
        var parsed = LyricsText.Parse("[00:12.50]First line\n[01:02.00]Second line");

        Assert.Equal([new LyricLine(12_500, "First line"), new LyricLine(62_000, "Second line")], parsed.Lines);

        // Простой текст остаётся читаемым и без меток — на нём держится показ, когда синхронизация не нужна.
        Assert.Equal("First line\nSecond line", parsed.Plain);
    }

    [Theory]
    [InlineData("[00:01]word", 1_000)]
    [InlineData("[00:01.5]word", 1_500)]
    [InlineData("[00:01.25]word", 1_250)]
    [InlineData("[00:01.250]word", 1_250)]
    [InlineData("[00:01:25]word", 1_250)]
    public void Fractions_are_read_at_whatever_precision_the_tag_used(string raw, int expected) =>
        Assert.Equal(expected, LyricsText.Parse(raw).Lines[0].At);

    [Fact]
    public void A_repeated_chorus_lands_at_each_of_its_timestamps()
    {
        var parsed = LyricsText.Parse("[00:10.00][01:30.00]Chorus");

        Assert.Equal([new LyricLine(10_000, "Chorus"), new LyricLine(90_000, "Chorus")], parsed.Lines);

        // Но читать один и тот же куплет дважды подряд никто не просил.
        Assert.Equal("Chorus", parsed.Plain);
    }

    [Fact]
    public void Metadata_tags_are_not_part_of_the_song()
    {
        var parsed = LyricsText.Parse("[ar:Some Artist]\n[ti:Some Title]\n[00:05.00]Actual words");

        Assert.Equal("Actual words", parsed.Plain);
        Assert.Single(parsed.Lines);
    }

    [Fact]
    public void The_offset_tag_shifts_every_timestamp()
    {
        // Положительный offset означает, что текст идёт раньше звука, поэтому метки уезжают назад.
        var parsed = LyricsText.Parse("[offset:+500]\n[00:10.00]Line");

        Assert.Equal(9_500, parsed.Lines[0].At);
    }

    [Fact]
    public void A_shift_past_the_start_clamps_instead_of_going_negative()
    {
        var parsed = LyricsText.Parse("[offset:+5000]\n[00:01.00]Line");

        Assert.Equal(0, parsed.Lines[0].At);
    }

    [Fact]
    public void Instrumental_gaps_keep_their_place()
    {
        // Строка без слов двигает подсветку с последнего куплета — без неё она висела бы весь проигрыш.
        var parsed = LyricsText.Parse("[00:10.00]Words\n[00:20.00]");

        Assert.Equal(2, parsed.Lines.Count);
        Assert.Equal(string.Empty, parsed.Lines[1].Text);
    }

    [Fact]
    public void Duplicate_timestamps_are_collapsed()
    {
        // Подсветка ищет последнюю строку до текущей позиции; две строки на одной метке сделали бы
        // выбор произвольным.
        var parsed = LyricsText.Parse("[00:10.00]First\n[00:10.00]Second");

        Assert.Single(parsed.Lines);
    }

    [Fact]
    public void Out_of_order_timestamps_are_sorted()
    {
        var parsed = LyricsText.Parse("[01:00.00]Later\n[00:30.00]Earlier");

        Assert.Equal([30_000, 60_000], parsed.Lines.Select(line => line.At));
    }

    [Fact]
    public void A_malformed_tag_degrades_to_plain_text()
    {
        // Ни одна из этих скобок не метка времени, поэтому текст остаётся текстом, а не теряется.
        var parsed = LyricsText.Parse("[99:99:99]Broken\n[xx:yy]Also broken");

        Assert.Empty(parsed.Lines);
        Assert.Contains("Broken", parsed.Plain);
    }

    [Fact]
    public void Null_bytes_from_a_broken_tag_are_stripped()
    {
        var parsed = LyricsText.Parse("Clean\0 text");

        Assert.Equal("Clean text", parsed.Plain);
    }

    [Fact]
    public void An_absurdly_long_tag_is_truncated()
    {
        var parsed = LyricsText.Parse(new string('a', LyricsText.MaxLength * 2));

        Assert.Equal(LyricsText.MaxLength, parsed.Plain.Length);
    }

    [Fact]
    public void Synced_frames_arrive_already_timed()
    {
        var parsed = LyricsText.FromTimedLines(
            [new LyricLine(2_000, "Second"), new LyricLine(1_000, "First")]);

        Assert.Equal([1_000, 2_000], parsed.Lines.Select(line => line.At));
        Assert.Equal("First\nSecond", parsed.Plain);
    }

    [Fact]
    public void A_synced_frame_with_no_usable_lines_is_no_lyrics()
    {
        Assert.True(LyricsText.FromTimedLines([]).IsEmpty);
        Assert.True(LyricsText.FromTimedLines([new LyricLine(-1, "Before the start")]).IsEmpty);
    }
}
