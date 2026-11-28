using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NSubstitute;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// Redesign 146c7e1b, phase 13 fix round: the tracker lives in the browser under an id the owner chose, so a node Blazor already removed
/// can still be released, and a request the owner withdrew while the module was still importing never leaves a tracker behind.
/// </summary>
public class VisualViewportInteropTests
{
    private sealed class FakeModule : IJSObjectReference
    {
        public readonly List<(string Identifier, object?[] Args)> Calls = [];
        public TaskCompletionSource? AttachGate;
        public bool Disposed;

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            if (identifier == "attach" && AttachGate is not null)
            {
                await AttachGate.Task;
            }

            return default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Harness
    {
        public readonly TaskCompletionSource<IJSObjectReference> Import = new();
        public readonly IJSRuntime Runtime = Substitute.For<IJSRuntime>();
        public readonly FakeModule Module = new();
        public List<(string Identifier, object?[] Args)> Calls => Module.Calls;

        public Harness(bool importImmediately = true)
        {
            Runtime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(_ => new ValueTask<IJSObjectReference>(Import.Task));
            if (importImmediately)
            {
                Import.SetResult(Module);
            }
        }

        public string? IdOf(string identifier)
        {
            var args = Calls.Last(c => c.Identifier == identifier).Args;
            var last = args[^1];
            return last is string s ? s : last?.GetType().GetProperty("id")?.GetValue(last) as string;
        }
    }

    private static ElementReference Element() => new("el-1");

    [Fact]
    public async Task Attach_AndDetach_UseTheSameId_AndNeverTheElement()
    {
        var h = new Harness();
        await using var interop = new VisualViewportInterop(h.Runtime);

        (await interop.AttachAsync(Element())).Should().BeTrue();
        await interop.DetachAsync();

        var attachId = h.IdOf("attach");
        attachId.Should().NotBeNullOrEmpty();
        h.IdOf("detach").Should().Be(attachId);
        h.Calls.Last(c => c.Identifier == "detach").Args.Should().ContainSingle().Which.Should().Be(attachId, "a node that Blazor removed cannot be passed back");
    }

    [Fact]
    public async Task Detach_WhileTheImportIsPending_NeverAttaches()
    {
        var h = new Harness(importImmediately: false);
        await using var interop = new VisualViewportInterop(h.Runtime);

        var attach = interop.AttachAsync(Element());
        await interop.DetachAsync();
        h.Import.SetResult(h.Module);

        (await attach).Should().BeFalse("the owner withdrew the request before the module was there");
        h.Calls.Should().NotContain(c => c.Identifier == "attach");
    }

    [Fact]
    public async Task Dispose_WhileTheImportIsPending_DetachesByIdBeforeTheModuleIsReleased()
    {
        var h = new Harness(importImmediately: false);
        var interop = new VisualViewportInterop(h.Runtime);

        var attach = interop.AttachAsync(Element());
        await interop.DisposeAsync();
        h.Import.SetResult(h.Module);

        (await attach).Should().BeFalse();
        h.Calls.Should().NotContain(c => c.Identifier == "attach");
        h.Calls.Should().Contain(c => c.Identifier == "detach", "the late-import last will detaches by id, then releases the module");
        h.Module.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task AnAttachThatFinishesAfterADetach_DetachesItself()
    {
        var h = new Harness();
        var gate = new TaskCompletionSource();
        h.Module.AttachGate = gate;
        await using var interop = new VisualViewportInterop(h.Runtime);

        var attach = interop.AttachAsync(Element());
        await Task.Delay(30);
        var detach = interop.DetachAsync();
        gate.SetResult();
        await detach;

        (await attach).Should().BeFalse();
        h.Calls.Should().Contain(c => c.Identifier == "detach", "an attach whose generation is stale detaches itself");
    }

    [Fact]
    public async Task AFailedImport_ReportsFalse_SoACallerCanLatchInsteadOfRetryingEveryRender()
    {
        var h = new Harness(importImmediately: false);
        h.Import.SetException(new JSException("no module"));
        await using var interop = new VisualViewportInterop(h.Runtime);

        (await interop.AttachAsync(Element())).Should().BeFalse();
    }

    [Fact]
    public async Task TheProperty_RidesAlongWhenAnotherConsumerOwnsTheKnob()
    {
        var h = new Harness();
        await using var interop = new VisualViewportInterop(h.Runtime);

        await interop.AttachAsync(Element(), "--my-knob");

        var options = h.Calls.Single(c => c.Identifier == "attach").Args[^1]!;
        options.GetType().GetProperty("property")!.GetValue(options).Should().Be("--my-knob");
    }
}
