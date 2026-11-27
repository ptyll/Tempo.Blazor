using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Overflow measurement contract (redesign 146c7e1b, DE-1.2). The ribbon used to ship its own ES module
/// (<c>toolbar-overflow.mjs</c>) that measured a scrolling strip and reported the clipped commands. Measuring now belongs to
/// the core <c>tm-toolbar.js</c> behind <see cref="TmToolbar"/>: the ribbon only hands it a bar of registered items, so these
/// tests pin that the ribbon is wired to the CORE mechanism and carries no module of its own.
/// </summary>
public class DocumentEditorToolbarOverflowControllerTests : LocalizationTestBase
{
    private const string CoreToolbarModule = "./_content/Tempo.Blazor/js/tm-toolbar.js";

    [Fact]
    public void Ribbon_AttachesTheCoreToolbarMeasurement_OnFirstRender()
    {
        var module = JSInterop.SetupModule(CoreToolbarModule);
        module.SetupVoid("attach", _ => true).SetVoidResult();

        Render<TmDocumentEditorToolbar>();

        module.Invocations.Should().ContainSingle(invocation => invocation.Identifier == "attach",
            "the ribbon's TmToolbar attaches tm-toolbar.js once; that module measures the fit");
        var options = module.Invocations.Single(invocation => invocation.Identifier == "attach").Arguments[2];
        options!.ToString().Should().Contain("overflow = True", "the ribbon asks the core toolbar to collapse into More (Overflow=Menu)");
    }

    [Fact]
    public void Ribbon_ImportsNoModuleOfItsOwn_ForOverflow()
    {
        Render<TmDocumentEditorToolbar>();

        JSInterop.Invocations.Select(invocation => invocation.Identifier).Should().NotContain("createOverflowController");
        JSInterop.Invocations
            .SelectMany(invocation => invocation.Arguments)
            .Select(argument => argument?.ToString() ?? string.Empty)
            .Should().NotContain(argument => argument.Contains("toolbar-overflow.mjs"),
                "DE-local collapse was removed: the core TmToolbar measures");
    }

    [Fact]
    public void RibbonComponent_NoLongerExposesTheOverflowReportingContract()
    {
        typeof(TmDocumentEditorToolbar).GetMethod("SetOverflowingAsync").Should().BeNull(
            "the JS controller that called it is gone; the core TmToolbar.OnFitChanged is the report");
    }

    [Fact]
    public async Task CoreFitReport_ShowsTheMoreButton_AndWidening_RemovesItAgain()
    {
        var cut = Render<TmDocumentEditorToolbar>();
        cut.Find("[data-testid='document-ribbon-tab-layout']").Click();
        cut.FindAll("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Should().BeEmpty(
            "without a report that something does not fit, nothing is in More (the Layout tab has no OverflowOnly command)");

        var toolbar = cut.FindComponent<TmToolbar>().Instance;
        await cut.InvokeAsync(() => toolbar.OnFitChanged(2));
        cut.FindAll("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Should().HaveCount(1);

        await cut.InvokeAsync(() => toolbar.OnFitChanged(500));
        cut.FindAll("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Should().BeEmpty(
            "when the window grows and everything fits, the More button disappears");
    }
}
