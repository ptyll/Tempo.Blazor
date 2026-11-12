using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Plan 92e96b25, phase 5 (TG-005): the mock gap was the hardcoded hex column colours
/// (<c>#10b981</c>, <c>#3b82f6</c>, <c>#6b7280</c>) that could not follow a theme. The investigation
/// (recorded in the backlog) proved the DEMO supplies them — <c>TmKanbanBoard</c> itself never paints
/// a colour of its own; <see cref="KanbanColumn.Color"/> renders verbatim as an inline
/// <c>background-color</c>. The contract pinned here:
/// <list type="bullet">
/// <item>a host (the demo included) passes <c>var(--tm-kanban-column-…)</c> token references so the
/// dot follows the theme, light and dark;</item>
/// <item>a caller colour (hex or any CSS colour) still renders verbatim — the parameter keeps its
/// meaning;</item>
/// <item>a column without a colour renders no dot at all (no invented default).</item>
/// </list>
/// </summary>
public class TmKanbanColumnColorTests : LocalizationTestBase
{
    private record ColorTask(int Id, string Title, string Status);

    private static RenderFragment<ColorTask> CardTemplate => task => builder
        => builder.AddContent(0, task.Title);

    [Fact]
    public void ColumnColor_RendersVerbatim_AsInlineBackground()
    {
        var columns = new[] { new KanbanColumn("todo", "To Do", "var(--tm-kanban-column-todo)") };
        var cut = Render<TmKanbanBoard<ColorTask>>(p => p
            .Add(x => x.Columns, columns)
            .Add(x => x.Items, new[] { new ColorTask(1, "Task A", "todo") })
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, CardTemplate));

        cut.Find(".tm-kanban__header-color")
            .GetAttribute("style")
            .Should().Contain("background-color: var(--tm-kanban-column-todo)");
    }

    [Fact]
    public void ColumnColor_AcceptsACallerHex_Verbatim()
    {
        var columns = new[] { new KanbanColumn("done", "Done", "#10b981") };
        var cut = Render<TmKanbanBoard<ColorTask>>(p => p
            .Add(x => x.Columns, columns)
            .Add(x => x.Items, new[] { new ColorTask(1, "Task A", "done") })
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, CardTemplate));

        cut.Find(".tm-kanban__header-color")
            .GetAttribute("style")
            .Should().Contain("background-color: #10b981");
    }

    [Fact]
    public void ColumnWithoutColor_RendersNoDot()
    {
        var columns = new[] { new KanbanColumn("todo", "To Do") };
        var cut = Render<TmKanbanBoard<ColorTask>>(p => p
            .Add(x => x.Columns, columns)
            .Add(x => x.Items, new[] { new ColorTask(1, "Task A", "todo") })
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, CardTemplate));

        cut.FindAll(".tm-kanban__header-color").Should().BeEmpty(
            "the component invents no default colour — the host opts in");
    }

    [Fact]
    public void SwimlaneColor_RendersVerbatim_Too()
    {
        var columns = new[] { new KanbanColumn("todo", "To Do") };
        var lanes = new[] { new KanbanSwimlane("Alice", "Alice", "var(--tm-kanban-column-todo)") };
        var cut = Render<TmKanbanBoard<ColorTask>>(p => p
            .Add(x => x.Columns, columns)
            .Add(x => x.Items, new[] { new ColorTask(1, "Task A", "todo") })
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.SwimlaneSelector, t => "Alice")
            .Add(x => x.Swimlanes, lanes)
            .Add(x => x.CardTemplate, CardTemplate));

        cut.Find(".tm-kanban__swimlane-color")
            .GetAttribute("style")
            .Should().Contain("background-color: var(--tm-kanban-column-todo)");
    }
}

/// <summary>
/// The token half of TG-005: <c>--tm-kanban-column-*</c> ship in <c>tokens.css</c>, flip to lifted
/// tones on the dark roots (including the light-island reset) in <c>tokens-dark.css</c>, and every
/// value clears the non-text 3:1 level against the header background it sits on
/// (<c>--tm-bg-muted</c>) in BOTH themes — the TG-002 series-token precedent (light strong tones,
/// dark 400-tones). The demo page consumes them and carries no hardcoded column hex any more.
/// </summary>
public class TmKanbanColumnTokenTests
{
    private static readonly string[] Slots = ["backlog", "todo", "doing", "review", "done"];

    private static FileInfo TokensCss => new(Path.Combine(ThemeCss.RepositoryRoot().FullName,
        "src", "Tempo.Blazor", "wwwroot", "css", "tokens.css"));

    private static FileInfo TokensDarkCss => new(Path.Combine(ThemeCss.RepositoryRoot().FullName,
        "src", "Tempo.Blazor", "wwwroot", "css", "tokens-dark.css"));

