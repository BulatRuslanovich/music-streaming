// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

public enum SuppressionTarget
{
    Track = 0,
    Artist = 1,
}

/// <summary>
/// An explicit "not interested".
/// </summary>
/// <remarks>
/// Неявный дизлайк выводится из пропусков и потому всегда спорен: человеку нужен способ сказать
/// это прямо, а рекомендациям — причина, которая не спорит с историей.
/// </remarks>
public class RecommendationSuppression
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public SuppressionTarget Target { get; set; }
    public Guid TargetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the suppression expires; null means never.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
