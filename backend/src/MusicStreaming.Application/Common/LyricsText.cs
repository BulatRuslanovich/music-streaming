using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Common;

/// <summary>Разобранный текст песни: всегда читаемый простой текст и, если повезло, строки с таймингом.</summary>
/// <param name="Plain">Текст без меток времени.</param>
/// <param name="Lines">Строки с метками времени по возрастанию; пусто, если тайминга нет.</param>
public record ParsedLyrics(string Plain, IReadOnlyList<LyricLine> Lines)
{
    public static readonly ParsedLyrics Empty = new(string.Empty, []);

    public bool IsEmpty => Plain.Length == 0 && Lines.Count == 0;
}

/// <summary>
/// Единственный разбор текста песни в проекте.
///
/// <para>
/// Формат один и тот же, откуда бы текст ни пришёл: LRC одинаково часто лежит и в поле
/// «несинхронизированного» текста файла (теггеры кладут его именно туда), и в файле <c>.lrc</c>,
/// и в том, что администратор вставил руками. Поэтому разбор здесь один и не знает про источник —
/// он принимает строку и отвечает, есть ли в ней тайминг.
/// </para>
/// </summary>
public static partial class LyricsText
{
    /// <summary>Предохранитель от испорченного тега: длиннее любого настоящего текста песни.</summary>
    public const int MaxLength = 20_000;

    /// <summary>Разбирает произвольный текст: LRC превращается в строки с таймингом, всё остальное остаётся простым текстом.</summary>
    public static ParsedLyrics Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ParsedLyrics.Empty;

        var text = raw.Replace("\0", string.Empty).ReplaceLineEndings("\n");
        if (text.Length > MaxLength)
            text = text[..MaxLength];

        var offset = OffsetOf(text);
        var timed = new List<LyricLine>();
        var plain = new StringBuilder();

        foreach (var line in text.Split('\n'))
        {
            var stamps = TimestampPattern().Matches(line);
            var content = (stamps.Count > 0 ? TimestampPattern().Replace(line, string.Empty) : line).Trim();

            if (stamps.Count == 0)
            {
                // Метаданные LRC ([ar:…], [offset:…]) — не текст песни и в неё не попадают.
                if (content.Length > 0 && !MetadataPattern().IsMatch(content))
                    AppendLine(plain, content);

                continue;
            }

            foreach (Match stamp in stamps)
                timed.Add(new LyricLine(Math.Max(0, MillisecondsOf(stamp) + offset), content));

            AppendLine(plain, content);
        }

        return Build(plain.ToString(), timed);
    }

    /// <summary>
    /// Собирает результат из строк, у которых тайминг уже известен, — так приходит текст из кадра
    /// SYLT, где время лежит отдельным полем и разбирать нечего.
    /// </summary>
    public static ParsedLyrics FromTimedLines(IEnumerable<LyricLine> lines)
    {
        var timed = lines.Where(line => line.At >= 0).ToList();
        if (timed.Count == 0)
            return ParsedLyrics.Empty;

        var plain = new StringBuilder();
        foreach (var line in timed.OrderBy(line => line.At))
            AppendLine(plain, line.Text.Trim());

        return Build(plain.ToString(), timed);
    }

    private static ParsedLyrics Build(string plain, List<LyricLine> timed)
    {
        var text = plain.TrimEnd('\n');

        return text.Length == 0 && timed.Count == 0
            ? ParsedLyrics.Empty
            : new ParsedLyrics(text, Order(timed));
    }

    /// <summary>
    /// Упорядочивает по времени и снимает дубликаты меток: повторная метка сбила бы подсветку,
    /// которая ищет последнюю строку до текущей позиции.
    /// </summary>
    private static List<LyricLine> Order(List<LyricLine> timed) =>
        [.. timed
            .GroupBy(line => line.At)
            .OrderBy(group => group.Key)
            .Select(group => group.First())];

    /// <summary>Пустые строки не удваиваются: разрыв между куплетами остаётся одним разрывом.</summary>
    private static void AppendLine(StringBuilder plain, string content)
    {
        if (content.Length == 0 && (plain.Length == 0 || plain[^1] == '\n' && plain.Length >= 2 && plain[^2] == '\n'))
            return;

        plain.Append(content).Append('\n');
    }

    private static int MillisecondsOf(Match stamp)
    {
        var minutes = int.Parse(stamp.Groups["m"].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(stamp.Groups["s"].Value, CultureInfo.InvariantCulture);

        var fraction = stamp.Groups["f"].Value;
        var milliseconds = fraction.Length switch
        {
            0 => 0,
            1 => int.Parse(fraction, CultureInfo.InvariantCulture) * 100,
            2 => int.Parse(fraction, CultureInfo.InvariantCulture) * 10,
            _ => int.Parse(fraction[..3], CultureInfo.InvariantCulture),
        };

        return (minutes * 60 + seconds) * 1000 + milliseconds;
    }

    /// <summary>Сдвиг всех меток из тега <c>[offset:…]</c>, в миллисекундах; положительный — раньше.</summary>
    private static int OffsetOf(string text) =>
        OffsetPattern().Match(text) is { Success: true } match
        && int.TryParse(match.Groups["ms"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms)
            ? -ms
            : 0;

    [GeneratedRegex(@"\[(?<m>\d{1,3}):(?<s>[0-5]?\d)(?:[.:](?<f>\d{1,3}))?\]")]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^\[[a-z]{2,10}:.*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex MetadataPattern();

    [GeneratedRegex(@"\[offset:\s*(?<ms>[+-]?\d{1,6})\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();
}
