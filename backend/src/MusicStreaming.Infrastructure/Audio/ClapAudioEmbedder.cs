// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations.Embeddings;

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>
/// Вектор звучания трека: аудио-башня CLAP под ONNX Runtime, в процессе, без Python.
/// <para>
/// Модель и банк mel-фильтров лежат в томе хранилища и в git не входят — это около 600 МБ,
/// нужных только продакшен-контейнеру. Когда их нет, <see cref="IsAvailable"/> равно false,
/// и весь путь эмбеддингов деградирует ровно так же, как при пустой библиотеке: это та же
/// ветка, что у нового инстанса, поэтому отдельных условий выше по стеку не появляется.
/// </para>
/// </summary>
public sealed class ClapAudioEmbedder : IAudioEmbedder, IDisposable
{
    private const string InputName = "input_features";

    private readonly AudioEmbeddingOptions _options;
    private readonly TranscodeOptions _transcode;
    private readonly IMusicStorage _storage;
    private readonly ILogger<ClapAudioEmbedder> _logger;
    private readonly Lazy<Model?> _model;

    public ClapAudioEmbedder(
        IOptions<AudioEmbeddingOptions> options,
        IOptions<TranscodeOptions> transcode,
        IMusicStorage storage,
        ILogger<ClapAudioEmbedder> logger)
    {
        _options = options.Value;
        _transcode = transcode.Value;
        _storage = storage;
        _logger = logger;
        _model = new Lazy<Model?>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => _options.Enabled && _model.Value is not null;

    public string ModelId => _options.ModelId;

    public string Strategy => ClapWindowPlanner.Strategy;

    public int Dimension => _options.Dimension;

    public async Task<AudioEmbedding?> EmbedAsync(
        string sourceAbsolutePath,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        if (_model.Value is not { } model)
            return null;

        var offsets = ClapWindowPlanner.Plan(durationSeconds);
        if (offsets.Count == 0)
            return null;

        var windows = new List<float[]>(offsets.Count);

        foreach (var offset in offsets)
        {
            var samples = await DecodeWindowAsync(sourceAbsolutePath, offset, cancellationToken);
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
        if (_model.Value is not { } model || windows.Count == 0)
            return null;

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
            _transcode.FfmpegPath,
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

    private Model? Load()
    {
        if (!_options.Enabled)
            return null;

        // ResolveExisting отдаёт абсолютный путь только если файл на месте, и не выпускает
        // за корень хранилища.
        var modelPath = _storage.ResolveExisting(_options.ModelPath);
        var filtersPath = _storage.ResolveExisting(_options.MelFiltersPath);

        if (modelPath is null || filtersPath is null)
        {
            // Information, а не Warning: отсутствие модели — рабочее состояние свежей установки,
            // а не поломка. Рекомендации работают и без звуковой стороны.
            _logger.LogInformation(
                "CLAP model is not installed ({Model}, {Filters}); sonic similarity stays off",
                _options.ModelPath,
                _options.MelFiltersPath);

            return null;
        }

        if (!VerifyDigest(modelPath))
            return null;

        var filters = ReadFloats(filtersPath);
        var expected = ClapMelSpectrogram.FrequencyBins * ClapMelSpectrogram.MelBands;

        if (filters.Length != expected)
        {
            _logger.LogError(
                "CLAP mel filter bank at {Path} holds {Actual} floats, expected {Expected}",
                filtersPath,
                filters.Length,
                expected);

            return null;
        }

        try
        {
            var sessionOptions = new SessionOptions
            {
                IntraOpNumThreads = _options.EffectiveIntraOpThreads,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };

            var session = new InferenceSession(modelPath, sessionOptions);

            _logger.LogInformation(
                "CLAP model loaded from {Path} ({Threads} intra-op threads)",
                modelPath,
                _options.EffectiveIntraOpThreads);

            return new Model(session, filters);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load the CLAP model from {Path}", modelPath);
            return null;
        }
    }

    /// <summary>
    /// Проверка SHA-256, когда он задан. Модель — исполняемый граф: брать её без сверки
    /// отпечатка значит запускать в контейнере то, чего никто не проверял.
    /// </summary>
    private bool VerifyDigest(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(_options.ModelSha256))
            return true;

        using var stream = File.OpenRead(modelPath);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));

        if (actual.Equals(_options.ModelSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        _logger.LogError(
            "CLAP model at {Path} has digest {Actual}, expected {Expected}; refusing to load it",
            modelPath,
            actual,
            _options.ModelSha256);

        return false;
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
            _model.Value?.Session.Dispose();
    }

    private sealed record Model(InferenceSession Session, float[] MelFilters);
}
