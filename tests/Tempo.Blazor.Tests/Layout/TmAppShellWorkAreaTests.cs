using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// The resizable work area (plan 5d396592, phase 3): an <c>aside</c> on the right of the content with
/// a focusable ARIA separator — pointer drag through the shared <c>tm-panel-resize.js</c>, keyboard
/// through Blazor, both clamped to the work area's min/max and to <c>MinContentWidth</c>, a two-way
/// width, a double-click reset, and localStorage persistence of the user's choices only.
/// </summary>
public class TmAppShellWorkAreaTests : LocalizationTestBase
{
    private static string Module => TmAppShellTests.ResizeModule;

    private sealed class Mod
    {
        public required Func<IEnumerable<JSRuntimeInvocation>> Attach;
        public required Func<IEnumerable<JSRuntimeInvocation>> Detach;
        public required Func<IEnumerable<JSRuntimeInvocation>> Save;
        public required Func<IEnumerable<JSRuntimeInvocation>> Load;
        public JSRuntimeInvocationHandler<double>? Measure;
    }

    private Mod SetupModule(double content = 1200, string? stored = null, Exception? loadError = null, double measuredWidth = 420)
    {
        var module = JSInterop.SetupModule(Module);
        var measure = module.Setup<double>("measureRegion", _ => true);
        measure.SetResult(content);
        module.Setup<double>("measureWidth", _ => true).SetResult(measuredWidth);
        var attach = module.SetupVoid("attachResize", _ => true); attach.SetVoidResult();
        var detach = module.SetupVoid("detachResize", _ => true); detach.SetVoidResult();
        var save = module.SetupVoid("saveStored", _ => true); save.SetVoidResult();
        var load = module.Setup<string?>("loadStored", _ => true);
        if (loadError is not null) load.SetException(loadError); else load.SetResult(stored);
        return new Mod
        {
            Attach = () => attach.Invocations,
            Detach = () => detach.Invocations,
            Save = () => save.Invocations,
            Load = () => load.Invocations,
            Measure = measure,
        };
    }

