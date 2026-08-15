namespace MusicStreaming.Application.Common;

/// <summary>
/// Насколько точно значение отвечает запросу: 0 — лучше некуда, 4 — совпало где-то ещё.
///
/// <para>
/// Это функция самой базы (<c>search_rank</c>, заводится миграцией), а не выражение на C#. Ранжируют
/// одинаково исполнители, альбомы, треки и жанры — четыре запроса на одно правило, и повторять его
/// в каждом означало бы четыре места, где оно может разойтись. В SQL же оно записывается один раз,
/// читается как обычный вызов и попадает прямо в <c>ORDER BY</c>, не заставляя тащить результат в
/// память ради сортировки.
/// </para>
/// </summary>
public static class SearchRank
{
    public const string FunctionName = "search_rank";

    /// <summary>Точное совпадение.</summary>
    public const int Exact = 0;

    /// <summary>Значение начинается с запроса.</summary>
    public const int Prefix = 1;

    /// <summary>С запроса начинается какое-то слово внутри значения.</summary>
    public const int WordPrefix = 2;

    /// <summary>Запрос встречается внутри значения.</summary>
    public const int Contains = 3;

    /// <summary>Само значение не совпало — объект попал в выдачу по смежному полю (например, трек по имени исполнителя).</summary>
    public const int Unrelated = 4;

    /// <summary>Ранг значения относительно запроса. Вызывается только внутри LINQ-запроса; выполняет его база.</summary>
    /// <param name="value">Нормализованная колонка (название, имя).</param>
    /// <param name="term">Нормализованный запрос — <see cref="SearchTerm.Value"/>.</param>
    public static int Of(string value, string term) =>
        throw new NotSupportedException($"{FunctionName} is evaluated by the database.");
}
