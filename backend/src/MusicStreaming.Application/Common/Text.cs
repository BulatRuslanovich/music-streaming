namespace MusicStreaming.Application.Common;

/// <summary>Мелочи для работы со строками, общие на весь слой приложения.</summary>
public static class Text
{
    /// <summary>
    /// Обрезает пробелы, а пустое превращает в <c>null</c>.
    ///
    /// <para>
    /// Нужно потому, что «поле не заполнено» приходит от клиентов тремя разными способами —
    /// отсутствующим полем, пустой строкой и строкой из пробелов, — а означает одно и то же.
    /// </para>
    /// </summary>
    public static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
