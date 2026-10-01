// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging;
using App.Abstractions;
using App.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Infrastructure.Imaging;

public class ImageSharpImageProcessor(ILogger<ImageSharpImageProcessor> logger) : IImageProcessor
{
    private const long MaxPixels = 12_000_000;

    private const int WebpQuality = 82;

    public async Task<IReadOnlyList<ResizedImage>> ToSquareWebpSetAsync(
        Stream source, IReadOnlyList<int> edges, CancellationToken ct = default)
    {
        if (edges.Count == 0)
            throw new ArgumentException("At least one edge length is required.", nameof(edges));

        try
        {
            var info = await Image.IdentifyAsync(source, ct);
            if ((long)info.Width * info.Height > MaxPixels)
                throw new ValidationException("That image has too many pixels to process.");

            source.Position = 0;

            using var image = await Image.LoadAsync(
                new DecoderOptions { MaxFrames = 1 }, source, ct);

            image.Mutate(context => context.AutoOrient());

            var sourceEdge = Math.Min(info.Width, info.Height);
            var descending = edges.Distinct().OrderByDescending(edge => edge).ToList();
            List<int> wanted = [.. descending.Where(edge => edge <= sourceEdge)];
            if (wanted.Count == 0)
                wanted.Add(descending[^1]);
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
                    output, new WebpEncoder { Quality = WebpQuality }, ct);

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
}
