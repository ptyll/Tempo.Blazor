using System.Globalization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// The before/after delegating localizers from the 2.9.1 CHANGELOG, compiled. A default interface
/// member is not in scope through its simple name, so <c>string.Format(Culture, …)</c> only compiles
/// when the class declares <c>Culture</c> itself.
/// </summary>
internal static class ChangelogDelegatingLocalizerSample
{
    public sealed class PartialOverrideLocalizerBefore(ITmLocalizer inner) : ITmLocalizer
    {
        public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("cs");
        public string this[string key] => key == "TmModelingIssuePanel_Count" ? "Nálezy: {0}" : inner[key];
        public string this[string key, params object[] arguments] => string.Format(Culture, this[key], arguments);
    }

    public sealed class PartialOverrideLocalizerAfter(ITmLocalizer inner) : ITmLocalizer
    {
        private static readonly Dictionary<string, string> Mine = new()
        {
            ["TmModelingIssuePanel_Count_One"] = "{0} nález",
            ["TmModelingIssuePanel_Count_Few"] = "{0} nálezy",
            ["TmModelingIssuePanel_Count_Many"] = "{0} nálezu",
            ["TmModelingIssuePanel_Count_Other"] = "{0} nálezů",
        };

        public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("cs");
        public string this[string key] => Mine.TryGetValue(key, out var mine) ? mine : inner[key];
        public string this[string key, params object[] arguments] => string.Format(Culture, this[key], arguments);
    }
}
