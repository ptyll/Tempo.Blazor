using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Helpers;

/// <summary>
/// The sheet wrapper's hand-out and cleanup paths: a dispose that lands while the
/// <c>attachGesture</c> invoke is still in flight must still detach through the imported module
/// (the JS detach is idempotent, and the in-flight attach must not re-attach after it), and a
/// failed attach must leave the wrapper unattached so a later <see cref="SheetInterop.DetachAsync"/>
/// stays silent.
/// </summary>
public class SheetInteropTests : LocalizationTestBase
{
    private const string SheetModule = "./_content/Tempo.Blazor/js/tm-sheet.js";

    [Fact]
    public async Task DisposeDuringInFlightAttachGesture_Detaches()
    {
        var module = JSInterop.SetupModule(SheetModule);
        var attach = module.SetupVoid("attachGesture", _ => true);   // stays pending until released below
        module.SetupVoid("detach", _ => true).SetVoidResult();

        var interop = new SheetInterop(JSInterop.JSRuntime);
        var attaching = interop.AttachAsync<object>(null, default, new[] { 0.5, 1.0 }, false, null);

        await interop.DisposeAsync();      // the owner dies while the gesture attach is in flight
        attach.SetVoidResult();            // … the attach lands after it
        await attaching;

        module.Invocations.Should().Contain(i => i.Identifier == "attachGesture",
            "the attach reached the module");
        module.Invocations.Should().Contain(i => i.Identifier == "detach",
            "a dispose that races the in-flight attach must still detach — the attachGesture " +
            "listeners may have landed before the circuit went away");
    }

    [Fact]
    public async Task AttachAsync_WhenTheGestureInvokeFails_IsNotAttached()
    {
        var module = JSInterop.SetupModule(SheetModule);
        module.SetupVoid("attachGesture", _ => true).SetException(new JSException("attach failed"));

        var interop = new SheetInterop(JSInterop.JSRuntime);
        await interop.AttachAsync<object>(null, default, new[] { 0.5, 1.0 }, false, null);

        await interop.DetachAsync();

        module.Invocations.Should().Contain(i => i.Identifier == "attachGesture",
            "the attach attempt reached the module");
        module.Invocations.Should().NotContain(i => i.Identifier == "detach",
            "a failed invoke never attached — a later DetachAsync has nothing to remove");
    }

    [Fact]
    public async Task DisposeAfterAFailedAttach_DetachesIdempotentlyWithoutThrowing()
    {
        var module = JSInterop.SetupModule(SheetModule);
        module.SetupVoid("attachGesture", _ => true).SetException(new JSException("attach failed"));
        module.SetupVoid("detach", _ => true).SetVoidResult();

        var interop = new SheetInterop(JSInterop.JSRuntime);
        await interop.AttachAsync<object>(null, default, new[] { 0.5, 1.0 }, false, null);

        Func<Task> act = async () => await interop.DisposeAsync();

        await act.Should().NotThrowAsync("the dispose-time detach is best-effort and idempotent");
    }
}
