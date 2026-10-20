using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Phase 5.4: the view switcher is a radiogroup with one roving tab stop. Arrow keys move the
/// view and the focus; the group is named "View", not after the table option.
/// </summary>
public class TmMultiViewListViewSwitcherTests : LocalizationTestBase
{
    private record Item(string Id, string Title);

    private static IRenderedComponent<TmMultiViewList<Item>> RenderList(
        BunitContext context, ListViewMode mode = ListViewMode.Table)
        => context.Render<TmMultiViewList<Item>>(p => p
            .Add(c => c.Items, new[] { new Item("1", "Ada") })
            .Add(c => c.TitleField, (Func<Item, string>)(i => i.Title))
            .Add(c => c.ViewMode, mode)
            .Add(c => c.ShowViewSwitcher, true));

    [Fact]
    public void ViewSwitcher_OnlyTheCheckedRadioIsTabbable()
    {
        var cut = RenderList(this);

        var group = cut.Find(".tm-mvl-switcher");
        group.GetAttribute("role").Should().Be("radiogroup");
        group.GetAttribute("aria-label").Should().Be("View");

        var radios = cut.FindAll(".tm-mvl-switcher [role='radio']");
        radios.Should().HaveCount(3);
        radios.Single(r => r.GetAttribute("aria-checked") == "true").GetAttribute("tabindex").Should().Be("0");
        radios.Where(r => r.GetAttribute("aria-checked") != "true")
            .Should().OnlyContain(r => r.GetAttribute("tabindex") == "-1");
    }

    [Fact]
    public void ArrowRight_SwitchesViewAndFocusesTheNewRadio()
    {
        var cut = RenderList(this);

        cut.Find(".tm-mvl-switch-table").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.Find(".tm-mvl-switch-card").GetAttribute("aria-checked").Should().Be("true");
        cut.FindAll(".tm-mvl-card").Should().NotBeEmpty();

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0]
            .ShouldBeElementReferenceTo(cut.Find(".tm-mvl-switch-card"));
    }
}
