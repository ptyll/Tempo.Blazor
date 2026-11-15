using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// TDD tests for <see cref="TopLayerPinService"/> / <see cref="TopLayerPin"/> (plan 4ce1b994,
/// decision TOP-LAYER-PIN, docs/top-layer-pin.md §5.3): the C# front door of the pinned-on-top
/// registry. Every call goes through <c>tm-pin-on-top.js</c>; JS being unavailable (prerender, a
/// torn-down circuit) and a dispose racing an in-flight call must never throw.
/// </summary>
public class TopLayerPinServiceTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-pin-on-top.js";

    [Fact]
    public async Task PinAsync_PinsTheElementWithItsPriority_AndReturnsAnActiveHandle()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("pin", _ => true).SetVoidResult();
        await using var service = new TopLayerPinService(JSInterop.JSRuntime);

        var element = new ElementReference("pin-1");
        await using var pin = await service.PinAsync(element, new TopLayerPinOptions { Priority = 100 });

        pin.IsActive.Should().BeTrue();
        var call = module.Invocations.Single(i => i.Identifier == "pin");
        ((ElementReference)call.Arguments[0]!).Id.Should().Be("pin-1");
        ((TopLayerPinOptions)call.Arguments[1]!).Priority.Should().Be(100);
    }

    [Fact]
    public async Task PinAsync_WithoutOptions_PinsAtPriorityZero()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("pin", _ => true).SetVoidResult();
        await using var service = new TopLayerPinService(JSInterop.JSRuntime);

        await using var pin = await service.PinAsync(new ElementReference("pin-1"));

        ((TopLayerPinOptions)module.Invocations.Single(i => i.Identifier == "pin").Arguments[1]!)
            .Priority.Should().Be(0, "the toast containers pin at 0; an unspecified host pin shares their band");
    }

    [Fact]
    public async Task DisposeAsync_UnpinsExactlyOnce_AndIsIdempotent()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("pin", _ => true).SetVoidResult();
        module.SetupVoid("unpin", _ => true).SetVoidResult();
        await using var service = new TopLayerPinService(JSInterop.JSRuntime);
        var pin = await service.PinAsync(new ElementReference("pin-1"));

        await pin.DisposeAsync();
        await pin.DisposeAsync();

        module.Invocations.Count(i => i.Identifier == "unpin").Should().Be(1);
        ((ElementReference)module.Invocations.Single(i => i.Identifier == "unpin").Arguments[0]!).Id.Should().Be("pin-1");
        pin.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SetPriorityAsync_RePinsWithTheNewPriority_AndIsANoOpAfterDispose()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("pin", _ => true).SetVoidResult();
        module.SetupVoid("unpin", _ => true).SetVoidResult();
        await using var service = new TopLayerPinService(JSInterop.JSRuntime);
        var pin = await service.PinAsync(new ElementReference("pin-1"));

        await pin.SetPriorityAsync(7);
        await pin.DisposeAsync();
        await pin.SetPriorityAsync(9);

        module.Invocations.Where(i => i.Identifier == "pin")
            .Select(i => ((TopLayerPinOptions)i.Arguments[1]!).Priority)
            .Should().Equal([0, 7], "re-pinning updates the priority in place; a disposed pin issues nothing");
    }

    [Fact]
    public async Task PinAsync_OnAnElementThatHasNotRendered_IsInactive_AndInvokesNothing()
    {
        var module = JSInterop.SetupModule(ModulePath);
        await using var service = new TopLayerPinService(JSInterop.JSRuntime);

        await using var pin = await service.PinAsync(default);

        pin.IsActive.Should().BeFalse("an ElementReference without an id was never rendered — call PinAsync from OnAfterRenderAsync");
        module.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task PinAsync_WhenJavaScriptIsUnavailable_ReturnsAnInactiveHandle_AndNothingThrows()
    {
        // The prerender runtime throws InvalidOperationException on every interop call.
        await using var service = new TopLayerPinService(new PrerenderJsRuntime());

        var pin = await service.PinAsync(new ElementReference("pin-1"));

        pin.IsActive.Should().BeFalse();
        var act = async () =>
        {
            await pin.SetPriorityAsync(3);
            await pin.DisposeAsync();
        };
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ServiceDisposedWhilePinIsInFlight_DoesNotThrow_AndTheLateHandleIsInactive()
    {
        // The 2.11.0 TmToastContainer lesson: a teardown racing a pending JS round trip must not
        // dereference anything the teardown already released.
        var module = JSInterop.SetupModule(ModulePath);
        var pending = module.SetupVoid("pin", _ => true);   // stays pending until completed below
        module.SetupVoid("unpin", _ => true).SetVoidResult();
        var service = new TopLayerPinService(JSInterop.JSRuntime);

        var pinTask = service.PinAsync(new ElementReference("pin-1"));
        await service.DisposeAsync();
        pending.SetVoidResult();

        var pin = await pinTask;
        pin.IsActive.Should().BeFalse("the scope that owned the pin is gone");
        var act = async () => await pin.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PinAsync_AfterTheServiceWasDisposed_IsInactive()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var service = new TopLayerPinService(JSInterop.JSRuntime);
        await service.DisposeAsync();

        var pin = await service.PinAsync(new ElementReference("pin-1"));

        pin.IsActive.Should().BeFalse();
        module.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void AddTempoBlazor_RegistersTheServiceScoped()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime>(new PrerenderJsRuntime());
        services.AddTempoBlazor();
        using var provider = services.BuildServiceProvider();
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var a = scopeA.ServiceProvider.GetRequiredService<TopLayerPinService>();
        a.Should().BeSameAs(scopeA.ServiceProvider.GetRequiredService<TopLayerPinService>());
        a.Should().NotBeSameAs(scopeB.ServiceProvider.GetRequiredService<TopLayerPinService>(),
            "one service per circuit/scope: the JS module reference belongs to one browser page");
    }

    [Fact]
    public void AddTempoBlazor_InAHostWithoutIJSRuntime_ValidatesOnBuild_AndYieldsAnInactiveService()
    {
        // A non-Blazor host (the demo API, a worker) calls AddTempoBlazor() for the localizer and
        // builds its provider with ValidateOnBuild. A constructor-injected IJSRuntime made the whole
        // host fail to start (every Demo.Api test went red).
        var services = new ServiceCollection();
        services.AddLogging();   // every real host has logging; the localizer factory needs it
        services.AddTempoBlazor();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TopLayerPinService>();

        var pin = service.PinAsync(new ElementReference("pin-1")).GetAwaiter().GetResult();
        pin.IsActive.Should().BeFalse("without a JS runtime there is nothing to pin through");
    }

    /// <summary>The runtime a prerender pass hands out: every call throws InvalidOperationException.</summary>
    private sealed class PrerenderJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException("JavaScript interop calls cannot be issued during prerendering.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => throw new InvalidOperationException("JavaScript interop calls cannot be issued during prerendering.");
    }
}
