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
        try
        {
            var info = await Image.IdentifyAsync(source, cancellationToken);
            if ((long)info.Width * info.Height > MaxPixels)
                throw new ValidationException("That image has too many pixels to process.");

            source.Position = 0;

            using var image = await Image.LoadAsync(
                new DecoderOptions { MaxFrames = 1 }, source, cancellationToken);

            image.Mutate(context => context
                .AutoOrient()
                .Resize(new ResizeOptions
                {
                    Size = new Size(edge, edge),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center,
                    Sampler = KnownResamplers.Lanczos3,
                }));

            using var output = new MemoryStream();
            await image.SaveAsWebpAsync(
                output, new WebpEncoder { Quality = WebpQuality }, cancellationToken);

            return output.ToArray();
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            logger.LogInformation(ex, "Rejected an upload that could not be decoded as an image");
            throw new ValidationException("That file could not be read as an image.");
        }
    }
}
