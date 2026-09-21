// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;
using MusicStreaming.Infrastructure.Audio;
using Xunit;

namespace MusicStreaming.UnitTests.Recommendations;

/// <summary>
/// Сверка препроцессинга CLAP с эталоном, посчитанным питоновским
/// <c>ClapFeatureExtractor</c> (см. <c>backend/scripts/export_clap_audio_onnx.py</c>).
/// <para>
/// Ошибка в окне, шкале mel, отступе или логарифме не роняет ничего: она даёт правдоподобный
/// вектор, который просто не значит того, что должен, и дальше молча портит все рекомендации.
/// Поймать это можно только сравнением с эталоном.
/// </para>
/// <para>
/// Сигналы не хранятся файлами — они порождаются здесь по тем же формулам, что в скрипте
/// экспорта, а <see cref="Signal_generators_have_not_drifted_from_the_export_script"/> следит,
/// чтобы генераторы не разошлись.
/// </para>
/// </summary>
public class ClapParityTests
{
    private const int Rate = ClapMelSpectrogram.SampleRate;
    private const double Seconds = 12.0;

    private static readonly string FixtureRoot = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "clap");

    private static readonly string[] Names = ["sweep", "noise", "clicks", "chord", "quiet"];

    public static TheoryData<string> Fixtures() => [.. Names];

    /// <summary>
    /// Полосы, где есть настоящий сигнал, обязаны совпасть с эталоном вплотную.
    /// </summary>
    /// <remarks>
    /// Порог зависит от уровня намеренно. На полу тишины (ниже −80 дБ) мощность порядка 1e-10,
    /// и там расходятся последние биты: transformers кладёт результат rfft в complex64, а
    /// используемое здесь radix-2 БПФ накапливает округление иначе, чем pocketfft у numpy.
    /// На свипе, где энергия сидит в узкой движущейся полосе, а всё прочее — тишина, это даёт
    /// до 0.04 дБ примерно на проценте бинов. Модели эти бины безразличны.
    /// <para>
    /// Ослаблять порог целиком было бы неверно: структурная ошибка — симметричное окно вместо
    /// периодического, шкала HTK вместо Slaney, забытый reflect-pad — сдвигает как раз
    /// <b>громкие</b> полосы, и на них проверка остаётся жёсткой.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_mel_spectrogram_matches_the_python_reference(string name)
    {
        const double SilenceFloorDb = -80.0;
        const double SignalTolerance = 1e-2;
        const double MeanTolerance = 1e-3;

        var expected = ReadFloats(Path.Combine(FixtureRoot, $"{name}.mel.f32"));
        var window = FirstWindow(SignalFor(name));

        var actual = ClapMelSpectrogram.Compute(window, MelFilters());

        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(ClapMelSpectrogram.Frames * ClapMelSpectrogram.MelBands, actual.Length);

        var worstSignal = 0.0;
        var worstSignalAt = -1;
        var signalBins = 0;
        var total = 0.0;

        for (var i = 0; i < expected.Length; i++)
        {
            var delta = Math.Abs(expected[i] - actual[i]);
            total += delta;

            if (expected[i] < SilenceFloorDb)
                continue;

            signalBins++;

            if (delta > worstSignal)
            {
                worstSignal = delta;
                worstSignalAt = i;
            }
        }

        // Если сигнальных полос почти нет, проверять нечего и тест был бы пустым.
        Assert.True(
            signalBins > expected.Length / 20,
            $"{name}: only {signalBins} of {expected.Length} bins carry signal — the fixture is too quiet to prove anything");

        Assert.True(
            worstSignal < SignalTolerance,
            $"{name}: worst |Δ| above {SilenceFloorDb} dB is {worstSignal:F6} dB at bin " +
            $"[{worstSignalAt / ClapMelSpectrogram.MelBands},{worstSignalAt % ClapMelSpectrogram.MelBands}]");

        var mean = total / expected.Length;
        Assert.True(mean < MeanTolerance, $"{name}: mean |Δ| = {mean:F8} dB");
    }

    [Fact]
    public void Signal_generators_have_not_drifted_from_the_export_script()
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "fixtures.json")));
        var fixtures = manifest.RootElement.GetProperty("fixtures");

        Assert.Equal(Rate, manifest.RootElement.GetProperty("sampleRate").GetInt32());
        Assert.Equal(Seconds, manifest.RootElement.GetProperty("seconds").GetDouble());

        foreach (var name in Names)
        {
            var expected = fixtures.GetProperty(name).GetProperty("sampleChecksum").GetDouble();

            var sum = 0.0;
            foreach (var sample in SignalFor(name))
                sum += sample;

            // Эталонные mel считались от питоновского сигнала; если генераторы разошлись,
            // сравнивать спектрограммы бессмысленно.
            var scale = Math.Max(1.0, Math.Abs(expected));
            Assert.True(
                Math.Abs(sum - expected) / scale < 1e-3,
                $"{name}: checksum {sum:F6} vs {expected:F6}");
        }
    }

    [Fact]
    public void The_planner_agrees_with_the_windows_the_reference_used()
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "fixtures.json")));
        var expected = manifest.RootElement
            .GetProperty("fixtures").GetProperty("sweep")
            .GetProperty("windows").EnumerateArray().Select(value => value.GetDouble()).ToArray();

        var actual = ClapWindowPlanner.Plan(Seconds);

        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], actual[i], precision: 5);
    }

    [Fact]
    public void The_mel_filter_bank_has_the_shape_the_model_expects()
    {
        Assert.Equal(ClapMelSpectrogram.FrequencyBins * ClapMelSpectrogram.MelBands, MelFilters().Length);
        Assert.Equal(513, ClapMelSpectrogram.FrequencyBins);
        Assert.Equal(1001, ClapMelSpectrogram.Frames);
    }

    /// <summary>
    /// Сквозная сверка: тот же граф, те же окна — и вектор обязан совпасть с питоновским.
    /// Пропускается, когда модели нет на месте: она весит сотни мегабайт и в git не входит.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_embedding_matches_the_python_reference(string name)
    {
        using var embedder = BuildEmbedder();
        Assert.SkipUnless(embedder.IsAvailable, "CLAP model is not installed");

        var signal = SignalFor(name);
        var windows = ClapWindowPlanner.Plan(Seconds)
            .Select(offset => Window(signal, offset))
            .ToList();

        var actual = embedder.EmbedWindows(windows);
        Assert.NotNull(actual);

        var expected = ReadFloats(Path.Combine(FixtureRoot, $"{name}.vec.f32"));
        Assert.Equal(expected.Length, actual.Vector.Length);

        var dot = 0.0;
        for (var i = 0; i < expected.Length; i++)
            dot += (double)expected[i] * actual.Vector[i];

        // Остаток расхождения — разница ядер ORT и PyTorch, порядка 1e-4 на активацию.
        // После проекции и нормировки это укладывается в тысячные доли косинусного расстояния.
        Assert.True(dot >= 0.999, $"{name}: cos = {dot:F6}");
    }

    [Fact]
    public void The_embedding_is_a_unit_vector_of_the_expected_width()
    {
        using var embedder = BuildEmbedder();
        Assert.SkipUnless(embedder.IsAvailable, "CLAP model is not installed");

        var windows = ClapWindowPlanner.Plan(Seconds)
            .Select(offset => Window(SignalFor("chord"), offset))
            .ToList();

        var embedding = embedder.EmbedWindows(windows);

        Assert.NotNull(embedding);
        Assert.Equal(512, embedding.Vector.Length);
        Assert.Equal(3, embedding.Windows);

        var norm = Math.Sqrt(embedding.Vector.Sum(value => (double)value * value));
        Assert.Equal(1.0, norm, precision: 4);
    }

    [Fact]
    public void Different_sounds_are_not_neighbours_in_the_embedding_space()
    {
        using var embedder = BuildEmbedder();
        Assert.SkipUnless(embedder.IsAvailable, "CLAP model is not installed");

        // Проверка, что вектор вообще что-то различает: если бы препроцессинг схлопывал вход,
        // все фикстуры оказались бы почти сонаправлены, а паритет с эталоном этого не заметил бы.
        var chord = ReadFloats(Path.Combine(FixtureRoot, "chord.vec.f32"));
        var noise = ReadFloats(Path.Combine(FixtureRoot, "noise.vec.f32"));

        var dot = 0.0;
        for (var i = 0; i < chord.Length; i++)
            dot += (double)chord[i] * noise[i];

        Assert.True(dot < 0.9, $"chord vs noise cosine = {dot:F4}");
    }

    private static ClapAudioEmbedder BuildEmbedder()
    {
        var options = new AudioEmbeddingOptions
        {
            ModelPath = "models/clap/audio.onnx",
            MelFiltersPath = "models/clap/mel_filters_64x513.f32",
        };

        return new ClapAudioEmbedder(
            Microsoft.Extensions.Options.Options.Create(options),
            Microsoft.Extensions.Options.Options.Create(new TranscodeOptions()),
            new RepoStorage(),
            NullLogger<ClapAudioEmbedder>.Instance);
    }

    /// <summary>Десять секунд начиная со смещения, как их вырезал бы ffmpeg.</summary>
    private static float[] Window(float[] signal, double offsetSeconds)
    {
        var start = (int)Math.Round(offsetSeconds * Rate);
        var length = Math.Min(ClapMelSpectrogram.WindowSamples, signal.Length - start);

        return signal[start..(start + length)];
    }

    /// <summary>
    /// Хранилище, указывающее на storage/ в рабочей копии: модель лежит там, а не в git.
    /// </summary>
    private sealed class RepoStorage : IMusicStorage
    {
        private static readonly string Root = FindStorageRoot();

        public string? ResolveExisting(string storageRelativePath)
        {
            var path = Path.Combine(Root, storageRelativePath);
            return File.Exists(path) ? path : null;
        }

        private static string FindStorageRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "storage");
                if (Directory.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            return Path.Combine(AppContext.BaseDirectory, "storage");
        }

        public Task<StoredFile> SaveTrackAsync(
            Stream content, string extension, long maxBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Stream? OpenRead(string storageRelativePath) => throw new NotSupportedException();

        public string ResolveForWrite(string storageRelativePath) => throw new NotSupportedException();

        public void Delete(string storageRelativePath) => throw new NotSupportedException();
    }

    /// <summary>Окно берётся так же, как это делает эмбеддер: первые десять секунд.</summary>
    private static float[] FirstWindow(float[] signal) =>
        signal[..ClapMelSpectrogram.WindowSamples];

    private static float[] MelFilters()
    {
        var path = Path.Combine(FixtureRoot, "mel_filters_64x513.f32");
        Assert.True(File.Exists(path), $"Mel filter bank missing at {path}");

        return ReadFloats(path);
    }

    private static float[] ReadFloats(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));

        return values;
    }

    /// <summary>
    /// Те же сигналы, что порождает скрипт экспорта. Каждый ломает свою часть цепочки:
    /// свип — шкалу частот, шум — общий уровень, щелчки — оконное взвешивание,
    /// аккорд — разрешение по частоте, тихий тон — пол перед логарифмом.
    /// </summary>
    private static float[] SignalFor(string name)
    {
        var n = (int)(Rate * Seconds);
        var signal = new float[n];

        switch (name)
        {
            case "sweep":
                for (var i = 0; i < n; i++)
                {
                    var t = (double)i / Rate;
                    signal[i] = (float)(Math.Sin(
                        2 * Math.PI * (80.0 * t + (6000.0 - 80.0) / (2 * Seconds) * t * t)) * 0.6);
                }

                break;

            case "noise":
                // Тот же целочисленный LCG, что в скрипте экспорта: арифметика по модулю 2^64
                // одинакова в обоих языках, в отличие от генератора numpy.
                var state = 20260921UL;
                for (var i = 0; i < n; i++)
                {
                    unchecked
                    {
                        state = state * 6364136223846793005UL + 1442695040888963407UL;
                    }

                    signal[i] = (float)(((state >> 40) / (double)(1 << 24)) * 2.0 - 1.0) * 0.2f;
                }

                break;

            case "clicks":
                for (var i = 0; i < n; i += Rate / 8)
                    signal[i] = 0.9f;

                break;

            case "chord":
                for (var i = 0; i < n; i++)
                {
                    var t = (double)i / Rate;
                    var sum = Math.Sin(2 * Math.PI * 220.0 * t)
                              + Math.Sin(2 * Math.PI * 277.18 * t)
                              + Math.Sin(2 * Math.PI * 329.63 * t);

                    signal[i] = (float)(sum / 3 * 0.7);
                }

                break;

            case "quiet":
                for (var i = 0; i < n; i++)
                    signal[i] = (float)(Math.Sin(2 * Math.PI * 440.0 * (double)i / Rate) * 1e-4);

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown fixture");
        }

        return signal;
    }
}