    private static string DemoPage => File.ReadAllText(Path.Combine(
        ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.Demo.SharedUI", "Pages", "KanbanPage.razor"));

    [Theory]
    [InlineData("backlog")]
    [InlineData("todo")]
    [InlineData("doing")]
    [InlineData("review")]
    [InlineData("done")]
    public void LightTheme_DeclaresTheKanbanColumnToken(string slot)
    {
        var css = File.ReadAllText(TokensCss.FullName);
        Resolve(css, $"--tm-kanban-column-{slot}").Should().MatchRegex("^#[0-9a-fA-F]{6}$",
            $"tokens.css declares --tm-kanban-column-{slot} (hex or a one-level var() to a hex tone)");
    }

    [Theory]
    [InlineData("backlog")]
    [InlineData("todo")]
    [InlineData("doing")]
    [InlineData("review")]
    [InlineData("done")]
    public void DarkTheme_ReDeclaresTheKanbanColumnToken(string slot)
    {
        var css = File.ReadAllText(TokensDarkCss.FullName);
        var name = $"--tm-kanban-column-{slot}";
        var declarations = Regex.Matches(css, $"--tm-kanban-column-{slot}:").Count;
        declarations.Should().BeGreaterThanOrEqualTo(2,
            "the dark roots re-declare the token AND the light-island reset re-pins the light value");

        // The FIRST declaration (the dark theme block) must resolve to a concrete dark tone.
        Resolve(css, name, File.ReadAllText(TokensCss.FullName))
            .Should().MatchRegex("^#[0-9a-fA-F]{6}$", "the dark value resolves to a concrete tone");
    }

    [Theory]
    [InlineData("backlog", "#f3f4f6", "#334155")]
    [InlineData("todo", "#f3f4f6", "#334155")]
    [InlineData("doing", "#f3f4f6", "#334155")]
    [InlineData("review", "#f3f4f6", "#334155")]
    [InlineData("done", "#f3f4f6", "#334155")]
    public void EveryTone_ClearsThreeToOne_OnTheHeaderBackground_InBothThemes(string slot, string lightBg, string darkBg)
    {
        var lightCss = File.ReadAllText(TokensCss.FullName);
        var darkCss = File.ReadAllText(TokensDarkCss.FullName);

        var light = Resolve(lightCss, $"--tm-kanban-column-{slot}");
        var dark = Resolve(darkCss, $"--tm-kanban-column-{slot}", lightCss);

        Contrast(light, lightBg).Should().BeGreaterThanOrEqualTo(3.0,
            "light --tm-kanban-column-{0} clears the non-text 3:1 level on the light header", slot);
        Contrast(dark, darkBg).Should().BeGreaterThanOrEqualTo(3.0,
            "dark --tm-kanban-column-{0} clears the non-text 3:1 level on the dark header", slot);
    }

    [Fact]
    public void DemoPage_UsesTheKanbanColumnTokens_AndNoHardcodedColumnHex()
    {
        DemoPage.Should().Contain("var(--tm-kanban-column-", "the demo consumes the token palette");

        var hexes = Regex.Matches(DemoPage, "KanbanColumn\\([^)]*\"(#[0-9a-fA-F]{6})\"");
        hexes.Count.Should().Be(0,
            "the demo passes tones (var(--tm-kanban-column-*)) instead of hardcoded hex column colours");
    }

    // WCAG relative-luminance contrast, computed from sRGB hexes (same helper shape as
    // TokenContrastTests uses for the chart series tokens): the lighter luminance over the darker.
    private static double Contrast(string first, string second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    // Resolves a token to a concrete hex: the value is either a literal "#rrggbb" or a one-level
    // "var(--other)" alias looked up in the same file (falling back to the light file for tokens the
    // dark theme inherits unchanged, which is exactly how the cascade works).
    private static string Resolve(string css, string name, string? fallbackCss = null)
    {
        var value = Read(css, name);
        if (value is null && fallbackCss is not null)
        {
            value = Read(fallbackCss, name);
        }

        value.Should().NotBeNull($"the token {name} is declared");
        var alias = Regex.Match(value!, "^var\\((--[a-z0-9-]+)\\)$", RegexOptions.IgnoreCase);
        if (!alias.Success)
        {
            return value!;
        }

        var inner = Read(css, alias.Groups[1].Value)
                    ?? (fallbackCss is not null ? Read(fallbackCss, alias.Groups[1].Value) : null);
        inner.Should().NotBeNull($"the alias {alias.Groups[1].Value} of {name} resolves");
        return inner!;
    }

    private static string? Read(string css, string name)
    {
        var match = Regex.Match(css, $"{Regex.Escape(name)}:\\s*([^;]+);");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static double Luminance(string hex)
    {
        var r = Convert.ToInt32(hex[1..3], 16) / 255.0;
        var g = Convert.ToInt32(hex[3..5], 16) / 255.0;
        var b = Convert.ToInt32(hex[5..7], 16) / 255.0;
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Channel(double c)
        => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
