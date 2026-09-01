// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace MusicStreaming.Infrastructure.Imaging;

public class ImageSharpImageProcessor(ILogger<ImageSharpImageProcessor> logger) : IImageProcessor
{
    private const long MaxPixels = 12_000_000;

    private const int WebpQuality = 82;

    public async Task<IReadOnlyList<ResizedImage>> ToSquareWebpSetAsync(
        Stream source, IReadOnlyList<int> edges, CancellationToken cancellationToken = default)
    {
        if (edges.Count == 0)
            throw new ArgumentException("At least one edge length is required.", nameof(edges));

        try
        {
            var info = await Image.IdentifyAsync(source, cancellationToken);
            if ((long)info.Width * info.Height > MaxPixels)
                throw new ValidationException("That image has too many pixels to process.");

            source.Position = 0;

            using var image = await Image.LoadAsync(
                new DecoderOptions { MaxFrames = 1 }, source, cancellationToken);

            image.Mutate(context => context.AutoOrient());

            var wanted = FittingEdges(edges, Math.Min(info.Width, info.Height));
            var rendered = new List<ResizedImage>(wanted.Count);

            foreach (var edge in wanted)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(edge, edge),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center,
                    Sampler = KnownResamplers.Lanczos3,
                }));

                using var output = new MemoryStream();
                await image.SaveAsWebpAsync(
                    output, new WebpEncoder { Quality = WebpQuality }, cancellationToken);

                rendered.Add(new ResizedImage(edge, output.ToArray()));
            }

            return rendered;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            logger.LogInformation(ex, "Rejected an upload that could not be decoded as an image");
            throw new ValidationException("That file could not be read as an image.");
        }
    }

    /// <summary>
    /// Ступени, которые источник действительно может дать, по убыванию.
    /// </summary>
    /// <remarks>
    /// Апскейла здесь нет намеренно. <see cref="ResizeMode.Crop"/> растягивает до точного размера,
    /// то есть вшитая обложка в 500px честно превратилась бы в 1024: файл втрое тяжелее, деталей
    /// столько же, а Lanczos по JPEG-артефактам ещё и добавит звон по контурам. Такой рендишен
    /// хуже отсутствующего — отдать вместо него меньший умеет <c>CoverVariants.Ladder</c>.
    ///
    /// Если источник мельче всех ступеней, остаётся самая мелкая: пустой список означал бы
    /// обложку, которой нет вовсе.
    /// </remarks>
    private static IReadOnlyList<int> FittingEdges(IReadOnlyList<int> edges, int sourceEdge)
    {
        var descending = edges.Distinct().OrderByDescending(edge => edge).ToList();
        var fitting = descending.Where(edge => edge <= sourceEdge).ToList();

        return fitting.Count > 0 ? fitting : [descending[^1]];
    }
}
