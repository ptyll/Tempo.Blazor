using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>bUnit tests for <see cref="TmWireframeLayersPanel"/> (wireframe redesign plan, phase 1).</summary>
public class TmWireframeLayersPanelTests : LocalizationTestBase
{
    private static WireframeDocument NewDocument()
    {
        var doc = new WireframeDocument { Title = "Test" };
        doc.EnsureActivePage();
        return doc;
    }

    [Fact]
    public void ActiveLayerDefault_IsReportedAfterRender_NotDuringParametersSet()
    {
        var doc = NewDocument();
        var reported = new List<string?>();

        var cut = Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerId, (string?)null)
            .Add(c => c.ActiveLayerIdChanged, EventCallback.Factory.Create<string?>(this, id => reported.Add(id))));

        var defaultLayerId = doc.Layers.OrderBy(l => l.Order).First().Id;
        cut.WaitForAssertion(() => reported.Should().Equal(defaultLayerId));

        // The default layer is already shown as active although the parent never confirmed it.
        cut.FindAll(".tm-wd-layers__radio--checked").Should().ContainSingle();
    }

    [Fact]
    public void ActiveLayerDefault_ReportIsAwaited_SoAHandlerFailureSurfaces()
    {
        // Fire-and-forget from OnParametersSet swallowed handler exceptions; the awaited after-render
        // report surfaces them on the renderer like every other callback.
        var doc = NewDocument();

        var act = () => Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerIdChanged, EventCallback.Factory.Create<string?>(this,
                (Func<string?, Task>)(_ => throw new InvalidOperationException("parent handler failed")))));

        act.Should().Throw<Exception>().WithMessage("*parent handler failed*");
    }
    [Fact]
    public void ActiveLayerDefault_IsReportedOnlyOnce_WhenParentDoesNotFeedItBack()
    {
        var doc = NewDocument();
        var reported = new List<string?>();

        var cut = Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerIdChanged, EventCallback.Factory.Create<string?>(this, id => reported.Add(id))));

        cut.Render(p => p.Add(c => c.SelectedElementIds, new[] { "x" }));
        cut.Render(p => p.Add(c => c.SelectedElementIds, Array.Empty<string>()));

        reported.Should().HaveCount(1);
    }

    [Fact]
    public void ActiveLayer_ProvidedByParent_IsNotOverwritten()
    {
        var doc = NewDocument();
        var reported = new List<string?>();
        var existing = doc.Layers.OrderBy(l => l.Order).First().Id;

        Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerId, existing)
            .Add(c => c.ActiveLayerIdChanged, EventCallback.Factory.Create<string?>(this, id => reported.Add(id))));

        reported.Should().BeEmpty();
    }
}
