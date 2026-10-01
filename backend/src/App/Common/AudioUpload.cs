// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace App.Common;

public readonly record struct AudioFormat(string Extension, string MimeType, string MetadataMimeType)
{
    public string Label => Extension[1..].ToUpperInvariant();
}

public static class AudioUpload
{
    private static readonly Dictionary<string, AudioFormat> ByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"] = new AudioFormat(".mp3", "audio/mpeg", "taglib/mp3"),
            [".flac"] = new AudioFormat(".flac", "audio/flac", "taglib/flac"),
            [".m4a"] = new AudioFormat(".m4a", "audio/mp4", "taglib/m4a"),
        };

    public static readonly string Accepted = string.Join(", ", ByExtension.Keys);

    public static AudioFormat? For(string fileName) =>
        ByExtension.TryGetValue(Path.GetExtension(fileName), out var format) ? format : null;

    public static bool IsLossless(string? codec) => codec is "flac" or "alac";

    public static string? SniffContainer(string absolutePath)
    {
        Span<byte> head = stackalloc byte[8];

        using (var file = File.OpenRead(absolutePath))
        {
            if (file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length)
                return null;
        }

        if (head[..4].SequenceEqual("fLaC"u8))
            return ".flac";

        return head[4..8].SequenceEqual("ftyp"u8) ? ".m4a" : null;
    }
}
