// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text;

namespace MusicStreaming.Domain.Common;

/// <summary>
/// Переводит кириллицу в латиницу так, как подписывают музыку: «Король и Шут» → «Korol i Shut».
/// Схема одна и намеренно без вариантов: задача не в академической точности, а в том, чтобы попасть
/// в ту запись, под которой исполнителя завели во внешней базе.
/// </summary>
public static class Translit
{
    private static readonly Dictionary<char, string> Map = new()
    {
        ['а'] = "a",
        ['б'] = "b",
        ['в'] = "v",
        ['г'] = "g",
        ['д'] = "d",
        ['е'] = "e",
        ['ё'] = "e",
        ['ж'] = "zh",
        ['з'] = "z",
        ['и'] = "i",
        ['й'] = "y",
        ['к'] = "k",
        ['л'] = "l",
        ['м'] = "m",
        ['н'] = "n",
        ['о'] = "o",
        ['п'] = "p",
        ['р'] = "r",
        ['с'] = "s",
        ['т'] = "t",
        ['у'] = "u",
        ['ф'] = "f",
        ['х'] = "kh",
        ['ц'] = "ts",
        ['ч'] = "ch",
        ['ш'] = "sh",
        ['щ'] = "shch",
        ['ы'] = "y",
        ['э'] = "e",
        ['ю'] = "yu",
        ['я'] = "ya",

        // Мягкий и твёрдый знаки выбрасываются, а не превращаются в апостроф: именно так получается
        // привычное «Korol», а не «Korol'».
        ['ъ'] = "",
        ['ь'] = "",

        // Соседние алфавиты попадаются в тех же подписях.
        ['і'] = "i",
        ['ї'] = "yi",
        ['є'] = "ye",
        ['ґ'] = "g",
        ['ў'] = "u",
    };

    public static string ToLatin(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var builder = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index += 1)
        {
            var symbol = value[index];
            var lower = char.ToLowerInvariant(symbol);

            if (!Map.TryGetValue(lower, out var replacement))
            {
                builder.Append(symbol);
                continue;
            }

            if (replacement.Length == 0)
                continue;

            if (symbol == lower)
            {
                builder.Append(replacement);
                continue;
            }

            // Заглавная даёт «Sh» в обычном слове и «SH» посреди набранного капсом: за подсказку
            // берётся следующая буква, иначе «ШУТ» превратился бы в «ShUT».
            builder.Append(NextIsUpper(value, index)
                ? replacement.ToUpperInvariant()
                : char.ToUpperInvariant(replacement[0]) + replacement[1..]);
        }

        return builder.ToString();
    }

    private static bool NextIsUpper(string value, int index)
    {
        var next = index + 1;

        return next < value.Length && char.IsUpper(value[next]);
    }
}
