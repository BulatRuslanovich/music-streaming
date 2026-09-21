// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics.Tensors;

namespace MusicStreaming.Application.Recommendations.Embeddings;

/// <param name="SongKey">"артист|название" в нижнем регистре; пусто, если одна из частей пуста.</param>
public readonly record struct TrackVectorMeta(
    Guid TrackId,
    Guid ArtistId,
    Guid? AlbumId,
    Guid? GenreId,
    string ContentHash,
    string SongKey,
    DateTimeOffset CreatedAt,
    int ClusterId,
    int ShownCount,
    int SkippedEarlyCount);

public readonly record struct ScoredRow(int Row, Guid TrackId, float Score);

/// <summary>
/// Сходство двух строк матрицы. Узкий шов, чтобы <c>Diversifier</c> считал MMR в пространстве
/// эмбеддингов, не таская <c>float[]</c> в каждом кандидате: 2 КБ на 600 кандидатов — мегабайт
/// копирования на генерацию впустую.
/// </summary>
public interface IVectorSimilarity
{
    double Between(int rowA, int rowB);
}

/// <summary>
/// Неизменяемый снимок матрицы эмбеддингов. Читатель берёт его один раз и работает без блокировок;
/// пересборка строит новый снимок в фоне и меняет ссылку целиком.
/// <para>
/// Это сознательное отличие от musik, который берёт RWMutex вокруг каждого скалярного произведения
/// и тем сериализует восьмитысячный внутренний цикл на reader-writer локе.
/// </para>
/// </summary>
public sealed class EmbeddingSnapshot : IVectorSimilarity
{
    /// <summary>Ниже этого N параллельный проход не окупает разбиение диапазона.</summary>
    private const int ParallelThreshold = 1500;

    private readonly float[] _matrix;           // row-major, Count * Dimension
    private readonly TrackVectorMeta[] _meta;
    private readonly Dictionary<Guid, int> _rowByTrack;
    private readonly Dictionary<string, List<Guid>> _byContentHash;
    private readonly Dictionary<string, List<Guid>> _bySongKey;
    private readonly Dictionary<Guid, float[]> _artistCentroids;
    private readonly Dictionary<int, int[]> _rowsByCluster;

    public int Count { get; }
    public int Dimension { get; }
    public DateTimeOffset BuiltAt { get; }

    public static EmbeddingSnapshot Empty { get; } = new();

    private EmbeddingSnapshot()
    {
        _matrix = [];
        _meta = [];
        _rowByTrack = [];
        _byContentHash = [];
        _bySongKey = [];
        _artistCentroids = [];
        _rowsByCluster = [];
        BuiltAt = DateTimeOffset.MinValue;
    }

    public EmbeddingSnapshot(
        float[] matrix,
        TrackVectorMeta[] meta,
        int dimension,
        DateTimeOffset builtAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);

        if (matrix.Length != meta.Length * dimension)
            throw new ArgumentException(
                $"Matrix holds {matrix.Length} floats, expected {meta.Length} x {dimension}.", nameof(matrix));

        _matrix = matrix;
        _meta = meta;
        Count = meta.Length;
        Dimension = dimension;
        BuiltAt = builtAt;

        _rowByTrack = new Dictionary<Guid, int>(Count);
        _byContentHash = [];
        _bySongKey = [];

        var clusters = new Dictionary<int, List<int>>();

        for (var row = 0; row < Count; row++)
        {
            var item = meta[row];
            _rowByTrack[item.TrackId] = row;

            if (!string.IsNullOrEmpty(item.ContentHash))
                Append(_byContentHash, item.ContentHash, item.TrackId);

            if (!string.IsNullOrEmpty(item.SongKey))
                Append(_bySongKey, item.SongKey, item.TrackId);

            if (item.ClusterId >= 0)
            {
                if (!clusters.TryGetValue(item.ClusterId, out var rows))
                    clusters[item.ClusterId] = rows = [];

                rows.Add(row);
            }
        }

