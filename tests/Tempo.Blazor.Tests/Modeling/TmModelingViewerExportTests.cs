using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 6 print/export contract: <c>ExportSvgAsync</c> renders the presentation copy through the
/// existing <see cref="IDiagramSvgRenderer"/>; the overlay tokens are resolved for the requested
/// theme through the Modeling JS module into an export-only copy, and a missing JS runtime degrades
/// to the renderer's palette instead of throwing.
/// </summary>
public sealed class TmModelingViewerExportTests : LocalizationTestBase
{
    private const string ModelingModulePath = "./_content/Tempo.Blazor.Modeling/js/modeling-editor.js";

    public TmModelingViewerExportTests()
    {
        Services.AddTempoBlazorModeling();
    }

    [Fact]
    public async Task Export_renders_the_diagram_without_js_and_keeps_the_live_copy_intact()
    {
        using var cut = RenderWithHeatmap();

        var svg = await cut.Instance.ExportSvgAsync();

        svg.Should().StartWith("<svg").And.Contain("</svg>");
        Node(cut, "e-review").Style.Fill.Should().Be("var(--tm-color-heat-3)",
            "the export works on a clone, never on the live presentation copy");
    }

    [Fact]
    public async Task Export_resolves_overlay_tokens_for_the_light_theme()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.Setup<Dictionary<string, string>>("resolveTokens", _ => true)
            .SetResult(LightTokens());
        module.Setup<Dictionary<string, string>>("resolveTokens", IsDarkInvocation)
            .SetResult(DarkTokens());

        using var cut = RenderWithHeatmap();
        var svg = await cut.Instance.ExportSvgAsync(DiagramSvgTheme.Light);

        svg.Should().Contain("#fecaca", "the light heat-3 token was resolved into a concrete paint");
        svg.Should().NotContain("var(--tm-color-heat");
    }

    [Fact]
    public async Task Export_resolves_the_dark_theme_through_the_module()
    {
        var module = JSInterop.SetupModule(ModelingModulePath);
        module.Setup<Dictionary<string, string>>("resolveTokens", _ => true)
            .SetResult(LightTokens());
        module.Setup<Dictionary<string, string>>("resolveTokens", IsDarkInvocation)
            .SetResult(DarkTokens());

        using var cut = RenderWithHeatmap();
        var svg = await cut.Instance.ExportSvgAsync(DiagramSvgTheme.Dark);

        svg.Should().Contain("#7f1d1d", "the dark heat-3 token was resolved into a concrete paint");
    }

    [Fact]
    public async Task Export_without_js_drops_every_unresolved_token_instead_of_writing_var_into_the_svg()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var svg = await cut.Instance.ExportSvgAsync(DiagramSvgTheme.Light);

        svg.Should().NotContain("var(", "a standalone SVG has no stylesheet to resolve a token against");
        svg.Should().Contain("</svg>");
        Node(cut, "e-review").Style.Fill.Should().Be("var(--tm-color-heat-3)",
            "only the export copy is stripped — the live presentation keeps its token paint");
    }

    [Fact]
    public async Task Export_of_an_empty_model_is_still_a_valid_svg()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateEmptyModel())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var svg = await cut.Instance.ExportSvgAsync();

        svg.Should().Contain("<svg");
    }

    private IRenderedComponent<TmModelingViewer> RenderWithHeatmap()
        => Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

    private static DiagramNode Node(IRenderedComponent<TmModelingViewer> cut, string elementId)
        => cut.FindComponent<Tempo.Blazor.Components.Diagram.TmDiagramEditor>().Instance.Document!
            .Pages.SelectMany(page => page.Nodes)
            .Single(node => node.Data.TryGetValue("modelElementId", out var id)
                && string.Equals(id?.ToString(), elementId, StringComparison.Ordinal));

    /// <summary>bUnit may surface a module call's params array whole or spread — flatten both.</summary>
    private static bool IsDarkInvocation(JSRuntimeInvocation invocation)
        => invocation.Arguments
            .SelectMany(argument => argument is object?[] array ? array : [argument])
            .Any(argument => string.Equals(argument?.ToString(), "dark", StringComparison.Ordinal));

    private static Dictionary<string, string> LightTokens() => new(StringComparer.Ordinal)
    {
        ["--tm-color-heat-0"] = "#dcfce7",
        ["--tm-color-heat-1"] = "#fef3c7",
        ["--tm-color-heat-2"] = "#fed7aa",
        ["--tm-color-heat-3"] = "#fecaca",
        ["--tm-color-primary"] = "#2563eb",
        ["--tm-color-success"] = "#22c55e",
        ["--tm-color-warning"] = "#f59e0b",
        ["--tm-border-color-strong"] = "#6b7280"
    };

    private static Dictionary<string, string> DarkTokens() => new(StringComparer.Ordinal)
    {
        ["--tm-color-heat-0"] = "#14532d",
        ["--tm-color-heat-1"] = "#78350f",
        ["--tm-color-heat-2"] = "#7c2d12",
        ["--tm-color-heat-3"] = "#7f1d1d",
        ["--tm-color-primary"] = "#60a5fa",
        ["--tm-color-success"] = "#22c55e",
        ["--tm-color-warning"] = "#f59e0b",
        ["--tm-border-color-strong"] = "#94a3b8"
    };
}
