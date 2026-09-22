// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Options;

public class AudioAnalysisOptions
{
    public const string SectionName = "AudioAnalysis";

    /// <summary>
    /// Единственная настройка анализа. Всё остальное — частота дискретизации, длина окна, размер
    /// пачки, пауза — это части алгоритма: их правка требует переанализа библиотеки, поэтому они
    /// живут константами рядом с кодом, который их читает.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
