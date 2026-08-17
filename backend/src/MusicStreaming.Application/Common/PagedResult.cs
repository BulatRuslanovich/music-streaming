namespace MusicStreaming.Application.Common;

/// <summary>Одна страница выдачи вместе со сведениями, нужными клиенту для навигации.</summary>
/// <param name="Items">Элементы этой страницы.</param>
/// <param name="Total">Сколько элементов всего удовлетворяет запросу, а не сколько их на странице.</param>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>
/// Нормализованные параметры страницы.
///
/// <para>
/// Приводит к допустимым значениям вместо того, чтобы отвергать: отрицательный номер и размер
/// в миллион приходят от опечатки в адресной строке, а не от злого умысла, и правильный ответ на
/// них — первая страница разумного размера, а не ошибка. Потолок при этом обязателен: без него
/// один запрос вытащил бы всю библиотеку в память.
/// </para>
/// </summary>
public record PageRequest
{
    /// <summary>Больше этого не отдаётся, сколько бы ни попросили.</summary>
    public const int MaxPageSize = 200;

    public int Page { get; }
    public int PageSize { get; }

    public PageRequest(int? page = null, int? pageSize = null)
    {
        Page = page is null or < 1 ? 1 : page.Value;
        PageSize = pageSize is null or < 1 ? 50 : Math.Min(pageSize.Value, MaxPageSize);
    }

    public int Skip => (Page - 1) * PageSize;
}
