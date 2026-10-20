using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

public class TmSplitterTests : LocalizationTestBase
{
    private const string SplitterModule = "./_content/Tempo.Blazor/js/tm-splitter.js";

    /// <summary>
    /// Loads the splitter module the way the browser does. <c>measureSize</c> answers
    /// <paramref name="measuredSize"/> (−1 = cannot measure, so the keyboard path falls back to the
    /// committed/parameter pixel size) and <c>directionOf</c> answers <paramref name="direction"/>.
    /// </summary>
    private void SetupSplitterModule(int measuredSize = -1, string direction = "ltr")
    {
        var module = JSInterop.SetupModule(SplitterModule);
        module.Setup<int>("measureSize", _ => true).SetResult(measuredSize);
        module.Setup<string>("directionOf", _ => true).SetResult(direction);
        module.SetupVoid("attachResize", _ => true).SetVoidResult();
        module.SetupVoid("detachResize", _ => true).SetVoidResult();
    }

    [Fact]
    public void TmSplitter_Renders_Panes()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1.AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.FindAll(".tm-splitter__pane").Count.Should().Be(2);
    }

    [Fact]
    public void TmSplitter_Vertical_Has_Vertical_Class()
    {
        var cut = Render<TmSplitter>(p => p
            .Add(x => x.Orientation, SplitterOrientation.Vertical)
            .AddChildContent<TmSplitterPane>(pane1 => pane1.AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.Find(".tm-splitter--vertical").Should().NotBeNull();
    }

    [Fact]
    public void TmSplitter_Resizer_Bar_Renders()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1.AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.FindAll(".tm-splitter__resizer").Count.Should().Be(1);
    }

    [Fact]
    public void TmSplitter_Pane_Collapsible_Renders_Collapse_Button()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Collapsible, true)
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.Find(".tm-splitter__collapse-btn").Should().NotBeNull();
    }

    [Fact]
    public void TmSplitter_Pane_Size_Applies_Style()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var pane = cut.FindAll(".tm-splitter__pane")[0];
        pane.GetAttribute("style").Should().Contain("flex-basis: 200px");
    }

    [Fact]
    public void TmSplitter_Pane_MinMaxSize_Applies_Style()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.MinSize, "100px")
                .Add(x => x.MaxSize, "400px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var pane = cut.FindAll(".tm-splitter__pane")[0];
        var style = pane.GetAttribute("style");
        style.Should().Contain("min-width: 100px");
        style.Should().Contain("max-width: 400px");
    }

    [Fact]
    public void TmSplitter_Pane_Collapsed_Applies_Collapsed_Class()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Collapsed, true)
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var pane = cut.FindAll(".tm-splitter__pane")[0];
        pane.ClassList.Contains("tm-splitter__pane--collapsed").Should().BeTrue();
    }

    [Fact]
    public void TmSplitter_Collapse_Button_Toggles_Collapsed()
    {
        bool collapsed = false;
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Collapsible, true)
                .Add(x => x.Collapsed, collapsed)
                .Add(x => x.CollapsedChanged, EventCallback.Factory.Create<bool>(this, v => collapsed = v))
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var btn = cut.Find(".tm-splitter__collapse-btn");
        btn.Click();

        collapsed.Should().BeTrue();
    }

    [Fact]
    public void TmSplitter_Last_Pane_Has_No_Resizer()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1.AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2"))
            .AddChildContent<TmSplitterPane>(pane3 => pane3.AddChildContent("Pane 3")));

        cut.FindAll(".tm-splitter__resizer").Count.Should().Be(2);
    }

    // ── The drag lives in tm-splitter.js (node tests): Blazor owns the single commit path ──────

    [Fact]
    public async Task OnSplitterResizeCommitted_SetsFlexBasisPx()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        await cut.InvokeAsync(() =>
            cut.FindComponents<TmSplitterPane>().First().Instance.OnSplitterResizeCommitted(250));

        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style")
            .Should().Contain("flex: 0 0 250px", "the committed size locks the pane to exact pixels");
    }

    [Fact]
    public void Resizer_Has_Role_Separator_Tabindex_AriaLabel()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var resizer = cut.Find(".tm-splitter__resizer");
        resizer.GetAttribute("role").Should().Be("separator");
        resizer.GetAttribute("tabindex").Should().Be("0", "the separator is keyboard operable (WCAG 2.1.1)");
        resizer.GetAttribute("aria-label").Should().Be("Resize panes");
        // A horizontal splitter's separator is a vertical strip: the separator's own orientation.
        resizer.GetAttribute("aria-orientation").Should().Be("vertical");
        resizer.GetAttribute("aria-valuenow").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Resizer_Reports_AriaValueBounds_From_Pixel_Parameters()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .Add(x => x.MinSize, "100px")
                .Add(x => x.MaxSize, "400px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var resizer = cut.Find(".tm-splitter__resizer");
        resizer.GetAttribute("aria-valuemin").Should().Be("100");
        resizer.GetAttribute("aria-valuemax").Should().Be("400");
        resizer.GetAttribute("aria-valuenow").Should().Be("200");
        resizer.GetAttribute("aria-valuetext").Should().Be("200 px", "screen readers hear the unit, not a bare number");
    }

    [Fact]
    public void AriaValueMin_Is_Always_Emitted_Even_Without_A_Parseable_MinSize()
    {
        SetupSplitterModule(measuredSize: -1);
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "30%")
                .Add(x => x.MinSize, "10rem")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var resizer = cut.Find(".tm-splitter__resizer");
        resizer.GetAttribute("aria-valuemin").Should().Be("0", "0 is the floor every splitter respects");
        resizer.HasAttribute("aria-valuemax").Should().BeFalse("no parseable MaxSize and the measure failed: unknown");
        resizer.HasAttribute("aria-valuenow").Should().BeFalse("30% is not a pixel length and the measure failed");
    }

    [Fact]
    public void Aria_Values_Are_Measured_When_The_Size_Is_Not_A_Pixel_Length()
    {
        SetupSplitterModule(measuredSize: 300);
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "30%")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.WaitForState(() => cut.Find(".tm-splitter__resizer").GetAttribute("aria-valuenow") == "300");
        var resizer = cut.Find(".tm-splitter__resizer");
        resizer.GetAttribute("aria-valuenow").Should().Be("300", "the rendered pane is measured once attached");
        resizer.GetAttribute("aria-valuemin").Should().Be("0");
        resizer.GetAttribute("aria-valuemax").Should().Be("300", "the splitter's measured main-axis length is the ceiling");
        resizer.GetAttribute("aria-valuetext").Should().Be("300 px");
    }

    [Fact]
    public void Collapse_Button_Is_A_Sibling_Of_The_Separator_Not_A_Child()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Collapsible, true)
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.FindAll("[role='separator'] button").Should().BeEmpty(
            "a button inside role=separator would be announced as part of the separator's value — it lives in the gutter beside the strip");
        cut.Find(".tm-splitter__gutter button").Should().NotBeNull("the collapse button sits in the gutter");
    }

    [Fact]
    public void Vertical_Separator_Reports_Horizontal_Orientation()
    {
        var cut = Render<TmSplitter>(p => p
            .Add(x => x.Orientation, SplitterOrientation.Vertical)
            .AddChildContent<TmSplitterPane>(pane1 => pane1.AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.Find(".tm-splitter__resizer").GetAttribute("aria-orientation").Should().Be("horizontal");
    }

    [Fact]
    public async Task ArrowRight_Grows_Horizontal_Pane_By16()
    {
        SetupSplitterModule();
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.Find(".tm-splitter__resizer").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        await Task.Yield();

        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style").Should().Contain("flex: 0 0 216px");
    }

    [Fact]
    public async Task ArrowLeft_Shrinks_And_Clamps_At_Minimum()
    {
        SetupSplitterModule();
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "120px")
                .Add(x => x.MinSize, "100px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var resizer = cut.Find(".tm-splitter__resizer");
        resizer.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        await Task.Yield();
        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style").Should().Contain("flex: 0 0 104px");

        resizer.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        await Task.Yield();
        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style")
            .Should().Contain("flex: 0 0 100px", "the keyboard shares the drag's clamp");
    }

    [Fact]
    public async Task ShiftArrow_Grows_By64()
    {
        SetupSplitterModule();
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.Find(".tm-splitter__resizer").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        await Task.Yield();

        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style").Should().Contain("flex: 0 0 264px");
    }

    [Fact]
    public async Task Vertical_Uses_ArrowDown_And_Ignores_ArrowRight()
    {
        SetupSplitterModule();
        var cut = Render<TmSplitter>(p => p
            .Add(x => x.Orientation, SplitterOrientation.Vertical)
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var resizer = cut.Find(".tm-splitter__resizer");
        resizer.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        await Task.Yield();
        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style")
            .Should().Contain("flex-basis: 200px", "a vertical splitter resizes along the column axis only");

        resizer.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        await Task.Yield();
        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style").Should().Contain("flex: 0 0 216px");
    }

    [Fact]
    public async Task Home_Clamps_To_MinSize_Px_And_End_To_MaxSize_Px()
    {
        SetupSplitterModule();
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .Add(x => x.MinSize, "100px")
                .Add(x => x.MaxSize, "400px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var resizer = cut.Find(".tm-splitter__resizer");
        resizer.KeyDown(new KeyboardEventArgs { Key = "End" });
        await Task.Yield();
        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style").Should().Contain("flex: 0 0 400px");

        resizer.KeyDown(new KeyboardEventArgs { Key = "Home" });
        await Task.Yield();
        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style").Should().Contain("flex: 0 0 100px");
    }

    [Fact]
    public async Task Home_Falls_Back_To_Zero_When_MinSize_Is_Not_A_Pixel_Length()
    {
        SetupSplitterModule();
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .Add(x => x.MinSize, "10rem")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.Find(".tm-splitter__resizer").KeyDown(new KeyboardEventArgs { Key = "Home" });
        await Task.Yield();

        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style")
            .Should().Contain("flex: 0 0 0px", "Home always has a floor: 0 when no parseable MinSize");
    }

    [Fact]
    public async Task End_Falls_Back_To_The_Measured_Splitter_Size()
    {
        SetupSplitterModule(measuredSize: 300);
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "100px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.Find(".tm-splitter__resizer").KeyDown(new KeyboardEventArgs { Key = "End" });
        await Task.Yield();

        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style")
            .Should().Contain("flex: 0 0 300px", "End measures the splitter's main axis when no parseable MaxSize");
    }

    [Fact]
    public async Task ArrowRight_Shrinks_In_Rtl_And_ArrowLeft_Grows()
    {
        SetupSplitterModule(direction: "rtl");
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Size, "200px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        var resizer = cut.Find(".tm-splitter__resizer");
        resizer.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        await Task.Yield();
        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style")
            .Should().Contain("flex: 0 0 184px", "in RTL ArrowRight points toward the start edge, so the pane shrinks");

        resizer.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        await Task.Yield();
        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style")
            .Should().Contain("flex: 0 0 200px", "in RTL ArrowLeft grows the start pane back");
    }

    [Fact]
    public void Collapse_Button_Has_AccessibleName()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Collapsible, true)
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.Find(".tm-splitter__collapse-btn").GetAttribute("aria-label")
            .Should().Be("Collapse pane", "the button carries a localized accessible name");
    }

    [Fact]
    public void Collapse_Button_Keys_Do_Not_Reach_The_Resizer()
    {
        SetupSplitterModule();
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Collapsible, true)
                .Add(x => x.Size, "200px")
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        // The button is fenced with @onkeydown:stopPropagation: bUnit cannot even dispatch the
        // keydown through it, which is the observable proof the key never reaches the separator.
        var act = () => cut.Find(".tm-splitter__collapse-btn")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        act.Should().Throw<MissingEventHandlerException>(
            "the barrier keeps the collapse button's keys off the separator's handler");

        cut.FindAll(".tm-splitter__pane")[0].GetAttribute("style")
            .Should().Contain("flex-basis: 200px", "the barriered child's keys never resize the pane");
    }

    [Fact]
    public void No_Eval_Interop_Is_Used()
    {
        var cut = Render<TmSplitter>(p => p
            .AddChildContent<TmSplitterPane>(pane1 => pane1
                .Add(x => x.Collapsible, true)
                .AddChildContent("Pane 1"))
            .AddChildContent<TmSplitterPane>(pane2 => pane2.AddChildContent("Pane 2")));

        cut.Find(".tm-splitter__collapse-btn").Click();

        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "eval",
            "a strict CSP has no unsafe-eval; the drag is the tm-splitter.js module");
    }
}
