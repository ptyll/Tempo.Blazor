using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Dashboard;

/// <summary>
/// Fáze 7 (dashboard redesign): the create/delete dialogs and the dashboard switcher
/// ride the F2/F3 foundation (MobilePresentation=Auto, footer layout conventions)
/// instead of hand-rolled presentation.
/// </summary>
public class TmDashboardDialogsTests : LocalizationTestBase
{
    private static DashboardConfig Dashboard(string id, string name, bool isDefault = false) =>
        new() { Id = id, Name = name, IsDefault = isDefault, Widgets = [] };

    private (IDashboardProvider Provider, IWidgetRegistry Registry) SetupDashboard(params DashboardConfig[] all)
    {
        var provider = Substitute.For<IDashboardProvider>();
        provider.GetDashboardsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<DashboardConfig>>(all));
        provider.GetDefaultDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(all.FirstOrDefault(d => d.IsDefault)));
        provider.GetDashboardAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(all.FirstOrDefault(d => d.Id == ci.Arg<string>())));
        provider.SaveDashboardAsync(Arg.Any<DashboardConfig>(), Arg.Any<CancellationToken>())
                .Returns(ci => Task.FromResult(ci.Arg<DashboardConfig>()));
        Services.AddSingleton(provider);

        var registry = Substitute.For<IWidgetRegistry>();
        registry.GetWidget(Arg.Any<string>()).Returns((WidgetDefinition?)null);
        Services.AddSingleton(registry);
        return (provider, registry);
    }

    private void SetupSwitcherModules()
    {
        var overlay = JSInterop.SetupModule("./_content/Tempo.Blazor/js/overlay.js");
        overlay.SetupVoid("open", _ => true).SetVoidResult();
        overlay.SetupVoid("update", _ => true).SetVoidResult();
        overlay.SetupVoid("close", _ => true).SetVoidResult();
        var nav = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-menu-nav.js");
        nav.SetupVoid("attach", _ => true).SetVoidResult();
        nav.Setup<bool>("focusFirst", _ => true).SetResult(true);
    }

    private void OpenSwitcher(IRenderedComponent<TmDashboard> cut) =>
        cut.Find(".tm-dashboard-views button").Click();

    [Fact]
    public void Dashboard_CreateDialog_FooterInline_CancelSecondary_CreatePrimary()
    {
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true), Dashboard("d2", "Beta"));
        var cut = Render<TmDashboard>();

        OpenSwitcher(cut);
        cut.Find(".tm-dashboard-menu-item--action").Click();

        var footer = cut.Find(".tm-modal-footer");
        // The foundation footer convention: one row (the sheet presentation splits it 50/50).
        footer.ClassList.Should().Contain("tm-modal-footer--inline");
        var buttons = footer.QuerySelectorAll(".tm-btn").ToList();
        buttons.Should().HaveCount(2);
        buttons[0].ClassList.Should().Contain("tm-btn-secondary",
            "Cancel is the secondary/outlined action per the foundation footer convention");
        buttons[1].ClassList.Should().Contain("tm-btn-primary",
            "Create is the primary action of the dialog");
    }

    [Fact]
    public void Dashboard_CreateDialog_PlaceholderIsExample_NotDuplicateLabel()
    {
        UseCzechLocalization();
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true));
        var cut = Render<TmDashboard>();

        OpenSwitcher(cut);
        cut.Find(".tm-dashboard-menu-item--action").Click();

        var input = cut.Find(".tm-modal input");
        var placeholder = input.GetAttribute("placeholder");
        placeholder.Should().Be("např. Týmový přehled",
            "the placeholder must be a concrete example, not a repeat of the field label");
        placeholder.Should().NotBe(cut.Find(".tm-modal label, .tm-modal .tm-form-field-label").TextContent.Trim(),
            "the placeholder must not duplicate the visible label");
    }

    [Fact]
    public void Dashboard_DeleteDialog_UsesTrashIcon()
    {
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true), Dashboard("d2", "Beta"));
        var cut = Render<TmDashboard>();

        OpenSwitcher(cut);
        cut.Find(".tm-dashboard-menu-action--danger").Click();

        // The destructive confirm shows the trash-2 glyph (trash path of TmIcon), not the
        // generic confirm fallback. Stacked footer: Delete (ok) paints ABOVE Cancel.
        var icon = cut.Find(".tm-dialog-icon svg");
        icon.InnerHtml.Should().Contain("M19 6l-1 14", "the trash-2 icon path");
        var footer = cut.Find(".tm-dialog-footer");
        footer.ClassList.Should().Contain("tm-modal-footer--stacked");
        var buttons = footer.QuerySelectorAll(".tm-btn").ToList();
        buttons[0].ClassList.Should().Contain("tm-dialog-btn-cancel", "DOM order: cancel first…");
        buttons[1].ClassList.Should().Contain("tm-dialog-btn-ok", "…so column-reverse paints Delete on top on mobile");
    }

    [Fact]
    public void Dashboard_Switcher_MobilePresentationAuto()
    {
        UseCzechLocalization();
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true), Dashboard("d2", "Beta"));
        var cut = Render<TmDashboard>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Mobile);
            // F3: the popover-vs-sheet choice resolves from the viewport scope.
            p.AddCascadingValue(TmLayoutScopes.Viewport,
                new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile));
        });

        OpenSwitcher(cut);

        // F3: MobilePresentation=Auto presents the switcher as a bottom sheet titled
        // with the localized "Dashboards" (cs: "Nástěnky"), not the desktop popover.
        cut.FindAll(".tm-overlay-panel-sheet").Should().NotBeEmpty("the switcher presents as a sheet on mobile");
        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Nástěnky");
    }

    [Fact]
    public void Dashboard_DefaultDashboard_DeleteUnavailable_HasExplanation()
    {
        UseCzechLocalization();
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true), Dashboard("d2", "Beta"));
        var cut = Render<TmDashboard>();

        OpenSwitcher(cut);

        // The default row shows a readable "Výchozí" label AND explains why there is no
        // delete control — not just a bare star icon.
        var badge = cut.Find(".tm-dashboard-menu-default");
        badge.TextContent.Should().Contain("Výchozí");
        cut.Find(".tm-dashboard-menu-default-hint").TextContent.Should().Contain("nelze smazat",
            "the row explains why the default dashboard cannot be deleted");
        // And the default row truly has no delete control.
        cut.FindAll(".tm-dashboard-menu-action--danger").Should().HaveCount(1,
            "only the non-default dashboard offers delete");
    }

    [Fact]
    public void Dashboard_DefaultRow_SelectButton_ReferencesHintViaAriaDescribedBy()
    {
        UseCzechLocalization();
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true), Dashboard("d2", "Beta"));
        var cut = Render<TmDashboard>();

        OpenSwitcher(cut);

        // Screen readers moving through role=menu hear the hint only when the select
        // menuitem references it. The hint id is derived from the trigger's id.
        var triggerId = cut.Find(".tm-dashboard-switcher").GetAttribute("id");
        var select = cut.Find(".tm-dashboard-menu-item__select");
        select.GetAttribute("aria-describedby").Should().Be($"{triggerId}-default-hint",
            "the select menuitem announces the hint via aria-describedby");
        cut.Find(".tm-dashboard-menu-default-hint").GetAttribute("id").Should().Be($"{triggerId}-default-hint",
            "the hint carries the referenced id");
    }

    [Fact]
    public void Dashboard_NonDefaultRow_SelectButton_HasNoHintReference()
    {
        UseCzechLocalization();
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Alpha", isDefault: true), Dashboard("d2", "Beta"));
        var cut = Render<TmDashboard>();

        OpenSwitcher(cut);

        // Only the default row explains the missing delete — the other rows reference nothing.
        var selects = cut.FindAll(".tm-dashboard-menu-item__select").ToList();
        selects.Should().HaveCount(2);
        selects[1].GetAttribute("aria-describedby").Should().BeNull(
            "a non-default dashboard has no hint to describe it");
    }

    [Fact]
    public void Dashboard_DefaultRow_NameRemainsVisible_NextToHint()
    {
        UseCzechLocalization();
        SetupSwitcherModules();
        SetupDashboard(Dashboard("d1", "Main Dashboard", isDefault: true), Dashboard("d2", "Beta"));
        var cut = Render<TmDashboard>();

        OpenSwitcher(cut);

        // Regression guard: the hint moved OUT of the inline actions span (it used to
        // squeeze the name to zero width). The default row still renders its name text
        // and the hint lives as the row's own last child.
        var defaultRow = cut.FindAll(".tm-dashboard-menu-item")[0];
        defaultRow.QuerySelector(".tm-dashboard-menu-item__select span")!.TextContent
            .Should().Be("Main Dashboard", "the default row's name is not squeezed away by the hint");
        defaultRow.LastElementChild!.ClassList.Should().Contain("tm-dashboard-menu-default-hint",
            "the hint is the row's last child, outside the actions span");
    }
}
