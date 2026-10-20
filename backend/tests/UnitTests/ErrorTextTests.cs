// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using App.Common;

namespace UnitTests;

public partial class ErrorTextTests
{
    [Theory]
    [InlineData("ru", "Радио берёт от 1 до 50 треков.")]
    [InlineData("ru-RU", "Радио берёт от 1 до 50 треков.")]
    [InlineData("en", "Radio limit must be between 1 and 50.")]
    [InlineData("de", "Radio limit must be between 1 and 50.")]
    public void An_exception_speaks_the_request_language(string language, string expected)
    {
        var ex = new ValidationException("Radio limit must be between 1 and {0}.", 50);

        Assert.Equal(expected, InLanguage(language, () => ex.LocalizedMessage));
    }

    [Fact]
    public void The_log_message_stays_english()
    {
        var ex = InLanguage("ru", () => new ValidationException("Radio limit must be between 1 and {0}.", 50));

        Assert.Equal("Radio limit must be between 1 and 50.", ex.Message);
    }

    [Fact]
    public void Every_thrown_message_has_a_russian_text()
    {
        var missing = ThrownTemplates().Where(template => !ErrorText.Russian.ContainsKey(template)).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Translations_keep_the_placeholders()
    {
        foreach (var (english, russian) in ErrorText.Russian)
        {
            Assert.Equal(Placeholders(english), Placeholders(russian));
        }
    }

    private static IEnumerable<string> ThrownTemplates()
    {
        var source = SourceRoot();
        var templates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            foreach (Match match in ThrowSite().Matches(File.ReadAllText(file)))
                templates.Add(Regex.Unescape(match.Groups["text"].Value));
        }

        Assert.NotEmpty(templates);
        return templates;
    }

    private static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "App");
            if (Directory.Exists(candidate))
                return Path.Combine(dir.FullName, "src");
        }

        throw new DirectoryNotFoundException("backend/src not found above the test output.");
    }

    private static T InLanguage<T>(string language, Func<T> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(language);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static string[] Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(match => match.Value).Order()];

    [GeneratedRegex(@"(?:Validation|NotFound|Conflict|Forbidden|Authentication|App)Exception\(\s*""(?<text>(?:[^""\\]|\\.)*)""")]
    private static partial Regex ThrowSite();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
