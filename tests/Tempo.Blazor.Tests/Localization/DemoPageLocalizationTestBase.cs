using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Base for the tests that render a Demo.SharedUI PAGE (e.g. <c>ChartsPage</c>). A page's own
/// strings live in the demo-only <c>DemoResources</c> tables — they moved out of the library's
/// <c>TmResources</c> and therefore out of <see cref="MockTmLocalizer"/>, which mirrors the library
/// key set. The base re-registers the English mock wrapped with the repo's demo en table, the same
/// composition the demo hosts get from <c>DemoTmLocalizer</c>.
/// </summary>
public class DemoPageLocalizationTestBase : LocalizationTestBase
{
    protected DemoPageLocalizationTestBase()
    {
        Services.AddSingleton<ITmLocalizer>(new DemoTableEnglishLocalizer(BuildEnglishLocalizer()));
    }
}

/// <summary>The English library mock + the demo en table (read from the repo's DemoResources.json).</summary>
public sealed class DemoTableEnglishLocalizer(MockTmLocalizer inner) : ITmLocalizer
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> DemoTableLazy = new(LoadDemoTable);

    public CultureInfo Culture => CultureInfo.CurrentUICulture;

    public string this[string key] =>
        DemoTableLazy.Value.TryGetValue(key, out var value) ? value : inner[key];

    public string this[string key, params object[] arguments] =>
        DemoTableLazy.Value.TryGetValue(key, out var value)
            ? (arguments.Length > 0 ? string.Format(CultureInfo.CurrentUICulture, value, arguments) : value)
            : inner[key, arguments];

    public bool TryGetString(string key, out string value)
    {
        if (DemoTableLazy.Value.TryGetValue(key, out var demoValue))
        {
            value = demoValue;
            return true;
        }

        return inner.TryGetString(key, out value!);
    }

    private static IReadOnlyDictionary<string, string> LoadDemoTable()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }

        var path = Path.Combine(
            dir!.FullName, "src", "Tempo.Blazor.Demo.SharedUI", "Resources", "DemoResources.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            map[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return map;
    }
}
