// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Abstractions;

/// <summary>
/// One context per concurrent query.
/// </summary>
/// <remarks>
/// <see cref="IApplicationDbContext"/> живёт в области запроса и не потокобезопасен, поэтому
/// пачку независимых выборок нельзя просто обернуть в <c>Task.WhenAll</c>. Сводка главной
/// страницы состояла из шести таких выборок подряд, и на канале с высоким пингом это шесть
/// последовательных ожиданий перед выдачей HTML.
/// </remarks>
public interface IApplicationDbContextFactory
{
    /// <summary>A fresh context; the caller owns it and must dispose it.</summary>
    IApplicationDbContext Create();
}
