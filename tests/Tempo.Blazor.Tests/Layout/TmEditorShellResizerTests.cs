using System.Globalization;
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

    private Mod Module(double canvas = 700, string? stored = null, Exception? loadError = null, double asideWidth = 300)
    {
        var module = JSInterop.SetupModule(ShellModule);
        module.Setup<double>("measureCanvas", _ => true).SetResult(canvas);
        module.Setup<double>("measureWidth", _ => true).SetResult(asideWidth);
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

    // ── C9: the tablet fit width keeps the canvas at MinCanvasWidth ─────────

    [Fact]
    public void Tablet_ExpandedPanel_ShrinksToKeepMinCanvasWidth_ButNotBelowItsMin()
    {
        // At 768 the measured canvas (278) sits below MinCanvasWidth (400): the expanded panel
        // gives up the 122px deficit, clamped to its own minimum (240 for the right panel).
        Module(canvas: 278);
        var cut = RenderShell(TmLayoutMode.Tablet);

        var aside = cut.Find("aside[data-region='right']");
        aside.GetAttribute("style").Should().Contain("--tm-editor-shell-panel-width: 240px");
        cut.Find("[role='separator']").GetAttribute("aria-valuenow").Should().Be("240",
            "aria-valuenow reports the rendered width, the fit included");
    }

    [Fact]
    public async Task Tablet_FitWidth_IsNotPersisted_NorReported()
    {
        var mod = Module(canvas: 278);
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Tablet, p =>
        {
            p.Add(x => x.PersistWidthsKey, "tablet-fit-demo");
            p.Add(x => x.LeftWidthChanged, EventCallback.Factory.Create<string>(this, v => raised.Add($"left:{v}")));
            p.Add(x => x.RightWidthChanged, EventCallback.Factory.Create<string>(this, v => raised.Add($"right:{v}")));
        });

        cut.Find("aside[data-region='right']").GetAttribute("style").Should().Contain("240px");

        raised.Should().BeEmpty("a fit width is a layout decision, not a user resize");
        await Task.Yield();
        mod.Save().Should().BeEmpty("the fit width is never persisted");
    }

    [Fact]
    public void Desktop_IsUnaffected()
    {
        // The fit width exists on a tablet layout only: a desktop shell renders the host width
        // even when the measured canvas sits below MinCanvasWidth.
        Module(canvas: 278);
        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.Find("aside[data-region='right']").GetAttribute("style")
            .Should().Contain("--tm-editor-shell-panel-width: 320px");
        cut.Find("[role='separator'][data-side='right']").GetAttribute("aria-valuenow").Should().Be("320");
    }

    [Fact]
    public void Tablet_FitWidth_RelaxesBackTowardsTheHostWidth_WhenTheContainerGrows()
    {
        var module = JSInterop.SetupModule(ShellModule);
        var measure = module.Setup<double>("measureCanvas", _ => true);
        measure.SetResult(278);
        module.Setup<double>("measureWidth", _ => true).SetResult(300);
        var attach = module.SetupVoid("attachResize", _ => true); attach.SetVoidResult();
        var detach = module.SetupVoid("detachResize", _ => true); detach.SetVoidResult();

        var cut = RenderShell(TmLayoutMode.Tablet);
        cut.Find("aside[data-region='right']").GetAttribute("style").Should().Contain("240px");

        // The container grows: the canvas now measures a surplus over MinCanvasWidth, so the fit
        // relaxes — in steps, up to the host width, never beyond it.
        measure.SetResult(458);
        cut.Render();

        cut.Find("aside[data-region='right']").GetAttribute("style")
            .Should().Contain("--tm-editor-shell-panel-width: 320px",
                "with room again the panel renders at the host width");
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
    public void Separator_AttachesWithTheSideAndLimits_TheJsModuleNeedsForTheDragDirection()
    {
        var module = Module();
        var cut = RenderShell();
        cut.WaitForAssertion(() => module.Attach().Should().HaveCount(2));

        var options = module.Attach().Select(i => System.Text.Json.JsonSerializer.Serialize(i.Arguments[5])).ToList();

        options.Should().Contain("{\"side\":\"left\",\"min\":200,\"max\":480,\"minCanvas\":400}");
        options.Should().Contain("{\"side\":\"right\",\"min\":240,\"max\":560,\"minCanvas\":400}");
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

    // ── F6 r2 G6 ────────────────────────────────────────────────────────────

    [Fact]
    public void Persist_ARestoredWidth_IsReportedThroughTheWidthCallbacks()
    {
        Module(stored: Stored("\"left\":{\"w\":300,\"base\":\"280px\"}", "\"right\":{\"w\":400,\"base\":\"320px\"}"));
        var left = new List<string>();
        var right = new List<string>();

        RenderShell(TmLayoutMode.Desktop, p =>
        {
            p.Add(x => x.PersistWidthsKey, "demo");
            p.Add(x => x.LeftWidthChanged, EventCallback.Factory.Create<string>(this, v => left.Add(v)));
            p.Add(x => x.RightWidthChanged, EventCallback.Factory.Create<string>(this, v => right.Add(v)));
        });

        left.Should().Equal(new[] { "300px" }, "a restore counts as a user value, so a bound host learns about it");
        right.Should().Equal(new[] { "400px" });
    }

    [Fact]
    public void Persist_AKeySetAfterTheFirstRender_IsLoadedWhenItAppears()
    {
        var module = Module(stored: Stored("\"left\":{\"w\":300,\"base\":\"280px\"}"));
        var cut = RenderShell(TmLayoutMode.Desktop);
        module.Load().Should().BeEmpty("no key yet");

        cut.Render(p => p.Add(x => x.PersistWidthsKey, "late"));

        cut.WaitForAssertion(() => module.Load().Should().HaveCount(1));
        cut.WaitForAssertion(() => Width(cut, "left").Should().Contain("300px"));
    }

    [Fact]
    public void Persist_AChangedKey_LoadsTheNewKey()
    {
        var module = Module();
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.PersistWidthsKey, "one"));
        cut.WaitForAssertion(() => module.Load().Should().HaveCount(1));

        cut.Render(p => p.Add(x => x.PersistWidthsKey, "two"));

        cut.WaitForAssertion(() => module.Load().Should().HaveCount(2));
        module.Load().Last().Arguments[0].Should().Be("two");
    }

    [Fact]
    public async Task Persist_ResizingBackToTheHostWidth_RemovesTheStoredOverride()
    {
        var module = Module();
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Desktop, p =>
        {
            p.Add(x => x.PersistWidthsKey, "demo");
            p.Add(x => x.LeftWidthChanged, EventCallback.Factory.Create<string>(this, v => raised.Add(v)));
        });
        cut.WaitForAssertion(() => module.Load().Should().HaveCount(1));
        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("left", 350));
        EditorShellWidths.Parse((string?)module.Save().Last().Arguments[1]).Left.Should().NotBeNull();

        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("left", 280));

        module.Save().Last().Arguments[1].Should().BeNull("the user is back on the host width: nothing is left to store");
        raised.Should().Equal("350px", "280px");
        Width(cut, "left").Should().Contain("280px");
    }

    [Fact]
    public void Keyboard_FromANonPixelHostWidth_StepsFromTheMeasuredAsideWidth()
    {
        Module(asideWidth: 320);
        var raised = new List<string>();
        var cut = RenderShell(TmLayoutMode.Desktop, p =>
        {
            p.Add(x => x.LeftWidth, "20rem");
            p.Add(x => x.LeftWidthChanged, EventCallback.Factory.Create<string>(this, v => raised.Add(v)));
        });

        cut.Find("[role='separator'][data-side='left']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        raised.Should().Equal(new[] { "336px" }, "320 measured + 16, not the 280 fallback + 16");
    }

    // ── C9 follow-up: user resize starts from the RENDERED (fit) width ──────

    /// <summary>
    /// A minimal <see cref="IJSRuntime"/> that feeds the measured canvas width back from the width
    /// the right panel actually rendered at (canvas = base - rendered + surplus). A static bUnit
    /// mock instead slides every fit to the panel minimum within a single render flush — which
    /// never happens in a real browser, where the narrowed panel widens the canvas.
    /// </summary>
    private sealed class FeedbackJsRuntime : IJSRuntime
    {
        public Func<double>? MeasureCanvas { get; set; }
        public int MeasureCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)new FeedbackModule(this));
            return ValueTask.FromResult<TValue>(default!);
        }

        private sealed class FeedbackModule : IJSObjectReference
        {
            private readonly FeedbackJsRuntime _runtime;
            public FeedbackModule(FeedbackJsRuntime runtime) => _runtime = runtime;

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
                => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            {
                if (identifier == "measureCanvas")
                {
                    _runtime.MeasureCount++;
                    return ValueTask.FromResult((TValue)(object)(_runtime.MeasureCanvas?.Invoke() ?? 700d));
                }
                if (identifier == "measureWidth") return ValueTask.FromResult((TValue)(object)300d);
                if (identifier == "loadPanelWidths") return ValueTask.FromResult<TValue>(default!);
                return ValueTask.FromResult<TValue>(default!);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private (IRenderedComponent<TmEditorShell> cut, FeedbackJsRuntime js) RenderTabletWithFeedback(
        Action<ComponentParameterCollectionBuilder<TmEditorShell>>? extra = null)
    {
        var js = new FeedbackJsRuntime();
        Services.AddSingleton<IJSRuntime>(js);
        var cut = RenderShell(TmLayoutMode.Tablet, extra);
        js.MeasureCanvas = () =>
        {
            var style = cut.Find("aside[data-region='right']").GetAttribute("style")!;
            var rendered = double.Parse(System.Text.RegularExpressions.Regex.Match(style, @"(\d+(?:\.\d+)?)px").Groups[1].Value, CultureInfo.InvariantCulture);
            // Geometry emulating the 768px demo host: a 320px panel leaves a 358px canvas, so the
            // canvas measures 678 - rendered; _surplus emulates scrollbars coming and going.
            return 678 - rendered + _surplus;
        };
        return (cut, js);
    }

    private double _surplus;

    private async Task ConvergeFitAsync(IRenderedComponent<TmEditorShell> cut)
    {
        await cut.InvokeAsync(() => cut.Instance.OnCanvasResized());
        // The fit converges at 278 (320 - the 42px deficit) and stays: the narrowed panel is what
        // brings the canvas to MinCanvasWidth.
        cut.WaitForAssertion(() => Width(cut, "right").Should().Contain("278px"));
    }

    [Fact]
    public async Task Keyboard_TabletFitActive_StepsFromTheRenderedFitWidth()
    {
        var raised = new List<string>();
        var (cut, _) = RenderTabletWithFeedback(p =>
            p.Add(x => x.RightWidthChanged, EventCallback.Factory.Create<string>(this, v => raised.Add(v))));
        await ConvergeFitAsync(cut);

        cut.Find("[role='separator']").GetAttribute("aria-valuenow").Should().Be("278",
            "aria-valuenow reports the rendered width, the fit included");

        // ArrowRight SHRINKS the right panel (its separator is on the panel's left edge). The
        // step starts from the FIT width on screen (278 - 16), not from the host 320 (336).
        cut.Find("[role='separator'][data-side='right']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        raised.Should().Equal(new[] { "262px" }, "the callback reports the width the screen shows");
        cut.WaitForAssertion(() => Width(cut, "right").Should().Contain("262px"));
        cut.Find("[role='separator']").GetAttribute("aria-valuenow").Should().Be("262");
        raised.Should().Equal(new[] { "262px" }, "a settled width is reported once");
    }

    [Fact]
    public async Task ResizeCommitted_FromTheFitWidth_SticksAsTheNewCeiling()
    {
        var raised = new List<string>();
        var (cut, _) = RenderTabletWithFeedback(p =>
            p.Add(x => x.RightWidthChanged, EventCallback.Factory.Create<string>(this, v => raised.Add(v))));
        await ConvergeFitAsync(cut);

        // A drag that settled below the fit (its start width was the RENDERED width) commits from
        // the rendered width: the user width becomes the new ceiling and — the canvas has room
        // again at 250px — the panel stays there instead of snapping back to the host width.
        await cut.InvokeAsync(() => cut.Instance.HandleResizeCommitted("right", 250));

        raised.Should().Equal("250px");
        cut.WaitForAssertion(() => Width(cut, "right").Should().Contain("250px"));
        cut.Find("[role='separator']").GetAttribute("aria-valuenow").Should().Be("250");
    }

    [Fact]
    public async Task TabletFit_ASmallSurplusKeepsTheFit_ARelSurplusRelaxesIt()
    {
        var (cut, js) = RenderTabletWithFeedback();
        await ConvergeFitAsync(cut);

        // A scrollbar vanishes: the canvas gains a sliver of surplus. The fit must HOLD — relaxing
        // on every sliver would oscillate the panel width and churn scrollbars.
        _surplus = 10;
        await cut.InvokeAsync(() => cut.Instance.OnCanvasResized());
        Width(cut, "right").Should().Contain("278px", "a 10px surplus keeps the fit");

        var measures = js.MeasureCount;
        for (var i = 0; i < 4; i++)
        {
            _surplus = i % 2 == 0 ? 10 : 0;
            await cut.InvokeAsync(() => cut.Instance.OnCanvasResized());
        }
        js.MeasureCount.Should().BeLessThanOrEqualTo(measures + 2 * 4 + 2, "a surplus sliver must not churn renders");
        Width(cut, "right").Should().Contain("278px", "the fit holds through the surplus alternation");

        // A real surplus (24px, a classic scrollbar) gives the width back, in steps.
        _surplus = 24;
        await cut.InvokeAsync(() => cut.Instance.OnCanvasResized());
        cut.WaitForAssertion(() => Width(cut, "right").Should().Contain("302px",
            "278 + the 24px surplus, relaxed towards the host width"));
    }

    // ── F6 r2 G8 ────────────────────────────────────────────────────────────

    [Fact]
    public void Separator_AriaValueMax_IsTheWidthTheUserCanActuallyReach_GivenMinCanvasWidth()
    {
        // 450px canvas, 400 minimum: the left panel (280) can grow by 50 only, not up to the 480 max.
        Module(canvas: 450);

        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.WaitForAssertion(() =>
            cut.Find("[role='separator'][data-side='left']").GetAttribute("aria-valuemax").Should().Be("330"));
        cut.Find("[role='separator'][data-side='left']").GetAttribute("aria-valuemin").Should().Be("200");
    }

    [Fact]
    public void Separator_AriaValueMax_IsTheConfiguredMax_WhenTheCanvasHasRoom()
    {
        Module(canvas: 2000);

        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.WaitForAssertion(() => cut.Find("[role='separator'][data-side='right']").GetAttribute("aria-valuemax").Should().Be("560"));
    }

    [Fact]
    public void Separator_AriaValueMax_NeverDropsBelowTheCurrentWidth()
    {
        // A canvas already below its minimum: growth is refused, but valuenow must stay <= valuemax.
        Module(canvas: 300);

        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.WaitForAssertion(() => cut.Find("[role='separator'][data-side='left']").GetAttribute("aria-valuemax").Should().Be("280"));
    }
}
