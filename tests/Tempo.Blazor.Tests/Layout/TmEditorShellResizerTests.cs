using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// F6 review round 1, decision Q4=B: a resize separator on each docked panel's inner edge
/// (role=separator, pointer drag in tm-editor-shell.js, keyboard in Blazor), clamped to the
/// Min/Max widths and MinCanvasWidth, two-way widths, and localStorage persistence of ONLY the
/// user-resized widths with validated reads and a host-wins precedence rule.
/// </summary>
public class TmEditorShellResizerTests : LocalizationTestBase
{
    private const string ShellModule = "./_content/Tempo.Blazor/js/tm-editor-shell.js";

    private sealed class Mod
    {
        public Func<IEnumerable<JSRuntimeInvocation>> Attach = null!;
        public Func<IEnumerable<JSRuntimeInvocation>> Detach = null!;
        public Func<IEnumerable<JSRuntimeInvocation>> Save = null!;
        public Func<IEnumerable<JSRuntimeInvocation>> Load = null!;
    }

    private Mod Module(double canvas = 700, string? stored = null, Exception? loadError = null)
    {
        var module = JSInterop.SetupModule(ShellModule);
        module.Setup<double>("measureCanvas", _ => true).SetResult(canvas);
        var attach = module.SetupVoid("attachResize", _ => true); attach.SetVoidResult();
        var detach = module.SetupVoid("detachResize", _ => true); detach.SetVoidResult();
        var save = module.SetupVoid("savePanelWidths", _ => true); save.SetVoidResult();
        var load = module.Setup<string?>("loadPanelWidths", _ => true);
        if (loadError is not null) load.SetException(loadError); else load.SetResult(stored);
        return new Mod
        {
            Attach = () => attach.Invocations,
            Detach = () => detach.Invocations,
            Save = () => save.Invocations,
            Load = () => load.Invocations,
        };
    }
    private IRenderedComponent<TmEditorShell> RenderShell(
        TmLayoutMode mode = TmLayoutMode.Desktop,
        Action<ComponentParameterCollectionBuilder<TmEditorShell>>? extra = null)
    {
        return Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Left, b => b.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, b => b.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, b => b.AddContent(0, "Right props"));
            extra?.Invoke(p);
        });
    }

    private static string Width(IRenderedComponent<TmEditorShell> cut, string side)
        => cut.Find($"aside[data-region='{side}']").GetAttribute("style")!;

    // ── The pure functions shared with the JS module ────────────────────────

    [Theory]
    [InlineData(100, 280, 200, 480, 1000, 400, 200)]
    [InlineData(900, 280, 200, 480, 5000, 400, 480)]
    [InlineData(300, 280, 200, 480, 5000, 400, 300)]
    [InlineData(380, 280, 200, 480, 410, 400, 290)]
    [InlineData(296, 280, 200, 480, 300, 400, 280)]
    [InlineData(264, 280, 200, 480, 300, 400, 264)]
    [InlineData(470, 280, 200, 480, double.NaN, 400, 470)]
    [InlineData(300.6, 280, 200, 480, 5000, 400, 301)]
    public void Clamp_MatchesTheJsVectors(double requested, double current, int min, int max, double canvas, int minCanvas, double expected)
    {
        EditorShellResize.Clamp(requested, current, min, max, canvas, minCanvas).Should().Be(expected);
    }

    [Theory]
    [InlineData("280px", 280)]
    [InlineData(" 320px ", 320)]
    [InlineData("320.5px", 321)]
    public void TryParsePx_AcceptsPlainPixelLengths(string value, int expected)
    {
        EditorShellResize.TryParsePx(value, out var px).Should().BeTrue();
        px.Should().Be(expected);
    }

    [Theory]
    [InlineData("20rem")]
    [InlineData("50%")]
    [InlineData("calc(1px + 2px)")]
    [InlineData("280px; background:red")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("-5px")]
    public void TryParsePx_RejectsAnythingElse(string? value)
    {
        EditorShellResize.TryParsePx(value, out _).Should().BeFalse();
    }

    [Fact]
    public void StoredWidths_RoundTrip_AndOnlyContainWhatTheUserResized()
    {
        var json = EditorShellWidths.Serialize(new StoredWidth(300, "280px"), null);

        json.Should().NotBeNull();
        json.Should().NotContain("right");
        var (left, right) = EditorShellWidths.Parse(json);
        left.Should().Be(new StoredWidth(300, "280px"));
        right.Should().BeNull();

        EditorShellWidths.Serialize(null, null).Should().BeNull("nothing user-resized: nothing stored");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"left\":\"280px\"}")]
    [InlineData("{\"left\":{\"w\":\"300px\",\"base\":\"280px\"}}")]
    [InlineData("{\"left\":{\"w\":300.5,\"base\":\"280px\"}}")]
    [InlineData("{\"left\":{\"w\":300}}")]
    [InlineData("{\"left\":{\"w\":300,\"base\":5}}")]
    [InlineData("{\"left\":null,\"right\":[]}")]
    public void Parse_IgnoresMalformedOrLegacyPayloads(string? json)
    {
        var (left, right) = EditorShellWidths.Parse(json);

        left.Should().BeNull();
        right.Should().BeNull();
    }

    // ── The separator ───────────────────────────────────────────────────────

    [Fact]
    public void Desktop_RendersASeparatorOnEachDockedPanel_WithTheFullAriaContract()
    {
        Module();
        var cut = RenderShell();

        var left = cut.Find("[role='separator'][data-side='left']");
        left.GetAttribute("aria-orientation").Should().Be("vertical");
        left.GetAttribute("aria-valuenow").Should().Be("280");
        left.GetAttribute("aria-valuemin").Should().Be("200");
        left.GetAttribute("aria-valuemax").Should().Be("480");
        left.GetAttribute("aria-label").Should().Be("Resize left panel");
        left.GetAttribute("tabindex").Should().Be("0");
        left.GetAttribute("aria-controls").Should().Be(cut.Find("aside[data-region='left']").Id);

        var right = cut.Find("[role='separator'][data-side='right']");
        right.GetAttribute("aria-valuenow").Should().Be("320");
        right.GetAttribute("aria-valuemin").Should().Be("240");
        right.GetAttribute("aria-valuemax").Should().Be("560");
        right.GetAttribute("aria-label").Should().Be("Resize right panel");
    }

    [Fact]
    public void Separator_SitsOnThePanelsInnerEdge()
    {
        Module();
        var cut = RenderShell();

        var main = cut.Find("[data-region='main']");
        main.Children.First(c => c.GetAttribute("role") == "separator" && c.GetAttribute("data-side") == "left")
            .PreviousElementSibling!.TagName.Should().Be("ASIDE", "the left separator touches the left panel's inner (right) edge");
        main.Children.First(c => c.GetAttribute("role") == "separator" && c.GetAttribute("data-side") == "right")
            .NextElementSibling!.TagName.Should().Be("ASIDE", "the right separator touches the right panel's inner (left) edge");
    }
    [Fact]
    public void NoSeparator_OnARail_WhenHidden_OrOnMobile()
    {
        Module();
        var rail = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.CollapsedPanels, EditorShellPanel.Left));
        rail.FindAll("[role='separator'][data-side='left']").Should().BeEmpty("a rail has nothing to resize");
        rail.FindAll("[role='separator'][data-side='right']").Should().HaveCount(1);

        var hidden = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.RightOpen, false));
        hidden.FindAll("[role='separator'][data-side='right']").Should().BeEmpty();

        var mobile = RenderShell(TmLayoutMode.Mobile);
        mobile.FindAll("[role='separator']").Should().BeEmpty("the mobile sheet is not resizable by width");
    }

    [Fact]
    public void Tablet_RendersASeparatorOnTheOneExpandedPanelOnly()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Tablet);

        cut.FindAll("[role='separator']").Should().HaveCount(1);
        cut.Find("[role='separator']").GetAttribute("data-side").Should().Be("right");
    }

    // ── Keyboard (Blazor) ───────────────────────────────────────────────────

    [Fact]
    public void Keyboard_ArrowRightGrowsTheLeftPanel_ByStepAndShiftStep()
    {
        Module();
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.LeftWidthChanged,
            EventCallback.Factory.Create<string>(this, v => raised.Add(v))));

        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });

        raised.Should().Equal("296px", "360px");
        Width(cut, "left").Should().Contain("360px");
        cut.Find("[role='separator'][data-side='left']").GetAttribute("aria-valuenow").Should().Be("360");
    }

    [Fact]
    public void Keyboard_ArrowLeftShrinksTheLeftPanel_AndStopsAtTheMinimum()
    {
        Module();
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Desktop, p =>
        {
            p.Add(x => x.LeftWidth, "208px");
            p.Add(x => x.LeftWidthChanged, EventCallback.Factory.Create<string>(this, v => raised.Add(v)));
        });

        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        raised.Should().Equal("200px");

        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        raised.Should().Equal(new[] { "200px" }, "already at the minimum: no change, no callback");
    }

    [Fact]
    public void Keyboard_RightPanel_ArrowLeftGrows_BecauseItsSeparatorIsOnTheLeftEdge()
    {
        Module();
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.RightWidthChanged,
            EventCallback.Factory.Create<string>(this, v => raised.Add(v))));

        cut.Find("[role='separator'][data-side='right']").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        cut.Find("[role='separator'][data-side='right']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        raised.Should().Equal("336px", "320px");
    }

    [Fact]
    public void Keyboard_HomeAndEnd_GoToMinAndMax_ClampedByTheCanvas()
    {
        Module(canvas: 5000);
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.LeftWidthChanged,
            EventCallback.Factory.Create<string>(this, v => raised.Add(v))));

        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "End" });
        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "Home" });

        raised.Should().Equal("480px", "200px");
    }

    [Fact]
    public void Keyboard_NeverTakesTheCanvasBelowMinCanvasWidth()
    {
        Module(canvas: 450);
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.LeftWidthChanged,
            EventCallback.Factory.Create<string>(this, v => raised.Add(v))));

        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "End" });

        raised.Should().Equal(new[] { "330px" }, "450 - 400 = 50px of room, not the 200px up to the maximum");
    }

    [Fact]
    public void Keyboard_OtherKeysDoNothing()
    {
        Module();
        var raised = false;
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.LeftWidthChanged,
            EventCallback.Factory.Create<string>(this, _ => raised = true)));

        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "a" });
        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        raised.Should().BeFalse();
    }

    // ── Pointer commit (called by the JS module) ────────────────────────────

    [Fact]
    public async Task ResizeCommitted_UpdatesTheEffectiveWidth_AndRaisesTheTwoWayCallback()
    {
        Module();
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.RightWidthChanged,
            EventCallback.Factory.Create<string>(this, v => raised.Add(v))));

        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("right", 400));

        raised.Should().Equal("400px");
        Width(cut, "right").Should().Contain("400px");
    }

    [Fact]
    public async Task ResizeCommitted_IsClampedAndValidatedAgain()
    {
        Module();
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.LeftWidthChanged,
            EventCallback.Factory.Create<string>(this, v => raised.Add(v))));

        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("left", 9000));
        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("left", -4));
        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("middle", 300));

        raised.Should().Equal("480px", "200px");
    }

    [Fact]
    public void Separator_AttachesThePointerResize_AndDetachesWhenThePanelCollapses()
    {
        var module = Module();
        var cut = RenderShell();

        cut.WaitForAssertion(() => module.Attach().Should().HaveCount(2, "one per docked panel"));

        cut.Render(p => p.Add(x => x.CollapsedPanels, EditorShellPanel.Left));

        cut.WaitForAssertion(() => module.Detach().Should().NotBeEmpty());
    }

    [Fact]
    public void TwoShellsOnOnePage_RegisterDistinctResizerIds()
    {
        // The module keeps its registrations in one map: two shells that both used "left" would
        // detach each other (the second silently killed the first shell's drag).
        var module = Module();
        var first = RenderShell();
        var second = RenderShell();
        first.WaitForAssertion(() => module.Attach().Should().HaveCount(4));

        var ids = module.Attach().Select(i => (string)i.Arguments[4]!).ToList();

        ids.Should().OnlyHaveUniqueItems("every shell instance owns its own registrations");
    }

    [Fact]
    public void Separator_ReattachOnlyWhenTheClampInputsChange_NotOnEveryRender()
    {
        var module = Module();
        var cut = RenderShell();
        cut.WaitForAssertion(() => module.Attach().Should().HaveCount(2));

        cut.Render(p => p.Add(x => x.Header, b => b.AddContent(0, "x")));
        cut.Render(p => p.Add(x => x.Header, b => b.AddContent(0, "y")));

        module.Attach().Should().HaveCount(2, "a re-render must not tear down a drag in progress");
    }

    // ── Persistence: only user-resized widths, validated, host wins ─────────

    private static string Stored(string left, string right = "") => "{" + left + (right.Length > 0 ? "," + right : "") + "}";

    [Fact]
    public void Persist_AppliesAValidStoredWidthOnLoad()
    {
        Module(stored: Stored("\"left\":{\"w\":300,\"base\":\"280px\"}", "\"right\":{\"w\":400,\"base\":\"320px\"}"));

        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.PersistWidthsKey, "demo"));

        cut.WaitForAssertion(() =>
        {
            Width(cut, "left").Should().Contain("300px");
            Width(cut, "right").Should().Contain("400px");
        });
    }

    [Theory]
    [InlineData("{\"left\":{\"w\":100,\"base\":\"280px\"}}", "below the minimum")]
    [InlineData("{\"left\":{\"w\":9999,\"base\":\"280px\"}}", "above the maximum")]
    [InlineData("{\"left\":{\"w\":\"300px; background:red\",\"base\":\"280px\"}}", "a raw string")]
    [InlineData("{\"left\":{\"w\":300,\"base\":\"999px\"}}", "the host supplies another width now")]
    [InlineData("{\"left\":{\"w\":300}}", "no base")]
    [InlineData("not json", "malformed")]
    [InlineData("{\"left\":\"300px\"}", "the previous format")]
    public void Persist_IgnoresInvalidOrStaleStoredWidths(string stored, string why)
    {
        var module = Module(stored: stored);

        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.PersistWidthsKey, "demo"));

        cut.WaitForAssertion(() => module.Load().Should().HaveCount(1));
        Width(cut, "left").Should().Contain("280px", why);
        Width(cut, "left").Should().NotContain("red").And.NotContain("300px");
    }

    [Fact]
    public void Persist_AJsFailureOnLoad_KeepsTheParameterWidths()
    {
        var module = Module(loadError: new JSException("SecurityError"));

        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.PersistWidthsKey, "demo"));

        cut.WaitForAssertion(() => module.Load().Should().HaveCount(1));
        Width(cut, "left").Should().Contain("280px");
    }

    [Fact]
    public async Task Persist_NoKey_NeverTouchesStorage()
    {
        var module = Module();
        var cut = RenderShell();

        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("left", 300));

        module.Load().Should().BeEmpty();
        module.Save().Should().BeEmpty();
    }

    [Fact]
    public async Task Persist_SavesOnlyTheUserResizedSide_WithTheHostBase()
    {
        var module = Module();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.PersistWidthsKey, "demo"));
        cut.WaitForAssertion(() => module.Load().Should().HaveCount(1));

        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("right", 400));

        var save = module.Save().Last();
        save.Arguments[0].Should().Be("demo");
        var (left, right) = EditorShellWidths.Parse((string?)save.Arguments[1]);
        left.Should().BeNull("the user never touched the left panel");
        right.Should().Be(new StoredWidth(400, "320px"));
    }

    [Fact]
    public async Task Persist_AHostBoundEchoOfTheUsersWidth_IsNotAHostOverride()
    {
        var module = Module();
        var leftWidth = "280px";
        var cut = RenderShell(TmLayoutMode.Desktop, p =>
        {
            p.Add(x => x.PersistWidthsKey, "demo");
            p.Add(x => x.LeftWidth, leftWidth);
            p.Add(x => x.LeftWidthChanged, EventCallback.Factory.Create<string>(this, v => leftWidth = v));
        });
        cut.WaitForAssertion(() => module.Load().Should().HaveCount(1));

        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("left", 350));
        cut.Render(p => p.Add(x => x.LeftWidth, leftWidth));

        Width(cut, "left").Should().Contain("350px");
        var (left, _) = EditorShellWidths.Parse((string?)module.Save().Last().Arguments[1]);
        left.Should().Be(new StoredWidth(350, "280px"), "the stored base is the width the HOST supplied, not its echo");
    }

    [Fact]
    public async Task Persist_AnExplicitHostWidthAfterAUserResize_WinsAndClearsTheStoredEntry()
    {
        var module = Module();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.PersistWidthsKey, "demo"));
        cut.WaitForAssertion(() => module.Load().Should().HaveCount(1));
        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("left", 350));

        cut.Render(p => p.Add(x => x.LeftWidth, "300px"));

        Width(cut, "left").Should().Contain("300px", "a width the host sets after the user resize wins");
        module.Save().Last().Arguments[1].Should().BeNull("the stale user width is removed from storage");
    }

    [Fact]
    public async Task Disposal_ThenACommit_DoesNotThrow()
    {
        Module();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.PersistWidthsKey, "demo"));

        await cut.Instance.DisposeAsync();

        var act = async () => await cut.Instance.HandleResizeCommitted("left", 300);
        await act.Should().NotThrowAsync();
    }
}
