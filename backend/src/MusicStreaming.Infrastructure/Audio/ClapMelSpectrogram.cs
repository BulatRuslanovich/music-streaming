// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Numerics;

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>
/// Лог-мел спектрограмма ровно в том виде, в каком её ждёт аудио-башня CLAP.
/// <para>
/// Это перенос ветки <c>rand_trunc</c> из <c>ClapFeatureExtractor</c>, сверенный с исходниками
/// transformers. Каждая константа здесь несёт вес: ошибка в любой даёт вектор, который выглядит
/// правдоподобно и при этом ничего не значит. Поэтому рядом живёт тест паритета против эталона,
/// посчитанного питоновской реализацией.
/// </para>
/// <para>
/// Банк mel-фильтров сюда <b>передаётся</b>, а не вычисляется: шкала Slaney — кусочно
/// линейно-логарифмическая функция с магическими константами, и её ручная транскрипция была бы
/// самым вероятным источником расхождения. Матрицу выгружает скрипт экспорта.
/// </para>
/// </summary>
public static class ClapMelSpectrogram
{
    public const int SampleRate = 48_000;
    public const int FrameLength = 1024;
    public const int HopLength = 480;
    public const int MelBands = 64;

    /// <summary>Бинов у rfft длины 1024.</summary>
    public const int FrequencyBins = FrameLength / 2 + 1;

    /// <summary>Модель принимает ровно 10 секунд.</summary>
    public const int WindowSamples = SampleRate * 10;

    /// <summary>1 + (480000 + 1024 − 1024) / 480.</summary>
    public const int Frames = WindowSamples / HopLength + 1;

    /// <summary>Пол перед логарифмом; тот же и в mel_floor, и в power_to_db.</summary>
    private const double Floor = 1e-10;

    /// <summary>
    /// Периодическое окно Ханна: <c>0.5 − 0.5·cos(2πi/N)</c>.
    /// <para>
    /// Именно периодическое, не симметричное. В <see cref="AudioFeatureExtraction"/> рядом лежит
    /// симметричное окно (<c>2πi/(N−1)</c>) — оно отличается на 2.4e-3 и здесь неверно.
    /// Переиспользовать его нельзя.
    /// </para>
    /// </summary>
    private static readonly double[] Window = BuildWindow();

    private static double[] BuildWindow()
    {
        var window = new double[FrameLength];
        for (var i = 0; i < FrameLength; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / FrameLength);

        return window;
    }

    /// <summary>
    /// Считает (<see cref="Frames"/> × <see cref="MelBands"/>) в децибелах, в порядке строк.
    /// </summary>
    /// <param name="samples">Моно 48 кГц. Короче окна — дополняется как в repeatpad, длиннее — режется.</param>
    /// <param name="melFilters">Банк (<see cref="FrequencyBins"/> × <see cref="MelBands"/>) в порядке строк.</param>
    public static float[] Compute(ReadOnlySpan<float> samples, ReadOnlySpan<float> melFilters)
    {
        if (melFilters.Length != FrequencyBins * MelBands)
        {
            throw new ArgumentException(
                $"Mel filter bank must hold {FrequencyBins} x {MelBands} floats.", nameof(melFilters));
        }

        var waveform = FitToWindow(samples);

        // center=True, pad_mode="reflect": по половине кадра с каждой стороны.
        var padded = ReflectPad(waveform, FrameLength / 2);

        var power = new double[Frames * FrequencyBins];
        var buffer = new Complex[FrameLength];

        for (var frame = 0; frame < Frames; frame++)
        {
            var start = frame * HopLength;

            for (var i = 0; i < FrameLength; i++)
                buffer[i] = new Complex(padded[start + i] * Window[i], 0.0);

            Fft.Transform(buffer);

            var offset = frame * FrequencyBins;
            for (var bin = 0; bin < FrequencyBins; bin++)
            {
                // transformers кладёт результат rfft в complex64, то есть округляет до float32
                // до возведения в квадрат. Повторяем, иначе расхождение видно в тихих полосах.
                var real = (float)buffer[bin].Real;
                var imaginary = (float)buffer[bin].Imaginary;
                var magnitude = Math.Sqrt((double)real * real + (double)imaginary * imaginary);

                power[offset + bin] = magnitude * magnitude;
            }
        }

        return ToDecibels(power, melFilters);
    }

    /// <summary>
    /// padding="repeatpad": целое число повторов, затем нули. Повторов именно целое —
    /// шестисекундный отрывок получает n = 1, то есть не повторяется вовсе, и остаток
    /// добивается тишиной.
    /// </summary>
    private static float[] FitToWindow(ReadOnlySpan<float> samples)
    {
        var window = new float[WindowSamples];

        if (samples.Length >= WindowSamples)
        {
            samples[..WindowSamples].CopyTo(window);
            return window;
        }

        if (samples.Length == 0)
            return window;

        var repeats = WindowSamples / samples.Length;
        for (var repeat = 0; repeat < repeats; repeat++)
            samples.CopyTo(window.AsSpan(repeat * samples.Length));

        return window;
    }

    /// <summary>Отражение без повтора крайнего отсчёта — семантика numpy "reflect".</summary>
    private static float[] ReflectPad(ReadOnlySpan<float> waveform, int pad)
    {
        var padded = new float[waveform.Length + pad * 2];
        waveform.CopyTo(padded.AsSpan(pad));

        for (var i = 0; i < pad; i++)
        {
            padded[pad - 1 - i] = waveform[Math.Min(i + 1, waveform.Length - 1)];
            padded[pad + waveform.Length + i] = waveform[Math.Max(waveform.Length - 2 - i, 0)];
        }

        return padded;
    }

    /// <summary>Свёртка с банком фильтров и перевод в децибелы: 10·log10, без верхней отсечки.</summary>
    private static float[] ToDecibels(double[] power, ReadOnlySpan<float> melFilters)
    {
        var result = new float[Frames * MelBands];

        for (var frame = 0; frame < Frames; frame++)
        {
            var powerOffset = frame * FrequencyBins;
            var melOffset = frame * MelBands;

            for (var band = 0; band < MelBands; band++)
            {
                var sum = 0.0;
                for (var bin = 0; bin < FrequencyBins; bin++)
                    sum += melFilters[bin * MelBands + band] * power[powerOffset + bin];

                // top_db не задан, поэтому верхнего клампа нет — только пол.
                result[melOffset + band] = (float)(10.0 * Math.Log10(Math.Max(Floor, sum)));
            }
        }

        return result;
    }
}
