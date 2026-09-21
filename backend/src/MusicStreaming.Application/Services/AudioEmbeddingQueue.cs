// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Services;

/// <summary>
/// Очередь на расчёт вектора звучания. Отдельно от <see cref="AudioAnalysisQueue"/>: там проход
/// занимает доли секунды, здесь — секунды, и смешивать их означало бы, что дешёвый анализ ждёт
/// за дорогим.
/// </summary>
public class AudioEmbeddingQueue : TrackWorkQueue;
