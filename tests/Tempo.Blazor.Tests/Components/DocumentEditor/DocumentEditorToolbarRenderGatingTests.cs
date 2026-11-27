using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Perf plan N9 — the toolbar (4 500 lines of markup, ~35 icons) must not re-render when neither
/// its parameters nor its internal UI state changed; TmIcon must not re-render for identical inputs.
/// </summary>
public sealed class DocumentEditorToolbarRenderGatingTests : LocalizationTestBase
{
    [Fact]
    public void Toolbar_DoesNotRerender_WhenParametersAreUnchanged()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.IsDirty, false)
            .Add(p => p.CanUndo, true)
            .Add(p => p.ZoomPercent, 100));
        var renders = cut.RenderCount;

        cut.Render(parameters => parameters
            .Add(p => p.IsDirty, false)
            .Add(p => p.CanUndo, true)
            .Add(p => p.ZoomPercent, 100));

        cut.RenderCount.Should().Be(renders,
            "identical parameters must not rebuild the 4.5k-line toolbar render tree");
    }

    [Theory]
    [InlineData("IsDirty")]
    [InlineData("CanUndo")]
    [InlineData("ZoomPercent")]
    [InlineData("BoldState")]
    [InlineData("ReadOnly")]
    public void Toolbar_Rerenders_WhenACriticalParameterChanges(string parameterName)
    {
        var cut = Render<TmDocumentEditorToolbar>();
        var renders = cut.RenderCount;

        cut.Render(parameters =>
        {
            switch (parameterName)
            {
                case "IsDirty": parameters.Add(p => p.IsDirty, true); break;
                case "CanUndo": parameters.Add(p => p.CanUndo, true); break;
                case "ZoomPercent": parameters.Add(p => p.ZoomPercent, 150); break;
                case "BoldState": parameters.Add(p => p.BoldState, Tempo.Blazor.DocumentEditor.Models.WysiwygFormattingValue.Active); break;
                case "ReadOnly": parameters.Add(p => p.ReadOnly, true); break;
            }
        });

        cut.RenderCount.Should().BeGreaterThan(renders, $"changing {parameterName} must re-render the toolbar");
    }

    [Fact]
    public void Toolbar_Rerenders_ForInternalUiStateChanges()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        // Internal state change through an event handler (ribbon tab switch) must still render.
        var insertTab = cut.Find("[data-testid='document-ribbon-tab-insert']");
        insertTab.Click();
        cut.Find("[data-testid='document-ribbon-tab-insert']").GetAttribute("aria-selected")
            .Should().Be("true", "the tab switch must take visual effect (render not swallowed)");
    }

    [Fact]
    public async Task Toolbar_OverflowStateChange_StillRenders()
    {
        var cut = Render<TmDocumentEditorToolbar>();
        // The Layout tab has no OverflowOnly command: nothing is in More until the core toolbar reports that something does not fit.
        cut.Find("[data-testid='document-ribbon-tab-layout']").Click();
        cut.FindAll("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Should().BeEmpty("everything fits until the core toolbar reports otherwise");

        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(1));

        cut.FindAll("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Should().HaveCount(1,
            "the core measurement callback must still surface the More button");
    }

    [Fact]
    public void TmIcon_DoesNotRerender_ForIdenticalInputs()
    {
        var cut = Render<TmIcon>(parameters => parameters
            .Add(p => p.Name, "check")
            .Add(p => p.Size, IconSize.Sm));
        var renders = cut.RenderCount;
        var markup = cut.Markup;

        cut.Render(parameters => parameters
            .Add(p => p.Name, "check")
            .Add(p => p.Size, IconSize.Sm));

        cut.RenderCount.Should().Be(renders, "identical icon inputs must not re-render the svg");
        cut.Markup.Should().Be(markup);
    }

    [Fact]
    public void TmIcon_Rerenders_WhenNameOrSizeChanges()
    {
        var cut = Render<TmIcon>(parameters => parameters
            .Add(p => p.Name, "check")
            .Add(p => p.Size, IconSize.Sm));

        cut.Render(parameters => parameters
            .Add(p => p.Name, "save")
            .Add(p => p.Size, IconSize.Sm));
        cut.Markup.Should().Contain("17 21 17 13 7 13 7 21", "the save glyph must render after the name change");

        cut.Render(parameters => parameters
            .Add(p => p.Name, "save")
            .Add(p => p.Size, IconSize.Lg));
        cut.Markup.Should().Contain("tm-icon-lg", "the size change must re-render the css class");
    }
}
