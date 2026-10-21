using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
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
        // With stable buttons the focused node is the SAME element that existed before the
        // switch (its reference id never changes), so focus lands on the real new radio.
        var cut = RenderList(this);
        var expectedId = cut.Find(".tm-mvl-switch-card").GetAttribute("blazor:elementReference");

        cut.Find(".tm-mvl-switch-table").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        cut.Find(".tm-mvl-switch-card").GetAttribute("aria-checked").Should().Be("true");
        cut.FindAll(".tm-mvl-card").Should().NotBeEmpty();

        var focus = JSInterop.Invocations.Single(i => i.Identifier == "Blazor._internal.domWrapper.focus");
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(expectedId,
            "the focused reference is the stable card-radio element");
    }

    [Fact]
    public void Attach_WithADisconnectedCircuit_ThrowsNothing()
    {
        // Blazor Server: a render after the circuit drops must be swallowed, not rethrown
        // from OnAfterRenderAsync (JSDisconnectedException does not derive from JSException).
        JSInterop.SetupVoid("tmDataTable.attachArrowKeyPrevent", _ => true)
            .SetException(new Microsoft.JSInterop.JSDisconnectedException("circuit gone"));

        var render = () => RenderList(this);
        render.Should().NotThrow();
    }

    [Fact]
    public void Attach_RunsOnceAcrossRerenders()
    {
        var cut = RenderList(this);

        cut.Render(p => p.Add(c => c.SearchText, "a"));
        cut.Render(p => p.Add(c => c.SearchText, ""));

        JSInterop.Invocations.Count(i => i.Identifier == "tmDataTable.attachArrowKeyPrevent")
            .Should().Be(1, "the listener is attached only when the switcher element changes, not on every render");
    }

    [Fact]
    public void MouseClickSwitch_DoesNotMoveFocus()
    {
        // The radio buttons are stable (no @key recreation), so a mouse click on an inactive
        // view keeps the clicked button in the DOM — no focus call may fight the browser.
        var cut = RenderList(this);

        cut.Find(".tm-mvl-switch-card").Click();

        cut.Find(".tm-mvl-switch-card").GetAttribute("aria-checked").Should().Be("true");
        JSInterop.Invocations.Count(i => i.Identifier == "focus").Should().Be(0);
    }

    [Fact]
    public void HidingTheSwitcher_DetachesTheArrowKeyPrevent()
    {
        var cut = RenderList(this);

        cut.Render(p => p.Add(c => c.ShowViewSwitcher, false));

        JSInterop.VerifyInvoke("tmDataTable.detachArrowKeyPrevent", calledTimes: 1);
    }

    [Fact]
    public async Task Dispose_DetachesTheArrowKeyPrevent()
    {
        RenderList(this);

        await this.DisposeAsync();

        JSInterop.VerifyInvoke("tmDataTable.detachArrowKeyPrevent", calledTimes: 1);
    }
}