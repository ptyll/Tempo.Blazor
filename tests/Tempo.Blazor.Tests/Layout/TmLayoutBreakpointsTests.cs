using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// F6 review round 1, decision Q3: the layout thresholds are per instance. An observer (and the
/// editor shell on top of it) can classify its container with its own pair; the default pair is
/// the shared <see cref="TmBreakpoints"/> so nothing changes for an existing host.
/// </summary>
public class TmLayoutBreakpointsTests : LocalizationTestBase
{
    private const string ObserverModule = "./_content/Tempo.Blazor/js/layout-observer.js";

    // ── The value type ──────────────────────────────────────────────────────

    [Fact]
    public void Default_IsTheSharedSmAndLg()
    {
        TmLayoutBreakpoints.Default.Sm.Should().Be(TmBreakpoints.Sm);
        TmLayoutBreakpoints.Default.Lg.Should().Be(TmBreakpoints.Lg);
        TmLayoutBreakpoints.Default.Should().Be(new TmLayoutBreakpoints(640, 1024));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 10)]
    [InlineData(20, 10)]
    public void Constructor_RejectsAPairThatCannotClassify(int sm, int lg)
    {
        var act = () => new TmLayoutBreakpoints(sm, lg);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, TmLayoutMode.Mobile)]
    [InlineData(767.9, TmLayoutMode.Mobile)]
    [InlineData(768, TmLayoutMode.Tablet)]
    [InlineData(1199.9, TmLayoutMode.Tablet)]
    [InlineData(1200, TmLayoutMode.Desktop)]
    [InlineData(1600, TmLayoutMode.Desktop)]
    public void Classify_WithEmailLikeThresholds_IsHalfOpen(double width, TmLayoutMode expected)
    {
        new TmLayoutBreakpoints(768, 1200).Classify(width).Should().Be(expected);
    }

    [Theory]
    [InlineData(639, TmLayoutMode.Mobile)]
    [InlineData(640, TmLayoutMode.Tablet)]
    [InlineData(1023, TmLayoutMode.Tablet)]
    [InlineData(1024, TmLayoutMode.Desktop)]
    public void Classify_Default_MatchesTmBreakpointsClassify(double width, TmLayoutMode expected)
    {
        TmLayoutBreakpoints.Default.Classify(width).Should().Be(expected);
        TmBreakpoints.Classify(width).Should().Be(expected);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void Classify_RejectsABrokenMeasurement(double width)
    {
        var act = () => TmLayoutBreakpoints.Default.Classify(width);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── TmLayoutObserver ────────────────────────────────────────────────────

    private static (int Sm, int Lg) Thresholds(object options)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(options));
        var breakpoints = json.RootElement.GetProperty("breakpoints");
        return (breakpoints.GetProperty("sm").GetInt32(), breakpoints.GetProperty("lg").GetInt32());
    }

    [Fact]
    public void Observer_Auto_DefaultsToTheSharedThresholds()
    {
        var module = JSInterop.SetupModule(ObserverModule);
        var observe = module.SetupVoid("observe", _ => true);

        Render<TmLayoutObserver>(p => p.Add(x => x.LayoutMode, TmLayoutMode.Auto));

        Thresholds(observe.Invocations.Single().Arguments[3]!).Should().Be((640, 1024));
    }

    [Fact]
    public void Observer_Auto_FeedsTheCustomThresholdsToTheModule()
    {
        var module = JSInterop.SetupModule(ObserverModule);
        var observe = module.SetupVoid("observe", _ => true);

        Render<TmLayoutObserver>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Auto)
            .Add(x => x.Breakpoints, new TmLayoutBreakpoints(768, 1200)));

        Thresholds(observe.Invocations.Single().Arguments[3]!).Should().Be((768, 1200));
    }

    [Fact]
    public void Observer_BreakpointsChange_ReobservesWithTheNewThresholds()
    {
        var module = JSInterop.SetupModule(ObserverModule);
        var observe = module.SetupVoid("observe", _ => true);
        var cut = Render<TmLayoutObserver>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Auto)
            .Add(x => x.Breakpoints, new TmLayoutBreakpoints(768, 1200)));

        cut.Render(p => p.Add(x => x.Breakpoints, new TmLayoutBreakpoints(700, 1100)));

        cut.WaitForAssertion(() => observe.Invocations.Should().HaveCount(2));
        Thresholds(observe.Invocations.Last().Arguments[3]!).Should().Be((700, 1100));
    }

    [Fact]
    public void Observer_EqualBreakpointsRerender_DoesNotReobserve()
    {
        var module = JSInterop.SetupModule(ObserverModule);
        var observe = module.SetupVoid("observe", _ => true);
        var cut = Render<TmLayoutObserver>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Auto)
            .Add(x => x.Breakpoints, new TmLayoutBreakpoints(768, 1200)));

        cut.Render(p => p.Add(x => x.Breakpoints, new TmLayoutBreakpoints(768, 1200)));

        observe.Invocations.Should().HaveCount(1, "value-equal thresholds are not a change");
    }

    [Fact]
    public void Observer_Forced_NeverImportsTheModule_WhateverTheBreakpoints()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;

        var cut = Render<TmLayoutObserver>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Tablet)
            .Add(x => x.Breakpoints, new TmLayoutBreakpoints(768, 1200)));

        cut.Find(".tm-layout").GetAttribute("data-layout").Should().Be("tablet");
        JSInterop.VerifyNotInvoke("import");
    }

    // ── TmEditorShell forwards its Breakpoints ──────────────────────────────

    [Fact]
    public void Shell_Auto_ForwardsItsBreakpointsToTheObserverModule()
    {
        var module = JSInterop.SetupModule(ObserverModule);
        var observe = module.SetupVoid("observe", _ => true);

        Render<TmEditorShell>(p => p
            .Add(x => x.Breakpoints, new TmLayoutBreakpoints(768, 1200))
            .Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas")));

        Thresholds(observe.Invocations.Single().Arguments[3]!).Should().Be((768, 1200));
    }

    [Fact]
    public void Shell_Auto_WithoutBreakpoints_UsesTheDefaults()
    {
        var module = JSInterop.SetupModule(ObserverModule);
        var observe = module.SetupVoid("observe", _ => true);

        Render<TmEditorShell>(p => p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas")));

        Thresholds(observe.Invocations.Single().Arguments[3]!).Should().Be((640, 1024));
    }
}
