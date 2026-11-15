using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// TDD tests for <see cref="TmPinnedOnTop"/> (plan 4ce1b994, decision TOP-LAYER-PIN,
/// docs/top-layer-pin.md §5.3): the declarative wrapper renders the pin markup, pins itself once
/// after its first render, follows Priority, hides through the hidden attribute (visibility is the
/// signal the module observes, no interop per toggle) and unpins on dispose — including a dispose
/// that races the first pin call.
/// </summary>
public class TmPinnedOnTopTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-pin-on-top.js";

    [Fact]
    public void Renders_TheRootWithThePinMarkup_ClassAndChildContent()
    {
        var cut = Render<TmPinnedOnTop>(p => p
            .Add(c => c.Class, "my-banner")
            .Add(c => c.Priority, 100)
            .AddUnmatched("role", "status")
            .AddChildContent("<span class=\"msg\">Offline</span>"));

        var root = cut.Find("div.tm-pin-on-top");
        root.ClassList.Should().Contain("my-banner");
        root.HasAttribute("data-tm-pin-on-top").Should().BeTrue("the attribute pins prerendered markup too, and the CSS reset keys off it");
        root.GetAttribute("data-tm-pin-priority").Should().Be("100");
        root.GetAttribute("role").Should().Be("status");
        root.GetAttribute("data-testid").Should().Be("pinned-on-top");
        root.QuerySelector(".msg")!.TextContent.Should().Be("Offline");
    }

    [Fact]
    public void Visible_False_RendersTheHiddenAttribute_AndTrueRemovesIt()
    {
        var cut = Render<TmPinnedOnTop>(p => p.Add(c => c.Visible, false));
        cut.Find(".tm-pin-on-top").HasAttribute("hidden").Should().BeTrue();

        cut.Render(p => p.Add(c => c.Visible, true));
        cut.Find(".tm-pin-on-top").HasAttribute("hidden").Should().BeFalse(
            "the module observes the hidden attribute — showing needs no interop call");
    }

    [Fact]
    public void Visible_DefaultsToTrue()
    {
        var cut = Render<TmPinnedOnTop>();
        cut.Find(".tm-pin-on-top").HasAttribute("hidden").Should().BeFalse();
    }

    [Fact]
    public void PinsItselfOnce_AfterTheFirstRender_WithItsPriority()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("pin", _ => true).SetVoidResult();

        var cut = Render<TmPinnedOnTop>(p => p.Add(c => c.Priority, 5));
        cut.Render();
        cut.Render(p => p.Add(c => c.Visible, false));

        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "pin").Should().Be(1,
            "re-renders and visibility toggles never re-pin: the module follows the element itself"));
        var call = module.Invocations.Single(i => i.Identifier == "pin");
        ((ElementReference)call.Arguments[0]!).Id.Should().NotBeNullOrEmpty();
        ((TopLayerPinOptions)call.Arguments[1]!).Priority.Should().Be(5);
    }

    [Fact]
    public void PriorityChange_UpdatesThePin()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("pin", _ => true).SetVoidResult();

        var cut = Render<TmPinnedOnTop>(p => p.Add(c => c.Priority, 1));
        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "pin").Should().Be(1));
        cut.Render(p => p.Add(c => c.Priority, 2));

        cut.WaitForAssertion(() => module.Invocations.Where(i => i.Identifier == "pin")
            .Select(i => ((TopLayerPinOptions)i.Arguments[1]!).Priority)
            .Should().Equal([1, 2]));
    }

    [Fact]
    public async Task Dispose_Unpins()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("pin", _ => true).SetVoidResult();
        module.SetupVoid("unpin", _ => true).SetVoidResult();

        var cut = Render<TmPinnedOnTop>();
        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "pin").Should().Be(1));
        await DisposeComponentsAsync();

        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "unpin").Should().Be(1));
    }

    [Fact]
    public async Task DisposeWhileTheFirstPinIsInFlight_DoesNotThrow_AndStillUnpins()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var pending = module.SetupVoid("pin", _ => true);   // pending until completed below
        module.SetupVoid("unpin", _ => true).SetVoidResult();

        var cut = Render<TmPinnedOnTop>();
        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "pin").Should().Be(1));

        await DisposeComponentsAsync();
        await cut.InvokeAsync(() => pending.SetVoidResult());

        // The pin landed after the component was gone: the component must release it, otherwise
        // the module keeps a detached-but-registered element (and a re-rendered page would keep a
        // stale priority for nothing).
        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "unpin").Should().Be(1));
    }

    [Fact]
    public void WithoutARegisteredService_ThePinStillWorks()
    {
        // A host that renders the component without AddTempoBlazor() (the bUnit base registers no
        // TopLayerPinService) must not fail with a missing-service exception.
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("pin", _ => true).SetVoidResult();

        Render<TmPinnedOnTop>();

        module.Invocations.Count(i => i.Identifier == "pin").Should().Be(1);
    }

    [Fact]
    public void UsesTheRegisteredScopedService_WhenThereIsOne()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("pin", _ => true).SetVoidResult();
        Services.AddScoped<TopLayerPinService>();

        Render<TmPinnedOnTop>();
        Render<TmPinnedOnTop>();

        module.Invocations.Count(i => i.Identifier == "pin").Should().Be(2);
        JSInterop.Invocations.Count(i => i.Identifier == "import").Should().Be(1,
            "both pins share the circuit's service, so the module is imported once");
    }
}
