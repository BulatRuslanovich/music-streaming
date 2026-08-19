// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Domain.Common;
using Xunit;

namespace MusicStreaming.UnitTests;

public class TranslitTests
{
    [Theory]
    [InlineData("Король и Шут", "Korol i Shut")]
    [InlineData("Кино", "Kino")]
    [InlineData("Земфира", "Zemfira")]
    [InlineData("Наутилус Помпилиус", "Nautilus Pompilius")]
    [InlineData("Ночные снайперы", "Nochnye snaypery")]
    [InlineData("Щи", "Shchi")]
    public void Writes_names_the_way_they_are_usually_signed(string source, string expected) =>
        Assert.Equal(expected, Translit.ToLatin(source));

    [Fact]
    public void Keeps_capitals_together_so_shouting_stays_shouting() =>
        Assert.Equal("SHUT", Translit.ToLatin("ШУТ"));

    [Fact]
    public void Capitalises_only_the_first_letter_of_a_multi_letter_replacement() =>
        Assert.Equal("Shut", Translit.ToLatin("Шут"));

    [Fact]
    public void Drops_the_soft_sign_rather_than_writing_an_apostrophe() =>
        Assert.Equal("Korol", Translit.ToLatin("Король"));

    [Fact]
    public void Carries_latin_digits_and_punctuation_through_untouched() =>
        Assert.Equal("Bi-2 & AC/DC", Translit.ToLatin("Би-2 & AC/DC"));

    [Fact]
    public void Returns_an_empty_string_for_nothing() =>
        Assert.Equal(string.Empty, Translit.ToLatin(null));
}
