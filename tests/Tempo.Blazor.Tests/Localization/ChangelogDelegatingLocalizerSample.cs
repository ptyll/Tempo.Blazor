using System.Globalization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// The before/after delegating localizers from the CHANGELOG / <c>docs/localization-plurals.md</c>,
/// compiled onto the <c>TmModelingModelTree_Count</c> family. A default interface member is not in
/// scope through its simple name, so <c>string.Format(Culture, …)</c> only compiles when the class
/// declares <c>Culture</c> itself.
/// </summary>
internal static class ChangelogDelegatingLocalizerSample
{
    public sealed class PartialOverrideLocalizerBefore(ITmLocalizer inner) : ITmLocalizer
    {
        public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("cs");
        public string this[string key] => key == "TmModelingModelTree_Count" ? "{0} prvky" : inner[key];
        public string this[string key, params object[] arguments] => string.Format(Culture, this[key], arguments);
    }

    public sealed class PartialOverrideLocalizerAfter(ITmLocalizer inner) : ITmLocalizer
    {
        private static readonly Dictionary<string, string> Mine = new()
        {
            ["TmModelingModelTree_Count_One"] = "{0} prvek",
            ["TmModelingModelTree_Count_Few"] = "{0} prvky",
            ["TmModelingModelTree_Count_Many"] = "{0} prvku",
            ["TmModelingModelTree_Count_Other"] = "{0} prvků",
        };

        public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("cs");
        public string this[string key] => Mine.TryGetValue(key, out var mine) ? mine : inner[key];
        public string this[string key, params object[] arguments] => string.Format(Culture, this[key], arguments);
    }
}
