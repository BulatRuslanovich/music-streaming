namespace MusicStreaming.Application.Common;

/// <param name="Extension">Расширение с точкой, в нижнем регистре. Оно же становится расширением файла в хранилище.</param>
/// <param name="MimeType">Тип, которым файл отдаётся браузеру.</param>
/// <param name="TagLibMimeType">
/// Псевдоним, которым у TagLib выбирается разборщик. Это не то же самое, что <paramref name="MimeType"/>:
/// например, <c>audio/mp4</c> TagLib не знает вовсе, а собственный <c>taglib/m4a</c> — знает.
/// </param>
public readonly record struct AudioFormat(string Extension, string MimeType, string TagLibMimeType)
{
    /// <summary>Имя формата для сообщений человеку: <c>FLAC</c>, <c>MP3</c>.</summary>
    public string Label => Extension[1..].ToUpperInvariant();
}

/// <summary>
/// Форматы, которые библиотека принимает.
///
/// <para>
/// Настоящий фильтр здесь один — разбор TagLib, который происходит уже над сохранённым файлом.
/// Расширение лишь выбирает, каким разборщиком читать; заявленный браузером тип содержимого не
/// проверяется вовсе, потому что для одного и того же файла разные браузеры присылают то
/// <c>audio/flac</c>, то <c>audio/x-flac</c>, то <c>application/octet-stream</c>, то пустую строку,
/// а подделывается он в любом случае тривиально.
/// </para>
/// </summary>
public static class AudioUpload
{
    private static readonly Dictionary<string, AudioFormat> ByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"] = new(".mp3", "audio/mpeg", "taglib/mp3"),
            [".flac"] = new(".flac", "audio/flac", "taglib/flac"),
            [".m4a"] = new(".m4a", "audio/mp4", "taglib/m4a"),
        };

    /// <summary>Перечисление для сообщения об ошибке — единственный список, который видит человек.</summary>
    public static readonly string Accepted = string.Join(", ", ByExtension.Keys);

    public static AudioFormat? For(string fileName) =>
        ByExtension.TryGetValue(Path.GetExtension(fileName), out var format) ? format : null;

    /// <summary>
    /// Расширение, которое файлу полагается по его первым байтам, — или <c>null</c>, если по ним
    /// ничего не понять.
    /// </summary>
    ///
    /// <remarks>
    /// Опознаются только контейнеры с однозначной сигнатурой в начале, и служит это ровно одному:
    /// поймать явную ложь про расширение. Разборщик MP3 сам по себе такой лжи не ловит — сигнатура
    /// кадра это всего два байта, и в чужом файле она находится случайно: <c>.m4a</c>,
    /// переименованный в <c>.mp3</c>, разбирается «успешно» и даёт неверную длительность.
    ///
    /// <para>
    /// Обратное — что незнакомое начало означает подделку — отсюда не следует, поэтому
    /// неопознанное пропускается дальше к TagLib. У MP3 своей надёжной сигнатуры нет: он может
    /// начинаться и с <c>ID3</c>, и сразу с кадра, и с постороннего мусора перед ним.
    /// </para>
    /// </remarks>
    public static string? SniffContainer(string absolutePath)
    {
        Span<byte> head = stackalloc byte[8];

        using (var file = File.OpenRead(absolutePath))
        {
            if (file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length)
                return null;
        }

        if (head[..4].SequenceEqual("fLaC"u8))
            return ".flac";

        // ISO BMFF: четыре байта размера, затем тип бокса. Первым всегда ftyp.
        if (head[4..8].SequenceEqual("ftyp"u8))
            return ".m4a";

        return null;
    }
}
