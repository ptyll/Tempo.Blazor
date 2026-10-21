using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// Fáze 8 review, U2 — the ORDER of the shell's two callbacks. A host that maps
/// <c>CollapsedPanels</c> onto its own state (the email template editor's <c>ToolboxCollapsed</c>,
/// D10) cannot tell a rail the LAYOUT imposed (the tablet one-expanded-panel rule) from a collapse
/// the user asked for; it can only recognise it by knowing whether the layout changed in the same
/// batch — which requires the layout callback to arrive FIRST.
/// </summary>
public class TmEditorShellLayoutEventOrderTests : LocalizationTestBase
{
    [Fact]
    public async Task ResolvedLayoutChanged_IsRaisedBeforeCollapsedPanelsChanged()
    {
        var order = new List<string>();
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Auto);
            p.Add(x => x.InitialMode, TmLayoutMode.Desktop);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Left, builder => builder.AddContent(0, "palette"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "inspector"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "canvas"));
            p.Add(x => x.ResolvedLayoutChanged,
                EventCallback.Factory.Create<TmLayoutMode>(new object(), mode => order.Add($"layout:{mode}")));
            p.Add(x => x.CollapsedPanelsChanged,
                EventCallback.Factory.Create<EditorShellPanel>(new object(), panels => order.Add($"collapsed:{panels}")));
        });

        cut.Find(".tm-editor-shell").GetAttribute("data-layout").Should().Be("desktop");

        // The container measurement the shell's own observer would report: a tablet. Both panels are
        // expanded, so the one-expanded-panel rule rails the left one in the same pass.
        var observer = cut.FindComponent<TmLayoutObserver>();
        await cut.InvokeAsync(() => observer.Instance.OnLayoutModeChanged("tablet"));

        order.Should().Equal(
            ["layout:Tablet", "collapsed:Left"],
            "a host must hear the layout BEFORE the rail the layout imposed (U2) — otherwise it cannot "
            + "tell that collapse from a user collapse");
    }

    [Fact]
    public void AUserCollapse_RaisesOnlyTheCollapse_NoLayoutCallback()
    {
        var order = new List<string>();
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Desktop);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Left, builder => builder.AddContent(0, "palette"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "inspector"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "canvas"));
            p.Add(x => x.ResolvedLayoutChanged,
                EventCallback.Factory.Create<TmLayoutMode>(new object(), mode => order.Add($"layout:{mode}")));
            p.Add(x => x.CollapsedPanelsChanged,
                EventCallback.Factory.Create<EditorShellPanel>(new object(), panels => order.Add($"collapsed:{panels}")));
        });

        cut.Find(".tm-editor-shell__panel-toggle--left").Click();

        order.Should().Equal(["collapsed:Left"],
            "a user collapse is not a layout state and must not be accompanied by a layout callback");
    }
}
