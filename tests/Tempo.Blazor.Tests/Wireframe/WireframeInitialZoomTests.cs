using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Decision WIREFRAME-INITIAL-ZOOM: <see cref="WireframeInitialZoom"/> and the <c>InitialZoom</c> parameter of the canvas and the editor.
/// The script (<c>applyInitialZoom</c>, node-tested) lays the view out; these tests pin when .NET asks for it - the first render, a
/// replaced document, a page switch, a changed page size and a changed mode - and that a parent render never resets a user zoom.
/// </summary>
public class WireframeInitialZoomTests : LocalizationTestBase
{
    private const string Apply = "tmWireframeDesigner.applyInitialZoom";

    private static WireframeComponentDef Def(string type) => new()
    {
        Type = type, DisplayName = type, Category = "Test", DefaultWidth = 120, DefaultHeight = 36,
        IsBuiltIn = true, Props = [], RenderSvg = (_, _) => { }
    };

    private void Register()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterDefinition(Def("TmButton"));
        Services.AddSingleton(registry);
    }

    private static WireframeDocument TwoPages()
    {
        var doc = new WireframeDocument { Title = "Zoom", Width = 1440, Height = 900 };
        doc.Pages.Clear();
        doc.Pages.Add(new WireframePage { Id = "desktop", Name = "Desktop", Width = 1440, Height = 900 });
        doc.Pages.Add(new WireframePage { Id = "mobile", Name = "Mobile", Width = 390, Height = 844 });
        doc.ActivePageId = "desktop";
        return doc;
    }

    private List<(string Mode, double Scale)> Applied()
        => JSInterop.Invocations.Where(i => i.Identifier == Apply)
            .Select(i => ((string)i.Arguments[1]!, 0.0)).ToList();

    private IRenderedComponent<TmWireframeDesignerCanvas> RenderCanvas(WireframeDocument doc, Action<ComponentParameterCollectionBuilder<TmWireframeDesignerCanvas>>? extra = null)
    {
        Register();
        JSInterop.Setup<double>(Apply, _ => true).SetResult(0.5);
        return Render<TmWireframeDesignerCanvas>(p =>
        {
            p.Add(c => c.Document, doc);
            extra?.Invoke(p);
        });
    }

    // ── The enum and the parameter ────────────────────────────────────────────

    [Fact]
    public void WireframeInitialZoom_ValuesAreStable_FitIsTheDefault()
    {
        ((int)WireframeInitialZoom.Fit).Should().Be(0);
        ((int)WireframeInitialZoom.ActualSize).Should().Be(1);
        typeof(WireframeInitialZoom).Namespace.Should().Be("Tempo.Blazor.Components.Wireframe");

        Register();
        Render<TmWireframeDesignerCanvas>().Instance.InitialZoom.Should().Be(WireframeInitialZoom.Fit);
        Render<TmWireframeEditor>().Instance.InitialZoom.Should().Be(WireframeInitialZoom.Fit);
    }

    // ── First render ──────────────────────────────────────────────────────────

    [Fact]
    public void Canvas_FirstRender_InitialisesWithTheModeAndAppliesItOnce_ReportingTheRealScale()
    {
        var reported = new List<double>();
        var cut = RenderCanvas(TwoPages(), p => p.Add(c => c.OnZoomChangedEvent, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<double>(this, reported.Add)));

        var init = JSInterop.Invocations.Single(i => i.Identifier == "tmWireframeDesigner.init");
        init.Arguments[2].Should().BeEquivalentTo(new { initialZoom = "fit" }, o => o.ExcludingMissingMembers());
        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == Apply).Should().Be(1));
        JSInterop.Invocations.Single(i => i.Identifier == Apply).Arguments[1].Should().Be("fit");
        reported.Should().Equal(0.5);
    }

    [Fact]
    public void Canvas_ActualSize_PassesTheActualMode()
    {
        RenderCanvas(TwoPages(), p => p.Add(c => c.InitialZoom, WireframeInitialZoom.ActualSize));

        JSInterop.Invocations.Single(i => i.Identifier == "tmWireframeDesigner.init").Arguments[2]
            .Should().BeEquivalentTo(new { initialZoom = "actual" }, o => o.ExcludingMissingMembers());
        JSInterop.Invocations.Single(i => i.Identifier == Apply).Arguments[1].Should().Be("actual");
    }

    [Fact]
    public void Canvas_ZeroScale_ReportsNothing_AHiddenTabIsLaidOutByTheFirstRealSize()
    {
        Register();
        JSInterop.Setup<double>(Apply, _ => true).SetResult(0);
        var reported = new List<double>();

        Render<TmWireframeDesignerCanvas>(p =>
        {
            p.Add(c => c.Document, TwoPages());
            p.Add(c => c.OnZoomChangedEvent, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<double>(this, reported.Add));
        });

        reported.Should().BeEmpty("a canvas without a size has no scale to report");
    }

    // ── Document / page / size / mode changes re-apply; anything else does not ─

    [Fact]
    public void Canvas_PageSwitch_AppliesTheInitialZoomAgain()
    {
        var doc = TwoPages();
        var cut = RenderCanvas(doc);
        JSInterop.Invocations.Count(i => i.Identifier == Apply).Should().Be(1);

        doc.ActivePageId = "mobile";
        cut.Render();

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == Apply).Should().Be(2));
    }

    [Fact]
    public void Canvas_ANewDocument_AppliesTheInitialZoomAgain()
    {
        var cut = RenderCanvas(TwoPages());

        cut.Render(p => p.Add(c => c.Document, TwoPages()));

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == Apply).Should().Be(2));
    }

    [Fact]
    public void Canvas_ChangedInitialZoom_AppliesTheNewMode()
    {
        var cut = RenderCanvas(TwoPages());

        cut.Render(p => p.Add(c => c.InitialZoom, WireframeInitialZoom.ActualSize));

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == Apply).Should().Be(2));
        JSInterop.Invocations.Last(i => i.Identifier == Apply).Arguments[1].Should().Be("actual");
    }

    [Fact]
    public void Canvas_ChangedPageSize_StartsTheViewOver()
    {
        var doc = TwoPages();
        var cut = RenderCanvas(doc);

        doc.ActivePage!.Width = 1920;
        cut.Render();

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == Apply).Should().Be(2));
    }

    [Fact]
    public void Canvas_AParentRenderWithNothingNew_NeverResetsTheUserZoom()
    {
        var doc = TwoPages();
        var cut = RenderCanvas(doc);

        cut.Render(p => p.Add(c => c.ShowGrid, false));
        cut.Render(p => p.Add(c => c.GridSize, 16));
        cut.Render(p => p.Add(c => c.ReadOnly, true));
        cut.Render();

        JSInterop.Invocations.Count(i => i.Identifier == Apply).Should().Be(1, "only the first render laid the view out; the user zoom of the same page is untouched");
    }

    // ── The editor passes it down and shows the real scale ────────────────────

    [Fact]
    public void Editor_PassesInitialZoomToTheCanvas_AndTheReadOutShowsTheRealScale()
    {
        Register();
        JSInterop.Setup<double>(Apply, _ => true).SetResult(0.46);

        var cut = Render<TmWireframeEditor>(p =>
        {
            p.Add(e => e.Document, TwoPages());
            p.Add(e => e.LayoutMode, TmLayoutMode.Desktop);
            p.Add(e => e.InitialZoom, WireframeInitialZoom.ActualSize);
        });

        cut.FindComponent<TmWireframeDesignerCanvas>().Instance.InitialZoom.Should().Be(WireframeInitialZoom.ActualSize);
        cut.WaitForAssertion(() => cut.Find(".tm-wd-editor__zoom-label").TextContent.Trim().Should().Be("46%"));
    }

    [Fact]
    public void Editor_SwitchingThePage_ReappliesTheZoom_AndTheReadOutFollows()
    {
        Register();
        JSInterop.Setup<double>(Apply, _ => true).SetResult(0.5);
        var cut = Render<TmWireframeEditor>(p =>
        {
            p.Add(e => e.Document, TwoPages());
            p.Add(e => e.LayoutMode, TmLayoutMode.Desktop);
        });
        cut.WaitForAssertion(() => cut.Find(".tm-wd-editor__zoom-label").TextContent.Trim().Should().Be("50%"));
        JSInterop.Invocations.Count(i => i.Identifier == Apply).Should().Be(1);

        JSInterop.Setup<double>(Apply, _ => true).SetResult(1.0);
        cut.Find("[data-testid='wd-page-tab-mobile']").Click();

        cut.WaitForAssertion(() =>
        {
            JSInterop.Invocations.Count(i => i.Identifier == Apply).Should().Be(2);
            cut.Find(".tm-wd-editor__zoom-label").TextContent.Trim().Should().Be("100%", "a small page stays at 100 %, it is not enlarged");
        });
    }
}
