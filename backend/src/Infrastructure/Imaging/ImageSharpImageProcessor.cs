// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Infrastructure.Imaging;

public class ImageSharpImageProcessor
{
    private const long MaxPixels = 12_000_000;

    private const int WebpQuality = 82;

    // Формат ImageSharp определяет по содержимому, а обложки приходят и из тегов аудио, и с Deezer,
    // мимо проверки расширения. Остальные декодеры (TIFF, BMP, GIF…) не нужны и только расширяют
    // поверхность атаки: уязвимости 3.1.x исправлены лишь в 4.x, которой нужен лицензионный ключ.
    private static readonly DecoderOptions Decoding = new()
    {
        Configuration = new Configuration(
            new JpegConfigurationModule(), new PngConfigurationModule(), new WebpConfigurationModule()),
        MaxFrames = 1,
    };

    public async Task<IReadOnlyList<ResizedImage>> ToSquareWebpSetAsync(
        Stream source, IReadOnlyList<int> edges, CancellationToken ct = default)
    {
        if (edges.Count == 0)
            throw new ArgumentException("At least one edge length is required.", nameof(edges));

        try
        {
            var info = await Image.IdentifyAsync(Decoding, source, ct);
            if ((long)info.Width * info.Height > MaxPixels)
                throw new ValidationException("That image has too many pixels to process.");

            source.Position = 0;

            using var image = await Image.LoadAsync(Decoding, source, ct);

            image.Mutate(context => context.AutoOrient());

            var plan = PlanRenditions(edges, Math.Min(info.Width, info.Height));
            var rendered = new List<ResizedImage>(plan.Count);

            foreach (var (edge, target) in plan)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(target, target),
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
            throw new ValidationException("That file could not be read as an image.");
        }
    }

    // Каждый запрошенный размер, но не крупнее исходника: обложка 600 px становится «полной» как есть,
    // а не ужимается до превью 256. Совпавшие по факту размеры не дублируются — остаётся меньший
    // запрошенный, а за отсутствующий крупный отдаётся он же. Порядок — от крупного к мелкому.
    public static IReadOnlyList<(int Edge, int Target)> PlanRenditions(IReadOnlyList<int> edges, int sourceEdge)
    {
        var plan = new List<(int Edge, int Target)>();

        foreach (var edge in edges.Distinct().Order())
        {
            var target = Math.Min(edge, sourceEdge);
            if (plan.Count == 0 || plan[^1].Target < target)
                plan.Add((edge, target));
        }

        plan.Reverse();
        return plan;
    }
}

// Edge — запрошенный вариант (1024 / 640 / 256); картинка внутри бывает меньше, если исходник мельче.
public record ResizedImage(int Edge, byte[] Content);
