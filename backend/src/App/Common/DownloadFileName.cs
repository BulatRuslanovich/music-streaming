// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text;

namespace App.Common;

public static class DownloadFileName
{
    private const string InvalidCharacters = "\\/:*?\"<>|";
    private const int MaxBaseLength = 120;

    public static string For(string? artist, string title, string extension)
    {
        var basis = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
        var builder = new StringBuilder(basis.Length);

        foreach (var character in basis)
        {
            var safe = InvalidCharacters.Contains(character) || char.IsControl(character) ? ' ' : character;

            if (safe != ' ' || (builder.Length > 0 && builder[^1] != ' '))
                builder.Append(safe);
        }

        var cleaned = builder.ToString().Trim(' ', '.');
        if (cleaned.Length > MaxBaseLength)
            cleaned = cleaned[..MaxBaseLength].TrimEnd();

        return (cleaned.Length == 0 ? "track" : cleaned) + extension;
    }
}
