using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Helpdesk;

/// <summary>Every text exists in every language we offer, with the same placeholders, so none is silently left in English.</summary>
public class LocalizationTests
{
    private static readonly string[] Offered = { "en", "vi" };

    private static Dictionary<string, string> Texts(string culture)
    {
        var assembly = typeof(HelpdeskDomainErrorCodes).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith($".Helpdesk.{culture}.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("texts").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{(\w+)\}").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();

    [Fact]
    public void Every_error_code_has_a_text_in_every_offered_language()
    {
        var codes = typeof(HelpdeskDomainErrorCodes).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToList();

        codes.ShouldNotBeEmpty();
        foreach (var culture in Offered)
        {
            var texts = Texts(culture);
            foreach (var code in codes)
            {
                texts.ShouldContainKey(code, $"{culture} has no text for {code}");
                texts[code].ShouldNotBeNullOrWhiteSpace($"{culture}:{code}");
            }
        }
    }

    [Fact]
    public void The_offered_languages_have_exactly_the_same_keys()
    {
        var english = Texts("en").Keys.ToHashSet();

        foreach (var culture in Offered.Where(c => c != "en"))
        {
            var other = Texts(culture).Keys.ToHashSet();
            english.Except(other).ShouldBeEmpty($"missing in {culture}");
            other.Except(english).ShouldBeEmpty($"{culture} has keys English does not");
        }
    }

    [Fact]
    public void A_translation_keeps_the_placeholders_the_error_fills_in()
    {
        var english = Texts("en");

        foreach (var culture in Offered.Where(c => c != "en"))
        {
            foreach (var (key, text) in Texts(culture))
            {
                Placeholders(text).ShouldBe(Placeholders(english[key]), $"{culture}:{key}");
            }
        }
    }

    [Fact]
    public void A_translation_is_really_a_translation()
    {
        var english = Texts("en");
        var untranslated = Texts("vi")
            .Where(pair => pair.Key.StartsWith("Helpdesk:", StringComparison.Ordinal) && pair.Key != "Helpdesk" && pair.Value == english[pair.Key])
            .Select(pair => pair.Key);

        untranslated.ShouldBeEmpty();
    }
}
