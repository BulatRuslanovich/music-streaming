// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Common;
using Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace UnitTests.Imaging;

public class ImageDecodingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MemoryStream Encode(Action<Image, Stream> save)
    {
        using var image = new Image<Rgba32>(300, 300, new Rgba32(200, 80, 40));
        var stream = new MemoryStream();
        save(image, stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task A_png_becomes_a_set_of_webp_covers()
    {
        using var source = Encode((image, stream) => image.SaveAsPng(stream));

        var renditions = await new ImageSharpImageProcessor().ToSquareWebpSetAsync(source, CoverVariants.Edges, Ct);

        Assert.NotEmpty(renditions);
        Assert.All(renditions, rendition => Assert.Equal("RIFF"u8.ToArray(), rendition.Content[..4]));
    }

    // TIFF — формат с уязвимыми декодерами в ImageSharp 3.1.x; обложки им не бывают.
    [Fact]
    public async Task A_tiff_is_rejected_whatever_it_is_called()
    {
        using var source = Encode((image, stream) => image.SaveAsTiff(stream));

        await Assert.ThrowsAsync<ValidationException>(() =>
            new ImageSharpImageProcessor().ToSquareWebpSetAsync(source, CoverVariants.Edges, Ct));
    }
}
