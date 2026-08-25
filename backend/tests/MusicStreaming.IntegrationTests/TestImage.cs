// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MusicStreaming.IntegrationTests;

/// <summary>A real, decodable image for the endpoints that accept cover art.</summary>
internal static class TestImage
{
    public static byte[] Png(int width = 300, int height = 300)
    {
        using var image = new Image<Rgba32>(width, height);

        image.Mutate(context => context.BackgroundColor(Color.RebeccaPurple));

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());

        return buffer.ToArray();
    }
}