        _rowsByCluster = clusters.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        _artistCentroids = BuildCentroids(row => meta[row].ArtistId);
    }

    public bool IsEmpty => Count == 0;

    /// <summary>Строка трека в матрице или -1, если трек не заэмбежжен.</summary>
    public int RowOf(Guid trackId) => _rowByTrack.GetValueOrDefault(trackId, -1);

    public TrackVectorMeta MetaAt(int row) => _meta[row];

    public ReadOnlySpan<float> Vector(int row) => _matrix.AsSpan(row * Dimension, Dimension);

    public double Between(int rowA, int rowB) =>
        rowA < 0 || rowB < 0 || rowA >= Count || rowB >= Count
            ? 0
            : TensorPrimitives.Dot(Vector(rowA), Vector(rowB));

    /// <summary>Косинусы запроса ко всем строкам. <paramref name="destination"/> длины <see cref="Count"/>.</summary>
    public void SimilaritiesTo(ReadOnlySpan<float> query, Span<float> destination)
    {
        if (destination.Length < Count)
            throw new ArgumentException($"Destination must hold at least {Count} floats.", nameof(destination));

        if (query.Length != Dimension)
        {
            destination[..Count].Clear();
            return;
        }

        if (Count < ParallelThreshold || Environment.ProcessorCount < 2)
        {
            for (var row = 0; row < Count; row++)
                destination[row] = TensorPrimitives.Dot(query, Vector(row));

            return;
        }

        // Параллельный проход по непрерывным блокам строк. На 50k экономит единицы миллисекунд —
        // порог унаследован от musik, где скалярный цикл был на порядок дороже, и оставлен
        // потому что бесплатен, а не потому что критичен.
        var matrix = _matrix;
        var dimension = Dimension;
        var queryCopy = query.ToArray();

        var destinationArray = destination[..Count].ToArray();
        var partitioner = System.Collections.Concurrent.Partitioner.Create(0, Count);

        Parallel.ForEach(partitioner, range =>
        {
            for (var row = range.Item1; row < range.Item2; row++)
            {
                destinationArray[row] = TensorPrimitives.Dot(
                    queryCopy, matrix.AsSpan(row * dimension, dimension));
            }
        });

        destinationArray.CopyTo(destination[..Count]);
    }

    public float[] SimilaritiesTo(ReadOnlySpan<float> query)
    {
        var result = new float[Count];
        SimilaritiesTo(query, result);
        return result;
    }

    /// <summary>
    /// Точный top-k через min-кучу размера k, без полной сортировки. Порядок при равных оценках —
    /// по возрастанию строки, чтобы результат был воспроизводим.
    /// </summary>
    public IReadOnlyList<ScoredRow> TopK(ReadOnlySpan<float> query, int k, IReadOnlySet<Guid>? exclude = null)
    {
        if (k <= 0 || Count == 0 || query.Length != Dimension)
            return [];

        var scores = SimilaritiesTo(query);
        var heap = new PriorityQueue<int, (float Score, int NegativeRow)>(k);

        for (var row = 0; row < Count; row++)
        {
            if (exclude is not null && exclude.Contains(_meta[row].TrackId))
                continue;

            var priority = (scores[row], -row);

            if (heap.Count < k)
            {
                heap.Enqueue(row, priority);
                continue;
            }

            heap.EnqueueDequeue(row, priority);
        }

        var result = new List<ScoredRow>(heap.Count);
        while (heap.TryDequeue(out var row, out var priority))
            result.Add(new ScoredRow(row, _meta[row].TrackId, priority.Score));

        result.Reverse();
        return result;
    }

    public float[] Centroid(ReadOnlySpan<int> rows)
    {
        if (rows.IsEmpty)
            return [];

        var accumulator = new double[Dimension];

        foreach (var row in rows)
        {
            var vector = Vector(row);
            for (var i = 0; i < Dimension; i++)
                accumulator[i] += vector[i];
        }

        var result = new float[Dimension];
        for (var i = 0; i < Dimension; i++)
            result[i] = (float)(accumulator[i] / rows.Length);

        VectorMath.NormalizeInPlace(result);
        return result;
    }

    public float[]? ArtistCentroid(Guid artistId) => _artistCentroids.GetValueOrDefault(artistId);

    /// <summary>Строки одного кластера. Очередь даёт небольшую надбавку за совпадение с текущим.</summary>
    public IReadOnlyList<int> RowsInCluster(int clusterId) => _rowsByCluster.GetValueOrDefault(clusterId, []);




    /// <summary>
    /// Трек и все его двойники: байт-идентичные файлы и та же песня под другим файлом.
    /// Радио исключает всю семью разом, иначе один и тот же трек приходит дважды под разными id.
    /// </summary>
    public IReadOnlyList<Guid> CloneIds(Guid trackId)
    {
        var row = RowOf(trackId);
        if (row < 0)
            return [trackId];

        var item = _meta[row];
        var family = new HashSet<Guid> { trackId };

        if (!string.IsNullOrEmpty(item.ContentHash) && _byContentHash.TryGetValue(item.ContentHash, out var byHash))
            family.UnionWith(byHash);

        if (!string.IsNullOrEmpty(item.SongKey) && _bySongKey.TryGetValue(item.SongKey, out var bySong))
            family.UnionWith(bySong);

        return [.. family];
    }

    /// <summary>
    /// Максимум взвешенного косинуса ко всем сидам. Это то, что раньше приходило из
    /// track_similarity.audio_score, и теперь берётся из памяти без обращения к БД.
    /// </summary>
    public double SeedSimilarity(int row, IReadOnlyList<(int Row, double Weight)> seeds)
    {
        if (row < 0 || seeds.Count == 0)
            return 0;

        var best = 0.0;
        foreach (var (seedRow, weight) in seeds)
        {
            if (seedRow < 0 || seedRow == row)
                continue;

            best = Math.Max(best, weight * Between(row, seedRow));
        }

        return best;
    }

    /// <summary>
    /// Перцентиль косинуса в библиотеке: доля треков, к которым запрос ближе, чем к данному.
    /// Скоринг кормится именно им, а не сырым косинусом — CLAP-косинусы между музыкальными
    /// треками занимают узкую полосу, зависящую от библиотеки, и сырое значение сделало бы
    /// вес непереносимым между установками.
    /// </summary>
    public static float[] ToPercentiles(ReadOnlySpan<float> similarities)
    {
        var count = similarities.Length;
        if (count == 0)
            return [];

        if (count == 1)
            return [1f];

        var order = new int[count];
        for (var i = 0; i < count; i++)
            order[i] = i;

        var copy = similarities.ToArray();
        Array.Sort(order, (a, b) => copy[a].CompareTo(copy[b]));

        var result = new float[count];
        for (var rank = 0; rank < count; rank++)
            result[order[rank]] = rank / (float)(count - 1);

        return result;
    }

    private Dictionary<Guid, float[]> BuildCentroids(Func<int, Guid?> keyOf)
    {
        var groups = new Dictionary<Guid, List<int>>();

        for (var row = 0; row < Count; row++)
        {
            if (keyOf(row) is not { } key || key == Guid.Empty)
                continue;

            if (!groups.TryGetValue(key, out var rows))
                groups[key] = rows = [];

            rows.Add(row);
        }

        var centroids = new Dictionary<Guid, float[]>(groups.Count);
        foreach (var (key, rows) in groups)
            centroids[key] = Centroid(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows));

        return centroids;
    }

    private static void Append(Dictionary<string, List<Guid>> map, string key, Guid trackId)
    {
        if (!map.TryGetValue(key, out var ids))
            map[key] = ids = [];

        ids.Add(trackId);
    }

    /// <summary>"артист|название" в нижнем регистре — ключ для поиска той же песни в другом файле.</summary>
    public static string SongKeyOf(string? artist, string? title)
    {
        var left = artist?.Trim();
        var right = title?.Trim();

        return string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)
            ? string.Empty
            : $"{left.ToLowerInvariant()}|{right.ToLowerInvariant()}";
    }
}
