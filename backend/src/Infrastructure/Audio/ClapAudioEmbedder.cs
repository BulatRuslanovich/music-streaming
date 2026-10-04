// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Storage;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using App.Recommendations.Embeddings;

namespace Infrastructure.Audio;

public sealed class ClapAudioEmbedder : IDisposable
{
    private const string InputName = "input_features";

    public const string ModelPath = "models/clap/audio.onnx";

    public const string MelFiltersPath = "models/clap/mel_filters_64x513.f32";

    public const string CheckpointId = "laion/larger_clap_music_and_speech";

    public const int VectorDimension = 512;

    private static int IntraOpThreads => Math.Max(1, Environment.ProcessorCount / 4);

    private readonly FileSystemMusicStorage _storage;
    private readonly ILogger<ClapAudioEmbedder> _logger;
    private readonly Lazy<Model> _model;

    public ClapAudioEmbedder(FileSystemMusicStorage storage, ILogger<ClapAudioEmbedder> logger)
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

    private AudioEmbedding? EmbedWindows(IReadOnlyList<float[]> windows)
    {
        if (windows.Count == 0)
            return null;

        var model = _model.Value;

        var mels = new List<float[]>(windows.Count);
        mels.AddRange(windows.Select(window => ClapMelSpectrogram.Compute(window, model.MelFilters)));

        var batch = mels.Count;
        const int stride = ClapMelSpectrogram.Frames * ClapMelSpectrogram.MelBands;
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

        for (var row = 0; row < batch; row++)
        {
            for (var i = 0; i < dimension; i++)
                pooled[i] += output[row, i];
        }

        for (var i = 0; i < dimension; i++)
            pooled[i] /= batch;

        VectorMath.NormalizeInPlace(pooled);

        return new AudioEmbedding(pooled, mels.Count);
    }

    private async Task<float[]?> DecodeWindowAsync(
        string sourceAbsolutePath, double offsetSeconds, CancellationToken ct)
    {
        var startInfo = FfmpegProcess.CreateStartInfo(
            FfmpegProcess.Executable,
            [
                "-nostdin", "-hide_banner", "-loglevel", "error",
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
        var modelPath = _storage.ResolveExisting(ModelPath);
        var filtersPath = _storage.ResolveExisting(MelFiltersPath);

        if (modelPath is null || filtersPath is null)
        {
            throw new InvalidOperationException(
                $"The CLAP model is required but '{ModelPath}' or '{MelFiltersPath}' "
                + "is missing from the storage root. Export it with `make model` "
                + "(or `docker compose up clap-model`).");
        }

        var filters = MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(filtersPath)).ToArray();
        const int expected = ClapMelSpectrogram.FrequencyBins * ClapMelSpectrogram.MelBands;

        if (filters.Length != expected)
        {
            throw new InvalidOperationException(
                $"The CLAP mel filter bank at '{filtersPath}' holds {filters.Length} floats, expected {expected}. "
                + "Delete the model directory and export it again.");
        }

        var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
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

    public void Dispose()
    {
        if (_model.IsValueCreated)
            _model.Value.Session.Dispose();
    }

    private sealed record Model(InferenceSession Session, float[] MelFilters);
}

public record AudioEmbedding(float[] Vector, int Windows);
