// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>Serializes on-demand shelf generation per listener.</summary>
/// <remarks>
/// Сборка на лету случается только на холодном кэше, но случается сразу у всех запросов, которые
/// в него попали, — и каждый из них стоит полного прохода генерации. Пропуск нужен
/// процессу целиком, а <see cref="RecommendationService"/> живёт один запрос, поэтому раньше
/// словарь был статическим полем. Статика заодно делила пропуска между хостами интеграционных
/// тестов; здесь то же поведение, но в границах контейнера и на виду у DI.
/// </remarks>
public sealed class InlineBuildGate : IDisposable
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public SemaphoreSlim For(Guid userId) => _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
            gate.Dispose();

        _gates.Clear();
    }
}
