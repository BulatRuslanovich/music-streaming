// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Recommendations;

/// <summary>Every fixed number the recommendation pipeline runs on, grouped by who reads it.</summary>
/// <remarks>
/// Раньше это были опции с биндингом и валидацией на старте, но менять их из конфигурации не
/// нужно никому: значения подбирает <c>make eval</c>, а не оператор, и правка веса — это правка
/// кода с прогоном оценки, а не строчка в <c>.env</c>. Поэтому константы. Настраиваемым остался
/// только выключатель <see cref="Options.RecommendationOptions.Enabled"/>.
/// <para>
/// Группы — не таксономия, а потребители: <see cref="Diversity"/> читает только MMR-отбор,
/// <see cref="Exploration"/> — только деление на ближнее и дальнее, <see cref="Penalties"/> —
/// только скоринг кандидата.
/// </para>
/// </remarks>
public static partial class RecommendationTuning;