    private IRenderedComponent<TmAppShell> RenderShell(
        TmLayoutMode mode = TmLayoutMode.Desktop,
        bool workAreaOpen = true,
        Action<ComponentParameterCollectionBuilder<TmAppShell>>? extra = null)
        => Render<TmAppShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.Title, "Acme");
            p.Add(x => x.NavItems, TmAppShellTests.DefaultItems);
            p.Add(x => x.WorkArea, b => b.AddContent(0, "Sources"));
            p.Add(x => x.WorkAreaOpen, workAreaOpen);
            extra?.Invoke(p);
        });

    private static string WorkAreaCss(IRenderedComponent<TmAppShell> cut)
        => cut.Find("aside.tm-app-shell__work-area").GetAttribute("style")!;

    private static AngleSharp.Dom.IElement Separator(IRenderedComponent<TmAppShell> cut)
        => cut.Find("[role='separator']");

    // ── The separator's ARIA contract ───────────────────────────────────────

    [Fact]
    public void ClosedWorkArea_RendersNoAsideAndNoSeparator()
    {
        SetupModule();
        var cut = RenderShell(workAreaOpen: false);

        cut.FindAll("aside").Should().BeEmpty();
        cut.FindAll("[role='separator']").Should().BeEmpty();
    }

    [Fact]
    public void Separator_CarriesTheFullAriaContract()
    {
        SetupModule();
        var cut = RenderShell();

        var separator = Separator(cut);
        separator.GetAttribute("aria-orientation").Should().Be("vertical");
        separator.GetAttribute("aria-valuenow").Should().Be("420");
        separator.GetAttribute("aria-valuemin").Should().Be("280");
        separator.GetAttribute("aria-valuemax").Should().Be("640");
        separator.GetAttribute("aria-label").Should().Be("Resize work area");
        separator.GetAttribute("tabindex").Should().Be("0");
        separator.GetAttribute("aria-controls").Should().Be(cut.Find("aside.tm-app-shell__work-area").Id);
    }

    [Fact]
    public void WorkArea_IsNamedByItsVisibleHeading()
    {
        SetupModule();
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaLabel, "Sources and tools"));

        var aside = cut.Find("aside");
        var labelledBy = aside.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace("a complementary region needs an accessible name");
        cut.Find("#" + labelledBy).TextContent.Trim().Should().Be("Sources and tools");
        cut.Find("#" + labelledBy).TagName.Should().Be("H2", "the region heading is part of the page outline");
    }

    [Fact]
    public void WorkArea_CloseButton_RaisesTheTwoWayOpenCallback()
    {
        SetupModule();
        var raised = new List<bool>();
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaOpenChanged,
            EventCallback.Factory.Create<bool>(this, v => raised.Add(v))));

        var close = cut.Find(".tm-app-shell__work-area-close");
        close.GetAttribute("aria-label").Should().Be("Close work area");
        close.GetAttribute("type").Should().Be("button");
        close.Click();

        raised.Should().Equal(new[] { false });
    }

    [Fact]
    public void WorkArea_WithoutACloseHandler_RendersNoCloseButton()
    {
        // A button that cannot close anything is worse than no button — the library's rule for the
        // account menu's sign out line applies to the region's close control too.
        SetupModule();
        var cut = RenderShell();

        cut.Find(".tm-app-shell__work-area-title").Should().NotBeNull();
        cut.FindAll(".tm-app-shell__work-area-close").Should().BeEmpty();
    }

    [Fact]
    public void WorkArea_ClosedByItsOwnButton_MovesTheFocusToTheContent()
    {
        // The focused close button unmounts with the region: leaving the focus on the document body
        // would drop a keyboard user out of the page (WCAG 2.4.3).
        var mod = SetupModule();
        var open = true;
        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.WorkAreaOpenChanged, EventCallback.Factory.Create<bool>(this, v => open = v));
        });

        // The shell registers the content region as the resize region: that ElementReference is the
        // very element the focus has to land on (a render clears the element's serialized attribute).
        cut.WaitForAssertion(() => mod.Attach().Should().NotBeEmpty());
        var contentReference = mod.Attach().Single().Arguments[2] is ElementReference region ? region.Id : null;
        contentReference.Should().NotBeNullOrWhiteSpace();

        cut.Find(".tm-app-shell__work-area-close").Click();
        open.Should().BeFalse("the shell reports the close to the host");

        // The host echoes it: the region unmounts.
        cut.Render(p => p.Add(x => x.WorkAreaOpen, open));

        cut.WaitForAssertion(() => cut.FindAll("aside").Should().BeEmpty());
        var focused = JSInterop.Invocations
            .Where(i => i.Identifier == "Blazor._internal.domWrapper.focus")
            .Select(i => i.Arguments[0] is ElementReference reference ? reference.Id : i.Arguments[0]?.ToString())
            .ToList();
        focused.Should().NotBeEmpty("the focus leaves the region that just unmounted");
        focused.Should().Contain(contentReference, "it lands on the content region — the part of the frame that stays");
    }

    // ── The separator's ARIA contract ───────────────────────────────────────

    [Fact]
    public void WorkArea_DefaultsToTheLocalizedName_AndRendersAtItsWidth()
    {
        SetupModule();
        var cut = RenderShell();

        cut.Find(".tm-app-shell__work-area-title").TextContent.Trim().Should().Be("Work area");
        WorkAreaCss(cut).Should().Contain("--tm-app-shell-work-area-width: 420px");
        cut.Find("aside").TextContent.Should().Contain("Sources");
    }

    [Fact]
    public void Separator_SitsOnTheWorkAreasInnerEdge()
    {
        SetupModule();
        var cut = RenderShell();

        Separator(cut).NextElementSibling!.TagName.Should().Be("ASIDE",
            "the work area is the last column: its separator is on its left edge");
    }

    // ── Keyboard (Blazor) ───────────────────────────────────────────────────

    [Fact]
    public void Keyboard_ArrowLeftGrowsTheWorkArea_ArrowRightShrinksIt()
    {
        SetupModule();
        var raised = new List<int>();
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaWidthChanged,
            EventCallback.Factory.Create<int>(this, v => raised.Add(v))));

        Separator(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        raised.Should().Equal(436);

        Separator(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        raised.Should().Equal(436, 420);
    }

    [Fact]
    public void Keyboard_ShiftStepsBy64()
    {
        SetupModule();
        var raised = new List<int>();
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaWidthChanged,
            EventCallback.Factory.Create<int>(this, v => raised.Add(v))));

        Separator(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowLeft", ShiftKey = true });

        raised.Should().Equal(484);
    }

    [Fact]
    public void Keyboard_HomeAndEndGoToTheMinimumAndMaximum()
    {
        SetupModule();
        var raised = new List<int>();
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaWidthChanged,
            EventCallback.Factory.Create<int>(this, v => raised.Add(v))));

        Separator(cut).KeyDown(new KeyboardEventArgs { Key = "End" });
        Separator(cut).KeyDown(new KeyboardEventArgs { Key = "Home" });

        raised.Should().Equal(new[] { 640, 280 }, "with room in the content, the configured bounds are reachable");
    }

    [Fact]
    public void Keyboard_NeverTakesTheContentBelowMinContentWidth()
    {
        SetupModule(content: 560);
        var raised = new List<int>();
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaWidthChanged,
            EventCallback.Factory.Create<int>(this, v => raised.Add(v))));

        cut.WaitForAssertion(() => Separator(cut).GetAttribute("aria-valuemax").Should().Be("500",
            "560px of content minus the 480px minimum leaves 80px of room on top of the current 420"));
        Separator(cut).KeyDown(new KeyboardEventArgs { Key = "End" });

        raised.Should().Equal(new[] { 500 }, "the clamp stops where the content reaches its minimum");
    }

    [Fact]
    public void Keyboard_OtherKeysDoNothing()
    {
        SetupModule();
        var raised = false;
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaWidthChanged,
            EventCallback.Factory.Create<int>(this, _ => raised = true)));

        Separator(cut).KeyDown(new KeyboardEventArgs { Key = "a" });
        Separator(cut).KeyDown(new KeyboardEventArgs { Key = "Tab" });

        raised.Should().BeFalse();
    }

    [Fact]
    public void DoubleClick_ResetsTheWidthToTheHostWidth()
    {
        SetupModule();
        var raised = new List<int>();
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaWidthChanged,
            EventCallback.Factory.Create<int>(this, v => raised.Add(v))));

        Separator(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        raised.Should().Equal(436);

        Separator(cut).DoubleClick();

        raised.Should().Equal(new[] { 436, 420 }, "a double click is the non-drag reset to the width the host supplied");
        WorkAreaCss(cut).Should().Contain("--tm-app-shell-work-area-width: 420px");
    }

    // ── Pointer commit (called by the JS module) ────────────────────────────

    [Fact]
    public async Task ResizeCommitted_RaisesTheTwoWayWidthAndReRenders()
    {
        SetupModule();
        var raised = new List<int>();
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaWidthChanged,
            EventCallback.Factory.Create<int>(this, v => raised.Add(v))));

        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("right", 500));

        raised.Should().Equal(500);
        WorkAreaCss(cut).Should().Contain("--tm-app-shell-work-area-width: 500px");
        Separator(cut).GetAttribute("aria-valuenow").Should().Be("500");
    }

    [Fact]
    public async Task ResizeCommitted_IsClampedAgain_AndIgnoresAnotherSide()
    {
        SetupModule();
        var raised = new List<int>();
        var cut = RenderShell(extra: p => p.Add(x => x.WorkAreaWidthChanged,
            EventCallback.Factory.Create<int>(this, v => raised.Add(v))));

        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("right", 9000));
        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("right", -4));
        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("left", 500));

        raised.Should().Equal(640, 280);
    }

    [Fact]
    public void RenderedWidth_IsBoundedByTheConfiguredBounds()
    {
        SetupModule();
        var low = RenderShell(extra: p => p.Add(x => x.WorkAreaWidth, 100));
        WorkAreaCss(low).Should().Contain("--tm-app-shell-work-area-width: 280px");

        var high = RenderShell(extra: p => p.Add(x => x.WorkAreaWidth, 5000));
        WorkAreaCss(high).Should().Contain("--tm-app-shell-work-area-width: 640px");
    }

    [Fact]
    public void Separator_AttachesThePointerResize_AndDetachesWhenTheWorkAreaCloses()
    {
        var mod = SetupModule();
        var cut = RenderShell();

        cut.WaitForAssertion(() => mod.Attach().Should().HaveCount(1));
        var options = System.Text.Json.JsonSerializer.Serialize(mod.Attach().Single().Arguments[5]);
        options.Should().Contain("\"side\":\"right\"")
            .And.Contain("\"min\":280")
            .And.Contain("\"max\":640")
            .And.Contain("\"minRegion\":480")
            .And.Contain("--tm-app-shell-work-area-width");

        cut.Render(p => p.Add(x => x.WorkAreaOpen, false));

        cut.WaitForAssertion(() => mod.Detach().Should().NotBeEmpty());
    }

    [Fact]
    public void Separator_IsNotAttachedOnAMobileLayout()
    {
        var mod = SetupModule();
        RenderShell(TmLayoutMode.Mobile);

        mod.Attach().Should().BeEmpty("a phone has no room for a dragged work area");
    }

    [Fact]
    public void TwoShellsOnOnePage_RegisterDistinctResizerIds()
    {
        var mod = SetupModule();
        var first = RenderShell();
        var second = RenderShell();

        first.WaitForAssertion(() => mod.Attach().Should().HaveCount(2));
        mod.Attach().Select(i => (string)i.Arguments[4]!).Should().OnlyHaveUniqueItems();
    }

    // ── Persistence ─────────────────────────────────────────────────────────

    private static string Stored(int width, int @base, bool? collapsed = null)
        => $"{{\"workArea\":{{\"w\":{width},\"base\":\"{@base}px\"}}"
           + (collapsed is null ? string.Empty : $",\"collapsed\":{(collapsed.Value ? "true" : "false")}") + "}";

    [Fact]
    public void Persist_AppliesAValidStoredWidthAndCollapsedStateOnLoad()
    {
        var mod = SetupModule(stored: Stored(520, 420, collapsed: true));
        var raisedWidth = new List<int>();
        var raisedCollapsed = new List<bool>();

        var cut = RenderShell(extra: p =>
        {
            p.Add(x => x.PersistKey, "demo");
            p.Add(x => x.WorkAreaWidthChanged, EventCallback.Factory.Create<int>(this, v => raisedWidth.Add(v)));
            p.Add(x => x.SidebarCollapsedChanged, EventCallback.Factory.Create<bool>(this, v => raisedCollapsed.Add(v)));
        });

        cut.WaitForAssertion(() => WorkAreaCss(cut).Should().Contain("520px"));
        cut.Find(".tm-sidebar").ClassList.Should().Contain("tm-sidebar-collapsed");
        raisedWidth.Should().Equal(new[] { 520 }, "a restore counts as a user value: a bound host learns about it");
        raisedCollapsed.Should().Equal(new[] { true });
        mod.Load().Single().Arguments[0].Should().Be("tempo.tm-app-shell.");
        mod.Load().Single().Arguments[1].Should().Be("demo");
    }

    [Theory]
    [InlineData("{\"workArea\":{\"w\":100,\"base\":\"420px\"}}", "below the minimum")]
    [InlineData("{\"workArea\":{\"w\":9999,\"base\":\"420px\"}}", "above the maximum")]
    [InlineData("{\"workArea\":{\"w\":520,\"base\":\"999px\"}}", "the host supplies another width now")]
    [InlineData("{\"workArea\":{\"w\":\"520px\",\"base\":\"420px\"}}", "a raw string")]
    [InlineData("{\"workArea\":{\"w\":520}}", "no base")]
    [InlineData("not json", "malformed")]
    [InlineData("{\"collapsed\":\"yes\"}", "a non-boolean collapsed flag")]
    public void Persist_IgnoresInvalidOrStaleStoredValues(string stored, string why)
    {
        SetupModule(stored: stored);
        var cut = RenderShell(extra: p => p.Add(x => x.PersistKey, "demo"));

        cut.WaitForAssertion(() => WorkAreaCss(cut).Should().Contain("420px"));
        cut.Find(".tm-sidebar").ClassList.Should().Contain("tm-sidebar-expanded");
    }

    [Fact]
    public void Persist_AJsFailureOnLoad_KeepsTheParameterValues()
    {
        SetupModule(loadError: new JSException("SecurityError"));
        var cut = RenderShell(extra: p => p.Add(x => x.PersistKey, "demo"));

        cut.WaitForAssertion(() => WorkAreaCss(cut).Should().Contain("420px"));
    }

    [Fact]
    public async Task Persist_SavesOnlyWhatTheUserChanged()
    {
        var mod = SetupModule();
        var cut = RenderShell(extra: p => p.Add(x => x.PersistKey, "demo"));
        cut.WaitForAssertion(() => mod.Load().Should().HaveCount(1));

        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("right", 520));

        var payload = (string?)mod.Save().Last().Arguments[2];
        payload.Should().NotBeNull();
        payload.Should().Contain("\"w\":520").And.Contain("\"base\":\"420px\"");
    }

    [Fact]
    public void Persist_SavesTheCollapsedChoice()
    {
        var mod = SetupModule();
        var cut = RenderShell(extra: p => p.Add(x => x.PersistKey, "demo"));

        cut.Find(".tm-sidebar-toggle").Click();

        cut.WaitForAssertion(() => mod.Save().Should().NotBeEmpty());
        ((string?)mod.Save().Last().Arguments[2]).Should().Contain("\"collapsed\":true");
    }

    [Fact]
    public void Persist_SavesTheCollapsedChoice_WhenTheBoundHostWritesTheValueBack()
    {
        var mod = SetupModule();
        var bound = new List<bool>();
        IRenderedComponent<TmAppShell>? cut = null;
        cut = RenderShell(extra: p =>
        {
            p.Add(x => x.PersistKey, "demo");
            p.Add(x => x.SidebarCollapsed, false);
            p.Add(x => x.SidebarCollapsedChanged, EventCallback.Factory.Create<bool>(this, value =>
            {
                // @bind-SidebarCollapsed: the host stores the value and re-renders the shell with it, so
                // the parameter equals the user's choice by the time the shell persists.
                bound.Add(value);
                cut!.Render(inner => inner.Add(x => x.SidebarCollapsed, value));
            }));
        });

        cut.Find(".tm-sidebar-toggle").Click();

        bound.Should().Equal(new[] { true }, "the toggle raises the two-way callback");
        cut.WaitForAssertion(() => mod.Save().Should().NotBeEmpty());
        ((string?)mod.Save().Last().Arguments[2]).Should().Contain("\"collapsed\":true",
            "a bound host echoes the choice back into the parameter: what persists is the USER's intent, not the parameter diff");
    }

    [Fact]
    public async Task Persist_KeepsTheRestoredCollapsedChoice_WhenTheUserResizesLater()
    {
        var mod = SetupModule(stored: Stored(520, 420, collapsed: true));
        var cut = RenderShell(extra: p => p.Add(x => x.PersistKey, "demo"));
        cut.WaitForAssertion(() => WorkAreaCss(cut).Should().Contain("520px"));

        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("right", 480));

        var payload = (string?)mod.Save().Last().Arguments[2];
        payload.Should().Contain("\"w\":480").And.Contain("\"collapsed\":true",
            "a restored value is the user's own earlier choice: the next resize must not erase it");
    }

    [Fact]
    public async Task Persist_NoKey_NeverTouchesStorage()
    {
        var mod = SetupModule();
        var cut = RenderShell();

        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("right", 500));
        cut.Find(".tm-sidebar-toggle").Click();

        mod.Load().Should().BeEmpty();
        mod.Save().Should().BeEmpty();
    }

    [Fact]
    public void Persist_AKeySetAfterTheFirstRender_IsLoadedWhenItAppears()
    {
        var mod = SetupModule(stored: Stored(520, 420));
        var cut = RenderShell();
        mod.Load().Should().BeEmpty("no key yet");

        cut.Render(p => p.Add(x => x.PersistKey, "late"));

        cut.WaitForAssertion(() => mod.Load().Should().HaveCount(1));
        cut.WaitForAssertion(() => WorkAreaCss(cut).Should().Contain("520px"));
    }

    [Fact]
    public async Task Persist_ResizingBackToTheHostWidth_RemovesTheStoredOverride()
    {
        var mod = SetupModule();
        var cut = RenderShell(extra: p => p.Add(x => x.PersistKey, "demo"));
        cut.WaitForAssertion(() => mod.Load().Should().HaveCount(1));
        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("right", 520));
        ((string?)mod.Save().Last().Arguments[2]).Should().Contain("workArea");

        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("right", 420));

        ((string?)mod.Save().Last().Arguments[2]).Should().BeNull(
            "the user is back on the host width: nothing is left to store");
    }

    [Fact]
    public async Task AnExplicitHostWidthAfterAUserResize_Wins()
    {
        var mod = SetupModule();
        var cut = RenderShell(extra: p => p.Add(x => x.PersistKey, "demo"));
        cut.WaitForAssertion(() => mod.Load().Should().HaveCount(1));
        await cut.InvokeAsync(() => cut.Instance.HandleWorkAreaResizeCommitted("right", 520));

        cut.Render(p => p.Add(x => x.WorkAreaWidth, 300));

        WorkAreaCss(cut).Should().Contain("300px");
        ((string?)mod.Save().Last().Arguments[2]).Should().BeNull("the stale user width is removed from storage");
    }

    [Fact]
    public async Task Disposal_ThenACommit_DoesNotThrow()
    {
        SetupModule();
        var cut = RenderShell(extra: p => p.Add(x => x.PersistKey, "demo"));

        await cut.Instance.DisposeAsync();

        var act = async () => await cut.Instance.HandleWorkAreaResizeCommitted("right", 500);
        await act.Should().NotThrowAsync();
    }

    // ── The pure rules shared with the JS module ────────────────────────────

    [Theory]
    [InlineData(100, 280, 200, 480, 1000, 400, 200)]
    [InlineData(900, 280, 200, 480, 5000, 400, 480)]
    [InlineData(300, 280, 200, 480, 5000, 400, 300)]
    [InlineData(380, 280, 200, 480, 410, 400, 290)]
    [InlineData(296, 280, 200, 480, 300, 400, 280)]
    [InlineData(264, 280, 200, 480, 300, 400, 264)]
    [InlineData(470, 280, 200, 480, double.NaN, 400, 470)]
    [InlineData(300.6, 280, 200, 480, 5000, 400, 301)]
    [InlineData(299.4, 280, 200, 480, 5000, 400, 299)]
    public void Clamp_MatchesTheJsVectors(double requested, double current, int min, int max, double content, int minContent, double expected)
    {
        PanelResize.Clamp(requested, current, min, max, content, minContent).Should().Be(expected);
    }

    [Fact]
    public void StoredLayout_RoundTrips_AndOnlyCarriesWhatChanged()
    {
        var json = AppShellLayoutState.Serialize(new StoredWidth(520, "420px"), true);

        json.Should().NotBeNull();
        json.Should().Contain("\"workArea\"").And.Contain("\"collapsed\":true");
        var (workArea, collapsed) = AppShellLayoutState.Parse(json);
        workArea.Should().Be(new StoredWidth(520, "420px"));
        collapsed.Should().BeTrue();

        AppShellLayoutState.Serialize(null, null).Should().BeNull("nothing user-set: nothing stored");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"workArea\":\"520px\"}")]
    [InlineData("{\"workArea\":{\"w\":520.5,\"base\":\"420px\"}}")]
    [InlineData("{\"workArea\":{\"w\":520,\"base\":5}}")]
    [InlineData("{\"collapsed\":1}")]
    public void Parse_IgnoresMalformedOrLegacyPayloads(string? json)
    {
        var (workArea, collapsed) = AppShellLayoutState.Parse(json);

        workArea.Should().BeNull();
        collapsed.Should().BeNull();
    }
}
