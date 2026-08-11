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
    private const long MaxPixels = 50_000_000;

    private const int WebpQuality = 82;

    public async Task<byte[]> ToSquareWebpAsync(
        Stream source, int edge, CancellationToken cancellationToken = default)
    {
        var rendered = await ToSquareWebpSetAsync(source, [edge], cancellationToken);
        return rendered[0].Content;
    }

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

            var rendered = new List<ResizedImage>(edges.Count);

            foreach (var edge in edges.OrderByDescending(edge => edge))
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
}
