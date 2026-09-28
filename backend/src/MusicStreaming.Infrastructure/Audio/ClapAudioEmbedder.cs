// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>
/// Вектор звучания трека: аудио-башня CLAP под ONNX Runtime, в процессе, без Python.
/// <para>
/// Модель и банк mel-фильтров лежат в томе хранилища и в git не входят: их выгружает туда
/// одноразовый сервис <c>clap-model</c> из docker-compose.yml. Модель обязательна, как ffmpeg:
/// без неё <see cref="EnsureLoaded"/> бросает, и хост не поднимается. Тихая деградация до
/// одних метаданных выглядела бы как «рекомендации почему-то хуже», а не как поломка.
/// </para>
/// </summary>
public sealed class ClapAudioEmbedder : IAudioEmbedder, IDisposable
{
    private const string InputName = "input_features";

    /// <summary>Where the <c>clap-model</c> service puts the graph, relative to the storage root.</summary>
    public const string ModelPath = "models/clap/audio.onnx";

    /// <summary>The mel filter bank exported beside the graph.</summary>
    /// <remarks>
    /// Шкала Slaney не воспроизводится в коде намеренно: её ручная транскрипция была бы самым
    /// вероятным источником тихого расхождения.
    /// </remarks>
    public const string MelFiltersPath = "models/clap/mel_filters_64x513.f32";

    /// <summary>The checkpoint the graph was exported from, stored as TrackEmbedding.ModelId.</summary>
    /// <remarks>
    /// Вместе со стратегией нарезки это версия алгоритма: смена любого из двух заставляет
    /// переэмбеддить всю библиотеку, а это часы или сутки CPU.
    /// </remarks>
    public const string CheckpointId = "laion/larger_clap_music_and_speech";

    public const int VectorDimension = 512;

    /// <summary>Четверть ядер внутри ORT, чтобы стриминг не голодал.</summary>
    private static int IntraOpThreads => Math.Max(1, Environment.ProcessorCount / 4);

    private readonly IMusicStorage _storage;
    private readonly ILogger<ClapAudioEmbedder> _logger;
    private readonly Lazy<Model> _model;

    public ClapAudioEmbedder(IMusicStorage storage, ILogger<ClapAudioEmbedder> logger)
    {
        _storage = storage;
        _logger = logger;
        _model = new Lazy<Model>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public void EnsureLoaded() => _ = _model.Value;

    public string ModelId => CheckpointId;

    public string Strategy => ClapWindowPlanner.Strategy;

    public int Dimension => VectorDimension;

    public async Task<AudioEmbedding?> EmbedAsync(
        string sourceAbsolutePath,
        double durationSeconds,
        CancellationToken ct = default)
    {
        var model = _model.Value;

        var offsets = ClapWindowPlanner.Plan(durationSeconds);
        if (offsets.Count == 0)
            return null;

        var windows = new List<float[]>(offsets.Count);

        foreach (var offset in offsets)
        {
            var samples = await DecodeWindowAsync(sourceAbsolutePath, offset, ct);
            if (samples is null)
                return null;

            windows.Add(samples);
        }

        return EmbedWindows(windows);
    }

    /// <summary>
    /// Вектор по уже декодированным окнам. Отдельно от <see cref="EmbedAsync"/>, чтобы тест
    /// паритета мог сверить результат с питоновским эталоном, не поднимая ffmpeg.
    /// </summary>
    public AudioEmbedding? EmbedWindows(IReadOnlyList<float[]> windows)
    {
        if (windows.Count == 0)
            return null;

        var model = _model.Value;

        var mels = new List<float[]>(windows.Count);
        foreach (var window in windows)
            mels.Add(ClapMelSpectrogram.Compute(window, model.MelFilters));

        // Все окна одним прогоном: батч из трёх дешевле трёх прогонов примерно на четверть.
        var pooled = Infer(model, mels);
        VectorMath.NormalizeInPlace(pooled);

        return new AudioEmbedding(pooled, mels.Count);
    }

    private float[] Infer(Model model, List<float[]> mels)
    {
        var batch = mels.Count;
        var stride = ClapMelSpectrogram.Frames * ClapMelSpectrogram.MelBands;
        var flat = new float[batch * stride];

        for (var index = 0; index < batch; index++)
            mels[index].CopyTo(flat, index * stride);

        var input = new DenseTensor<float>(
            flat, [batch, 1, ClapMelSpectrogram.Frames, ClapMelSpectrogram.MelBands]);

        using var results = model.Session.Run(
            [NamedOnnxValue.CreateFromTensor(InputName, input)]);

        var output = results[0].AsTensor<float>();
        var dimension = output.Dimensions[^1];
        var pooled = new float[dimension];

        // Модель уже отдаёт нормированные строки; усредняем и нормируем ещё раз.
        for (var row = 0; row < batch; row++)
        {
            for (var i = 0; i < dimension; i++)
                pooled[i] += output[row, i];
        }

        for (var i = 0; i < dimension; i++)
            pooled[i] /= batch;

        return pooled;
    }

    /// <summary>Десять секунд с заданного места: моно, 48 кГц, float32.</summary>
    private async Task<float[]?> DecodeWindowAsync(
        string sourceAbsolutePath, double offsetSeconds, CancellationToken ct)
    {
        var startInfo = FfmpegProcess.CreateStartInfo(
            FfmpegProcess.Executable,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error",
                // -ss до -i: ffmpeg перематывает по индексу, а не декодирует хвост впустую.
                "-ss", offsetSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                "-i", sourceAbsolutePath,
                "-t", ClapWindowPlanner.WindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-vn", "-map_metadata", "-1",
                "-ac", "1", "-ar", ClapMelSpectrogram.SampleRate.ToString(),
                "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1",
            ]);

        using var process = Process.Start(startInfo);
        if (process is null)
            return null;

        using var pcm = new MemoryStream(ClapMelSpectrogram.WindowSamples * sizeof(float));
        var error = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(pcm, ct);
            await process.WaitForExitAsync(ct);
            await error;
        }
        catch (OperationCanceledException)
        {
            FfmpegProcess.TryKill(process);
            throw;
        }

