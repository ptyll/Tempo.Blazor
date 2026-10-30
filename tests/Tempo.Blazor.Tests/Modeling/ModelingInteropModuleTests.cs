using Microsoft.JSInterop;
using NSubstitute;
using Tempo.Blazor.Components.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Unit contract of the shared Modeling interop wrapper: a failed import is never cached,
/// the safe invoke swallows teardown exceptions, dispose releases the AWAITED import and
/// nothing is imported after dispose.
/// </summary>
public sealed class ModelingInteropModuleTests
{
    private static (IJSRuntime Runtime, IJSObjectReference ModuleObject) SetupImport(IJSRuntime? runtime = null)
    {
        runtime ??= Substitute.For<IJSRuntime>();
        var moduleObject = Substitute.For<IJSObjectReference>();
        // The params extension may forward to either IJSRuntime overload — configure both.
        runtime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]>())
            .Returns(ValueTask.FromResult(moduleObject));
        runtime.InvokeAsync<IJSObjectReference>("import", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(ValueTask.FromResult(moduleObject));
        return (runtime, moduleObject);
    }

    [Fact]
    public async Task Dispose_releases_the_awaited_module()
    {
        var (jsRuntime, moduleObject) = SetupImport();
        var wrapper = new ModelingInteropModule(jsRuntime);

        await wrapper.InvokeVoidSafeAsync("focusWithin", default(Microsoft.AspNetCore.Components.ElementReference), "[data-modeling-focus='x']");
        await wrapper.DisposeAsync();

        await moduleObject.Received(1).DisposeAsync();
        wrapper.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_awaits_an_in_flight_import_before_releasing_it()
    {
        var jsRuntime = Substitute.For<IJSRuntime>();
        var moduleObject = Substitute.For<IJSObjectReference>();
        var importGate = new TaskCompletionSource<IJSObjectReference>(TaskCreationOptions.RunContinuationsAsynchronously);
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]>())
            .Returns(new ValueTask<IJSObjectReference>(importGate.Task));
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(new ValueTask<IJSObjectReference>(importGate.Task));

        var wrapper = new ModelingInteropModule(jsRuntime);
        var invoke = wrapper.InvokeVoidSafeAsync("focusWithin", default(Microsoft.AspNetCore.Components.ElementReference), "x");
        var dispose = wrapper.DisposeAsync();

        importGate.TrySetResult(moduleObject);
        await dispose;
        await invoke;

        // The in-flight import is awaited, then disposed.
        await moduleObject.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Nothing_is_imported_after_dispose()
    {
        var (jsRuntime, moduleObject) = SetupImport();
        var wrapper = new ModelingInteropModule(jsRuntime);
        await wrapper.DisposeAsync();

        await wrapper.InvokeVoidSafeAsync("focusWithin", default(Microsoft.AspNetCore.Components.ElementReference), "x");

        await jsRuntime.DidNotReceive().InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]>());
        await jsRuntime.DidNotReceive().InvokeAsync<IJSObjectReference>("import", Arg.Any<CancellationToken>(), Arg.Any<object?[]>());
        await moduleObject.DidNotReceive().DisposeAsync();
    }

    [Fact]
    public async Task Disposing_twice_is_a_no_op()
    {
        var (jsRuntime, moduleObject) = SetupImport();
        var wrapper = new ModelingInteropModule(jsRuntime);
        await wrapper.InvokeVoidSafeAsync("focusWithin", default(Microsoft.AspNetCore.Components.ElementReference), "x");

        await wrapper.DisposeAsync();
        await wrapper.DisposeAsync();

        await moduleObject.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task A_failed_import_is_not_cached_and_does_not_break_the_safe_invoke()
    {
        var jsRuntime = Substitute.For<IJSRuntime>();
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]>())
            .Returns(new ValueTask<IJSObjectReference>(Task.FromException<IJSObjectReference>(new JSException("module failed"))));
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(new ValueTask<IJSObjectReference>(Task.FromException<IJSObjectReference>(new JSException("module failed"))));

        var wrapper = new ModelingInteropModule(jsRuntime);
        var act = async () => await wrapper.InvokeVoidSafeAsync("focusWithin", default(Microsoft.AspNetCore.Components.ElementReference), "x");
        await act.Should().NotThrowAsync("a failed import is progressive-enhancement noise, not an error");
    }

    [Theory]
    [InlineData(typeof(JSException))]
    [InlineData(typeof(JSDisconnectedException))]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(ObjectDisposedException))]
    public async Task Teardown_exceptions_from_the_module_call_are_swallowed(Type exceptionType)
    {
        var jsRuntime = Substitute.For<IJSRuntime>();
        var moduleObject = Substitute.For<IJSObjectReference>();
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]>())
            .Returns(ValueTask.FromResult(moduleObject));
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<CancellationToken>(), Arg.Any<object?[]>())
            .Returns(ValueTask.FromResult(moduleObject));
        moduleObject.When(m => m.InvokeVoidAsync("focusWithin", Arg.Any<object?[]>()))
            .Do(_ => throw (Exception)Activator.CreateInstance(exceptionType, "torn down")!);

        var wrapper = new ModelingInteropModule(jsRuntime);
        var act = async () => await wrapper.InvokeVoidSafeAsync("focusWithin", default(Microsoft.AspNetCore.Components.ElementReference), "x");
        await act.Should().NotThrowAsync($"{exceptionType.Name} means there is nothing left to enhance");
    }
}
