namespace MusicStreaming.Application.Common;

/// <summary>Какого размера обложку просят.</summary>
public enum CoverSize
{
    Full,
    Thumb,
}

/// <summary>
/// Размеры, в которых хранятся обложки.
///
/// <para>
/// Их два, а не сколько угодно: каждый размер — это ещё один файл на диске для каждого альбома.
/// Полный нужен странице альбома и полноэкранному плееру, миниатюра — спискам, где иначе на экран
/// приезжали бы десятки полноразмерных картинок.
/// </para>
/// </summary>
public static class CoverVariants
{
    public const int FullEdge = 640;
    public const int ThumbEdge = 256;

    public static readonly IReadOnlyList<int> Edges = [FullEdge, ThumbEdge];

    public const string ThumbSuffix = ".thumb.webp";
}
