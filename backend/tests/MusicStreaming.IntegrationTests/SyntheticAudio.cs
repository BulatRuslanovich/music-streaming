// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.IntegrationTests;

/// <summary>
/// Настоящий mp3 с тегами: тишина в кадрах, которые TagLib и ffprobe разбирают как реальный поток.
/// </summary>
/// <remarks>
/// Приёмник загрузки нюхает контейнер по магическим байтам и требует ненулевую длительность, так
/// что подсунуть ему четыре байта нельзя — файл должен быть настоящим.
/// </remarks>
internal static class SyntheticAudio
{
    private static readonly byte[] FrameHeader = [0xFF, 0xFB, 0x90, 0x00];

    private const int FrameLength = 417;
    private const int FrameCount = 120;

    public static byte[] Mp3(string title, string artist, string? album = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"caimack-synthetic-{Guid.CreateVersion7():N}.mp3");

        try
        {
            var audio = new byte[FrameLength * FrameCount];
            for (var frame = 0; frame < FrameCount; frame++)
                FrameHeader.CopyTo(audio, frame * FrameLength);

            File.WriteAllBytes(path, audio);

            using (var tagged = TagLib.File.Create(path, "taglib/mp3", TagLib.ReadStyle.Average))
            {
                tagged.Tag.Title = title;
                tagged.Tag.Performers = [artist];

                if (album is not null)
                    tagged.Tag.Album = album;

                tagged.Save();
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
