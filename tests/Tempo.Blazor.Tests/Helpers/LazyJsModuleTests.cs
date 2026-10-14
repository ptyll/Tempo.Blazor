using FluentAssertions;
using Microsoft.JSInterop;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Tests.Helpers;

/// <summary>
/// cluster-B 8.8: the shared lazy JS module helper owns the import/guard boilerplate every internal
/// interop wrapper duplicated — exactly one in-flight import per owner (concurrent and repeated
/// <see cref="LazyJsModule.GetAsync"/> calls share it), disposal-safe hand-out (an import that
/// outlived the owner is released by the Get that awaited it and later Gets yield null), every
/// interop failure class swallowed (<see cref="JSException"/>, <see cref="JSDisconnectedException"/>,
/// <see cref="TaskCanceledException"/>/<see cref="OperationCanceledException"/>,
/// <see cref="ObjectDisposedException"/>, <see cref="InvalidOperationException"/>), double dispose
/// safe, and nothing at prerender (construction and dispose without a call never touch the
/// <see cref="IJSRuntime"/>). A null or failed import is never cached — the next call retries, so a
/// module that only becomes available after a first unavailable-JS attempt is still honoured.
/// </summary>
public class LazyJsModuleTests
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-lazy-js-module-test.js";

    private readonly FakeJsRuntime _js = new();
    private readonly FakeModule _module = new();

    private LazyJsModule CreateLazy() => new(_js, ModulePath);

    [Fact]
    public void Constructor_NeverTouchesTheJsRuntime()
    {
        _ = CreateLazy();

        _js.Imports.Should().BeEmpty(
            "safe to construct during prerender — no interop happens before the first call");
    }

    [Fact]
    public async Task DisposeWithoutAnyCall_NeverTouchesTheJsRuntime()
    {
        var lazy = CreateLazy();

        await lazy.DisposeAsync();

        _js.Imports.Should().BeEmpty("disposing a never-used helper has nothing to release");
    }

    [Fact]
    public async Task GetAsync_ImportsOnceAndCachesTheModule()
    {
        _js.ImportReturns(_module);
        var lazy = CreateLazy();

        var first = await lazy.GetAsync();
        var second = await lazy.GetAsync();

        first.Should().BeSameAs(_module);
        second.Should().BeSameAs(_module, "the import is cached — every later Get reuses the module");
        _js.Imports.Should().HaveCount(1, "one lazily imported ES module per owner");
        lazy.IsImported.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_ConcurrentCallsShareOneInFlightImport()
    {
        var pending = _js.ImportIsPending();
        var lazy = CreateLazy();

        var first = lazy.GetAsync().AsTask();
        var second = lazy.GetAsync().AsTask();      // lands while the import is still in flight
        pending.SetResult(_module);
        await Task.WhenAll(first, second);

        first.Result.Should().BeSameAs(_module);
        second.Result.Should().BeSameAs(_module);
        _js.Imports.Should().HaveCount(1, "concurrent Gets await the same import instead of racing two imports");
    }

    [Fact]
    public async Task DisposeDuringInFlightImport_ReleasesTheModuleAndLaterGetsYieldNull()
    {
        var pending = _js.ImportIsPending();        // the import is in flight
        var lazy = CreateLazy();
        var get = lazy.GetAsync().AsTask();

        await lazy.DisposeAsync();                  // the owner dies mid-import
        pending.SetResult(_module);                 // … the import completes after it
        var result = await get;

        result.Should().BeNull("a disposed owner is never handed the module");
        _module.DisposeCount.Should().Be(1, "an import that outlived disposal is released by the Get that awaited it");
        (await lazy.GetAsync()).Should().BeNull("GetAsync after dispose yields null");
        _js.Imports.Should().HaveCount(1, "the disposed helper never imports again");
    }

    [Fact]
    public async Task DisposeDuringInFlightImport_RunsTheOwnersLastWillBeforeReleasingTheModule()
    {
        var js = new FakeJsRuntime();
        var pending = js.ImportIsPending();
        var module = new FakeModule();
        var lastWillRuns = 0;
        var lazy = new LazyJsModule(js, ModulePath, async m =>
        {
            await m.InvokeVoidAsync("deactivate");
            lastWillRuns++;
        });
        var get = lazy.GetAsync().AsTask();

        await lazy.DisposeAsync();
        pending.SetResult(module);
        (await get).Should().BeNull();

        lastWillRuns.Should().Be(1, "the last will runs exactly once, with the late module");
        module.Invocations.Should().Contain(i => i.Identifier == "deactivate",
            "the owner's cleanup invoke a racing dispose could not issue itself");
        module.DisposeCount.Should().Be(1, "the module is released after the last will");
    }

    [Fact]
    public async Task DisposeDuringInFlightImport_TwoConcurrentGets_RunTheLastWillAndReleaseOnce()
    {
        var js = new FakeJsRuntime();
        var pending = js.ImportIsPending();
        var module = new FakeModule();
        var lastWillRuns = 0;
        var lazy = new LazyJsModule(js, ModulePath, async m =>
        {
            await m.InvokeVoidAsync("deactivate");
            lastWillRuns++;
        });
        var first = lazy.GetAsync().AsTask();
        var second = lazy.GetAsync().AsTask();      // both Gets share the one in-flight import

        await lazy.DisposeAsync();                  // the owner dies mid-import
        pending.SetResult(module);                  // … the import completes after it
        await Task.WhenAll(first, second);

        first.Result.Should().BeNull("a disposed owner is never handed the module");
        second.Result.Should().BeNull("a disposed owner is never handed the module");
        lastWillRuns.Should().Be(1, "the last will runs exactly once, not once per awaiting Get");
        module.DisposeCount.Should().Be(1, "the late module is released exactly once");
    }

    [Fact]
    public async Task GetAsync_AfterDispose_YieldsNullWithoutImporting()
    {
        var lazy = CreateLazy();
        await lazy.DisposeAsync();

        lazy.IsDisposed.Should().BeTrue();
        (await lazy.GetAsync()).Should().BeNull();
        _js.Imports.Should().BeEmpty("a disposed helper never starts an import");
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheImportedModuleExactlyOnce()
    {
        _js.ImportReturns(_module);
        var lazy = CreateLazy();
        await lazy.GetAsync();

        await lazy.DisposeAsync();
        await lazy.DisposeAsync();                  // double dispose is safe

        _module.DisposeCount.Should().Be(1, "the module reference is disposed exactly once");
        lazy.IsImported.Should().BeFalse("a released module is not handed out again");
    }

    [Fact]
    public async Task GetAsync_WhenTheImportThrows_GuardedExceptionsYieldNull()
    {
        foreach (var exceptionFactory in GuardedExceptions)
        {
            var js = new FakeJsRuntime();
            js.ImportThrows(exceptionFactory());

            (await new LazyJsModule(js, ModulePath).GetAsync()).Should().BeNull(
                $"{exceptionFactory().GetType().Name} from the import is swallowed");
        }
    }

    [Fact]
    public async Task GetAsync_WhenTheImportYieldsNull_IsNotCached()
    {
        _js.ImportReturns(null);
        var lazy = CreateLazy();

        (await lazy.GetAsync()).Should().BeNull("the runtime could not provide the module");

        _js.ImportReturns(_module);                 // the module only becomes available later

        (await lazy.GetAsync()).Should().BeSameAs(_module,
            "a null import is never cached — the next call retries");
        _js.Imports.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAsync_WhenTheImportThrowsANonGuardedReason_ItPropagates()
    {
        // The NRE catches the old wrappers carried are deliberately removed (cluster-B 8.8):
        // an unexpected import failure surfaces instead of being silently degraded.
        _js.ImportThrows(new NullReferenceException("bug"));
        var lazy = CreateLazy();

        Func<Task> act = async () => await lazy.GetAsync();

        await act.Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task GetAsync_AfterAFaultedImport_TheFaultIsNotCached()
    {
        _js.ImportThrows(new NullReferenceException("bug"));
        var lazy = CreateLazy();
        Func<Task> act = async () => await lazy.GetAsync();
        await act.Should().ThrowAsync<NullReferenceException>();

        _js.ImportReturns(_module);                 // a later attempt gets a fresh import

        (await lazy.GetAsync()).Should().BeSameAs(_module, "a faulted import is never cached — the next call retries");
    }

    [Fact]
    public async Task TryInvokeVoidAsync_CallsTheModuleAndReportsSuccess()
    {
        _js.ImportReturns(_module);
        var lazy = CreateLazy();

        (await lazy.TryInvokeVoidAsync("activate", 1, "two", null)).Should().BeTrue();

        _module.Invocations.Should().ContainSingle(i => i.Identifier == "activate");
        _module.Invocations[0].Args.Should().HaveCount(3);
        _module.Invocations[0].Args[0].Should().Be(1);
        _module.Invocations[0].Args[1].Should().Be("two");
        _module.Invocations[0].Args[2].Should().BeNull();
    }

    [Fact]
    public async Task TryInvokeVoidAsync_WithoutModule_YieldsFalse()
    {
        _js.ImportReturns(null);                    // the "JS unavailable" degradation path
        var lazy = CreateLazy();

        (await lazy.TryInvokeVoidAsync("activate", 1)).Should().BeFalse(
            "the call could not be made — the failure class every wrapper guarded against");
    }

    [Fact]
    public async Task TryInvokeVoidAsync_WhenTheModuleInvokeThrows_GuardedExceptionsYieldFalse()
    {
        foreach (var exceptionFactory in GuardedExceptions)
        {
            var module = new FakeModule((_, _) => throw exceptionFactory());
            var js = new FakeJsRuntime();
            js.ImportReturns(module);

            (await new LazyJsModule(js, ModulePath).TryInvokeVoidAsync("activate", 1))
                .Should().BeFalse($"{exceptionFactory().GetType().Name} from the invoke is swallowed");
        }
    }

    [Fact]
    public async Task TryInvokeVoidAsync_WhenTheModuleInvokeThrowsANonGuardedReason_ItPropagates()
    {
        var module = new FakeModule((_, _) => throw new NullReferenceException("bug"));
        _js.ImportReturns(module);
        var lazy = CreateLazy();

        Func<Task> act = async () => await lazy.TryInvokeVoidAsync("activate", 1);

        await act.Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task TryInvokeAsync_ReturnsTheValue()
    {
        var module = new FakeModule((identifier, _) => identifier == "focusIfLost" ? (object)true : null);
        _js.ImportReturns(module);
        var lazy = CreateLazy();

        var (ok, value) = await lazy.TryInvokeAsync<bool>("focusIfLost", "id", null);

        ok.Should().BeTrue();
        value.Should().BeTrue();
    }

    [Fact]
    public async Task TryInvokeAsync_WithoutModule_YieldsFalseAndDefault()
    {
        _js.ImportReturns(null);
        var lazy = CreateLazy();

        var (ok, value) = await lazy.TryInvokeAsync<string>("focusIfLost", "id");

        ok.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public async Task TryInvokeAsync_WhenTheModuleInvokeThrows_GuardedExceptionsYieldFalseAndDefault()
    {
        foreach (var exceptionFactory in GuardedExceptions)
        {
            var module = new FakeModule((_, _) => throw exceptionFactory());
            var js = new FakeJsRuntime();
            js.ImportReturns(module);

            var (ok, value) = await new LazyJsModule(js, ModulePath).TryInvokeAsync<bool>("focusIfLost", "id");

            ok.Should().BeFalse($"{exceptionFactory().GetType().Name} from the invoke is swallowed");
            value.Should().BeFalse();
        }
    }

    [Fact]
    public async Task TryInvokeVoidAsync_AfterDispose_YieldsFalseWithoutReachingTheModule()
    {
        _js.ImportReturns(_module);
        var lazy = CreateLazy();
        await lazy.GetAsync();
        await lazy.DisposeAsync();

        (await lazy.TryInvokeVoidAsync("activate", 1)).Should().BeFalse();

        _module.Invocations.Should().BeEmpty("a disposed helper never reaches the module");
    }

    /// <summary>Every failure class the helper guards (cluster-B 8.8).</summary>
    private static IEnumerable<Func<Exception>> GuardedExceptions
    {
        get
        {
            yield return () => new JSException("module failed");
            yield return () => new JSDisconnectedException("circuit gone");
            yield return () => new TaskCanceledException();
            yield return () => new OperationCanceledException();
            yield return () => new ObjectDisposedException("module");
            yield return () => new InvalidOperationException("prerender");
        }
    }

    /// <summary>An <see cref="IJSRuntime"/> the tests fully control: the import can resolve, stay
    /// pending, yield null or throw — the four states the wrappers' boilerplate had to survive.</summary>
    private sealed class FakeJsRuntime : IJSRuntime
    {
        private Func<ValueTask<IJSObjectReference?>> _import = () => ValueTask.FromResult<IJSObjectReference?>(null);

        public List<(string Identifier, object?[] Args)> Imports { get; } = new();

        public void ImportReturns(IJSObjectReference? module) => _import = () => ValueTask.FromResult(module);

        public void ImportThrows(Exception failure) => _import = () => ValueTask.FromException<IJSObjectReference?>(failure);

        public TaskCompletionSource<IJSObjectReference?> ImportIsPending()
        {
            var completion = new TaskCompletionSource<IJSObjectReference?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _import = () => new ValueTask<IJSObjectReference?>(completion.Task);
            return completion;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsyncCore<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsyncCore<TValue>(identifier, args);

        private ValueTask<TValue> InvokeAsyncCore<TValue>(string identifier, object?[]? args)
        {
            if (identifier != "import") throw new NotSupportedException($"Unexpected interop call '{identifier}'.");
            Imports.Add((identifier, args ?? Array.Empty<object?>()));
            return AsValueTask<TValue>(_import());
        }

        public ValueTask InvokeVoidAsync(string identifier, object?[]? args)
            => throw new NotSupportedException($"Unexpected interop call '{identifier}'.");

        public ValueTask InvokeVoidAsync(string identifier, CancellationToken cancellationToken, object?[]? args)
            => throw new NotSupportedException($"Unexpected interop call '{identifier}'.");

        private static async ValueTask<TValue> AsValueTask<TValue>(ValueTask<IJSObjectReference?> import)
            => (TValue)await import!;
    }

    /// <summary>A recording <see cref="IJSObjectReference"/> the import hands out, so the tests can
    /// observe invokes and — what bUnit's module mock does not record — the DisposeAsync calls.</summary>
    private sealed class FakeModule : IJSObjectReference
    {
        private readonly Func<string, object?[], object?>? _handler;

        public FakeModule(Func<string, object?[], object?>? handler = null) => _handler = handler;

        public List<(string Identifier, object?[] Args)> Invocations { get; } = new();

        public int DisposeCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsyncCore<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsyncCore<TValue>(identifier, args);

        private ValueTask<TValue> InvokeAsyncCore<TValue>(string identifier, object?[]? args)
        {
            Invocations.Add((identifier, args ?? Array.Empty<object?>()));
            var result = _handler?.Invoke(identifier, args ?? Array.Empty<object?>());
            if (result is Exception failure) return ValueTask.FromException<TValue>(failure);
            return ValueTask.FromResult((TValue)result!);
        }

        public ValueTask InvokeVoidAsync(string identifier, object?[]? args)
            => InvokeVoidCore(identifier, args);

        public ValueTask InvokeVoidAsync(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeVoidCore(identifier, args);

        private ValueTask InvokeVoidCore(string identifier, object?[]? args)
        {
            Invocations.Add((identifier, args ?? Array.Empty<object?>()));
            if (_handler?.Invoke(identifier, args ?? Array.Empty<object?>()) is Exception failure)
                return ValueTask.FromException(failure);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
