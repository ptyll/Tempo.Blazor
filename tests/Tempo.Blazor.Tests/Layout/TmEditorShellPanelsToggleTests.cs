using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// UX review (DiagramEditor redesign fze-6): the shell's closed-state mobile panels toggle.
/// <see cref="TmEditorShell.ShowMobilePanelsToggle"/> (default true) renders the labelled bar
/// naming the hidden panels; a host that renders its own mobile action bar (its tiles reopen
/// the panels) sets it to false, giving the mobile sheet exactly ONE reopen path. The bar's
/// label names every hidden panel — Left, Right and the one Extra sheet tab (the design limit).
/// </summary>
public class TmEditorShellPanelsToggleTests : LocalizationTestBase
{
    private static IReadOnlyList<TmActionItem> Actions =>
    [
        new TmActionItem { Id = "save", Label = "Save", OnClick = EventCallback.Factory.Create(new object(), () => { }) },
    ];

    private IRenderedComponent<TmEditorShell> RenderShell(
        TmLayoutMode mode,
        Action<ComponentParameterCollectionBuilder<TmEditorShell>>? extra = null)
    {
        return Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Header, builder => builder.AddContent(0, "Shell header"));
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "Right props"));
            extra?.Invoke(p);
        });
    }

    [Fact]
    public void MobileClosedSheet_ShowMobilePanelsToggleFalse_RendersNoToggle()
    {
        // A host with its own action bar hides the shell toggle: the sheet has one reopen path.
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.LeftOpen, false);
            p.Add(x => x.RightOpen, false);
            p.Add(x => x.MobileActions, Actions);
            p.Add(x => x.ShowMobilePanelsToggle, false);
        });

        cut.FindAll(".tm-editor-shell__panel-toggle--mobile").Should().BeEmpty();
        cut.Find(".tm-mobile-action-bar__bar").Should().NotBeNull();
    }

    [Fact]
    public void MobileClosedSheet_ShowMobilePanelsToggleDefaultsToTrue()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.LeftOpen, false);
            p.Add(x => x.RightOpen, false);
        });

        cut.Find(".tm-editor-shell__panel-toggle--mobile").Should().NotBeNull(
            "the closed-state toggle is the default reopen path");
    }

    [Fact]
    public void MobileClosedSheet_ToggleLabelNamesTheExtraPanelToo()
    {
        // The design limit is ONE extra sheet tab — the closed bar names it like every panel.
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.LeftOpen, false);
            p.Add(x => x.RightOpen, false);
            p.Add(x => x.Extra, builder => builder.AddContent(0, "Extra layers"));
            p.Add(x => x.ExtraTitle, "Layers");
        });

        cut.Find(".tm-editor-shell__panel-toggle--mobile .tm-editor-shell__panel-toggle-label")
            .TextContent.Trim().Should().Be("Blocks · Properties · Layers");
    }
}
