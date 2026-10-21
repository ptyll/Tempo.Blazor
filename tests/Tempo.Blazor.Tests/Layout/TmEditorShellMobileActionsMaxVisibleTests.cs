using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// TDD tests for <see cref="TmEditorShell.MobileActionsMaxVisible"/> — the additive core parameter
/// that lets a host lift the mobile action bar's overflow budget. D20 of the email-editor plan lists
/// four bar actions (Back / Redo / Preview / Validate); without the parameter the shell could only
/// ever show three and the fourth landed in the bar's own "More" menu.
/// </summary>
public class TmEditorShellMobileActionsMaxVisibleTests : LocalizationTestBase
{
    private static IReadOnlyList<TmActionItem> FourActions =>
    [
        Action("back", "Back"),
        Action("redo", "Redo"),
        Action("preview", "Preview"),
        Action("validate", "Validate"),
    ];

    private static TmActionItem Action(string id, string label) => new()
    {
        Id = id,
        Label = label,
        OnClick = EventCallback.Factory.Create(new object(), () => { }),
    };

    private IRenderedComponent<TmEditorShell> RenderMobile(
        Action<ComponentParameterCollectionBuilder<TmEditorShell>>? extra = null)
        => Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Mobile);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.MobileActions, FourActions);
            extra?.Invoke(p);
        });

    [Fact]
    public void Default_KeepsTheBarBudgetAtThree_AndTheFourthActionInMore()
    {
        var cut = RenderMobile();

        cut.FindAll(".tm-mobile-action-bar__action")
            .Select(button => button.GetAttribute("data-action-id"))
            .Should().Equal("back", "redo", "preview");

        cut.Find(".tm-mobile-action-bar__more").Click();
        cut.FindAll("[role='menuitem']")
            .Select(item => item.GetAttribute("data-action-id"))
            .Should().Equal("validate");
    }

    [Fact]
    public void MaxVisibleFour_KeepsAllFourActionsOnTheBar_AndRendersNoMore()
    {
        var cut = RenderMobile(p => p.Add(x => x.MobileActionsMaxVisible, 4));

        cut.FindAll(".tm-mobile-action-bar__action")
            .Select(button => button.GetAttribute("data-action-id"))
            .Should().Equal("back", "redo", "preview", "validate");

        cut.FindAll(".tm-mobile-action-bar__more").Should().BeEmpty(
            "the four-slot budget leaves nothing to overflow");
        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void MaxVisibleBelowOne_IsRejected()
    {
        var render = () => RenderMobile(p => p.Add(x => x.MobileActionsMaxVisible, 0));

        render.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("MobileActionsMaxVisible");
    }
}
