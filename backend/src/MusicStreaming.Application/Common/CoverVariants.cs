// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

public enum CoverSize
{
    Full,
    Thumb,
    Large,
}

public static class CoverVariants
{
    /// <summary>
    /// Обложка во весь экран. Полноэкранный плеер отводит арту до 460 логических пикселей,
    /// то есть 920 физических на экране с двойной плотностью — из файла в 640 это заметное мыло
    /// ровно там, где обложка и есть весь интерфейс.
    /// </summary>
    public const int LargeEdge = 1024;

    public const int FullEdge = 640;

    public const int ThumbEdge = 256;

    public static readonly IReadOnlyList<int> Edges = [LargeEdge, FullEdge, ThumbEdge];

    /// <summary>
    /// Ступени вниз от запрошенной. Крупный рендишен есть не у всякой обложки: у мелкого
    /// источника его не из чего сделать, и апскейл здесь запрещён. Поэтому запрос отдаёт
    /// следующий существующий размер, а не 404.
    /// </summary>
    public static IReadOnlyList<CoverSize> Ladder(CoverSize size) => size switch
    {
        CoverSize.Large => [CoverSize.Large, CoverSize.Full, CoverSize.Thumb],
        CoverSize.Thumb => [CoverSize.Thumb, CoverSize.Full],
        _ => [CoverSize.Full, CoverSize.Thumb],
    };
}