        if (process.ExitCode != 0 || pcm.Length < sizeof(float))
        {
            _logger.LogWarning(
                "CLAP decode failed for {Source} at {Offset}s: ffmpeg exited with {ExitCode} ({Error})",
                sourceAbsolutePath,
                offsetSeconds,
                process.ExitCode,
                error.Result.Trim());

            return null;
        }

        var bytes = pcm.ToArray();
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));

        return samples;
    }

    private Model Load()
    {
        // ResolveExisting отдаёт абсолютный путь только если файл на месте, и не выпускает
        // за корень хранилища.
        var modelPath = _storage.ResolveExisting(ModelPath);
        var filtersPath = _storage.ResolveExisting(MelFiltersPath);

        if (modelPath is null || filtersPath is null)
        {
            throw new InvalidOperationException(
                $"The CLAP model is required but '{ModelPath}' or '{MelFiltersPath}' "
                + "is missing from the storage root. Export it with `make model` "
                + "(or `docker compose up clap-model`).");
        }

        var filters = ReadFloats(filtersPath);
        var expected = ClapMelSpectrogram.FrequencyBins * ClapMelSpectrogram.MelBands;

        if (filters.Length != expected)
        {
            throw new InvalidOperationException(
                $"The CLAP mel filter bank at '{filtersPath}' holds {filters.Length} floats, expected {expected}. "
                + "Delete the model directory and export it again.");
        }

        var sessionOptions = new SessionOptions
        {
            IntraOpNumThreads = IntraOpThreads,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };

        InferenceSession session;

        try
        {
            session = new InferenceSession(modelPath, sessionOptions);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Could not load the CLAP model from '{modelPath}'. Delete the model directory and export it again.",
                exception);
        }

        _logger.LogInformation(
            "CLAP model loaded from {Path} ({Threads} intra-op threads)",
            modelPath,
            IntraOpThreads);

        return new Model(session, filters);
    }

    private static float[] ReadFloats(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));

        return values;
    }

    public void Dispose()
    {
        if (_model.IsValueCreated)
            _model.Value.Session.Dispose();
    }

    private sealed record Model(InferenceSession Session, float[] MelFilters);
}
