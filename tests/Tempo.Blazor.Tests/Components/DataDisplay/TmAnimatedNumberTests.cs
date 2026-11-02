using Bunit;
using FluentAssertions;
using System.Globalization;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// TDD contract of <see cref="Tempo.Blazor.Components.DataDisplay.TmAnimatedNumber"/> (plan
/// ff043b2e, phase 1). One node carries the FINAL formatted value — a reader and a copy/paste get
/// the number once, and the animation only repaints that same text between frames, so nothing is
/// announced unless the host opts into <c>Announce</c>. The motion is delegated to the
/// <c>tm-animated-number.js</c> module only when the machine did not ask for reduced motion.
/// </summary>
public class TmAnimatedNumberTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private BunitJSModuleInterop SetupModule(bool reducedMotion = false)
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Mode = JSRuntimeMode.Strict;
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(reducedMotion);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
        return module;
    }

    private static IReadOnlyList<JSRuntimeInvocation> Animations(BunitJSModuleInterop module)
        => module.Invocations.Where(invocation => invocation.Identifier == "animate").ToList();

    [Fact]
    public void Renders_ItsRootAndTheVisualValue()
    {
        SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p.Add(c => c.Value, 1234.0));

        cut.Find(".tm-animated-number").Should().NotBeNull();
        cut.Find(".tm-animated-number__value").TextContent.Should().Be("1,234");
    }

    /// <summary>
    /// The value lives in exactly ONE node: the reader and a copy/paste get the number once, and the
    /// first paint already shows the final value (never a flash of 0). A running ticker is not a live
    /// region, so the intermediate frames are never announced.
    /// </summary>
    [Fact]
    public void WithoutAnnounce_TheValueNodeIsTheAccessibleValue()
    {
        SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 1234.0)
            .Add(c => c.Format, "N0"));

        var value = cut.Find(".tm-animated-number__value");
        value.TextContent.Should().Be("1,234");
        value.GetAttribute("aria-hidden").Should().BeNull(
            "the single node is the accessible value, so hiding it would leave nothing to read");

        cut.FindAll(".tm-animated-number__reader").Should().BeEmpty("no duplicate text next to the visual one");
        cut.FindAll("[aria-live]").Should().BeEmpty("an animated number is silent unless the host asks for an announcement");
        cut.FindAll("[role='status']").Should().BeEmpty();
    }

    [Fact]
    public void Announce_RendersExactlyOnePoliteLiveRegion()
    {
        SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 42.0)
            .Add(c => c.Announce, true));

        var regions = cut.FindAll("[aria-live]");
        regions.Should().ContainSingle("exactly one announcement region, so nothing is read twice");
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].GetAttribute("role").Should().Be("status");
        regions[0].GetAttribute("aria-atomic").Should().Be("true");
        regions[0].TextContent.Should().Be("42");
        regions[0].ClassList.Should().Contain("tm-sr-only");

        cut.Find(".tm-animated-number__value").GetAttribute("aria-hidden").Should().Be("true",
            "the region is the only text a reader may meet, so the visual span steps out of the tree");
    }

    [Fact]
    public void Format_DefaultsToGroupedWholeNumbers()
    {
        SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 1234.0)
            .Add(c => c.Culture, English));

        cut.Find(".tm-animated-number__value").TextContent.Should().Be("1,234");
    }

    [Theory]
    [InlineData("N1")]
    [InlineData("F2")]
    [InlineData("P1")]
    public void Format_ReachesTheRenderedValue(string format)
    {
        SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 0.125)
            .Add(c => c.Format, format)
            .Add(c => c.Culture, English));

        cut.Find(".tm-animated-number__value").TextContent.Should().Be(0.125.ToString(format, English));
    }

    [Fact]
    public void Culture_DefaultsToTheCurrentCulture()
    {
        SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 1234.5)
            .Add(c => c.Format, "N1"));

        cut.Find(".tm-animated-number__value").TextContent.Should()
            .Be(1234.5.ToString("N1", CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Culture_FormatsWithTheGivenCulture()
    {
        SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 1234.5)
            .Add(c => c.Format, "N1")
            .Add(c => c.Culture, Czech));

        cut.Find(".tm-animated-number__value").TextContent.Should().Be("1\u00A0234,5");
    }

    [Fact]
    public void PrefixAndSuffix_WrapTheNumber_ForReaderAndSight()
    {
        SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 42.0)
            .Add(c => c.Prefix, "€")
            .Add(c => c.Suffix, " / mo"));

        cut.Find(".tm-animated-number__value").TextContent.Should().Be("€42 / mo");
    }

    [Fact]
    public void FirstAnimation_RunsFromTheStartValue_ToTheTarget()
    {
        var module = SetupModule();
        Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 500.0)
            .Add(c => c.StartValue, 100.0)
            .Add(c => c.Duration, 700));

        var animate = Animations(module).Should().ContainSingle().Subject;
        animate.Arguments[0].Should().BeOfType<Microsoft.AspNetCore.Components.ElementReference>();
        ((double?)animate.Arguments[1]).Should().Be(100.0);
        ((double)animate.Arguments[2]!).Should().Be(500.0);
        ((int)animate.Arguments[3]!).Should().Be(700, "Duration is the host's knob and travels verbatim");
    }

    [Fact]
    public void FirstAnimation_DefaultsToZero()
    {
        var module = SetupModule();
        Render<TmAnimatedNumber>(p => p.Add(c => c.Value, 500.0));

        ((double?)Animations(module).Single().Arguments[1]).Should().Be(0.0);
    }

    [Fact]
    public void ReducedMotion_RendersTheFinalValue_WithoutAnyAnimation()
    {
        var module = SetupModule(reducedMotion: true);
        var cut = Render<TmAnimatedNumber>(p => p.Add(c => c.Value, 500.0));

        cut.Find(".tm-animated-number__value").TextContent.Should().Be("500");
        Animations(module).Should().BeEmpty("a reader who asked for less motion gets no ticker at all");
    }

    [Fact]
    public void Duration_Zero_SkipsTheAnimation()
    {
        var module = SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 500.0)
            .Add(c => c.Duration, 0));

        cut.Find(".tm-animated-number__value").TextContent.Should().Be("500");
        Animations(module).Should().BeEmpty("Duration=0 means instant — there is nothing to animate");
    }

    [Fact]
    public void Animate_False_TouchesNeitherJavaScriptNorTheMotionPreference()
    {
        var module = SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 500.0)
            .Add(c => c.Animate, false));

        cut.Find(".tm-animated-number__value").TextContent.Should().Be("500");
        module.Invocations.Should().BeEmpty("the opt-out is a pure-Blazor path: no import, no probe");
        JSInterop.Invocations.Should().BeEmpty();
    }

    /// <summary>
    /// A value that changes while the ticker is still running must RETARGET the animation onto the
    /// new number immediately — never finish the old one first, and never restart from
    /// <see cref="Tempo.Blazor.Components.DataDisplay.TmAnimatedNumber.StartValue"/>. The component
    /// therefore passes <c>from = null</c> on every later change, which tells the module to continue
    /// from the number it is currently painting.
    /// </summary>
    [Fact]
    public void ValueChangedMidAnimation_RetargetsToTheNewValue()
    {
        var module = SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p.Add(c => c.Value, 100.0));
        cut.WaitForAssertion(() => Animations(module).Should().ContainSingle());

        cut.Render(p => p.Add(c => c.Value, 200.0));

        cut.WaitForAssertion(() => Animations(module).Should().HaveCount(2));
        var retarget = Animations(module)[1];
        ((double)retarget.Arguments[2]!).Should().Be(200.0);
        ((double?)retarget.Arguments[1]).Should().BeNull(
            "a retarget continues from the number on screen instead of jumping back to StartValue");
        cut.Find(".tm-animated-number__value").TextContent.Should().Be("200");
    }

    [Fact]
    public void UnchangedValue_OnAParentRender_DoesNotRestartTheAnimation()
    {
        var module = SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p.Add(c => c.Value, 100.0));
        cut.WaitForAssertion(() => Animations(module).Should().ContainSingle());

        cut.Render(p => p.Add(c => c.Value, 100.0));

        Animations(module).Should().ContainSingle("an unrelated parent render must not replay the ticker");
    }

    /// <summary>
    /// The cleanup contract, exercised on the component itself (the renderer's own disposal path is
    /// bUnit's business): a ticker that outlived its component would keep a requestAnimationFrame
    /// loop alive against a detached node.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_CancelsTheRunningAnimation()
    {
        var module = SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p.Add(c => c.Value, 100.0));
        cut.WaitForAssertion(() => Animations(module).Should().ContainSingle());

        await cut.Instance.DisposeAsync();

        module.Invocations.Where(invocation => invocation.Identifier == "cancel").Should()
            .NotBeEmpty("a disposed ticker must stop its requestAnimationFrame loop");
    }

    /// <summary>Disposing a component that never animated must not import the module just to cancel.</summary>
    [Fact]
    public async Task DisposeAsync_WithoutAnimation_DoesNotTouchJavaScript()
    {
        var module = SetupModule();
        var cut = Render<TmAnimatedNumber>(p => p
            .Add(c => c.Value, 100.0)
            .Add(c => c.Animate, false));

        await cut.Instance.DisposeAsync();

        module.Invocations.Should().BeEmpty();
    }
}
