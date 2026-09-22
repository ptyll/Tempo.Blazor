using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Overlay;

/// <summary>TDD tests for the TmOverlayPanel floating primitive.</summary>
public class TmOverlayPanelTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/overlay.js";

    private BunitJSModuleInterop SetupOverlayModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("open", _ => true).SetVoidResult();
        module.SetupVoid("update", _ => true).SetVoidResult();
        module.SetupVoid("close", _ => true).SetVoidResult();
        return module;
    }

    /// <summary>
    /// N198 (review 2026-09-22): a JSInterop handler for the <c>import</c> call whose result
    /// completes only when the test releases it — the window in which a first
    /// <c>OnAfterRenderAsync</c> is parked on the module import while a second run starts.
    /// </summary>
    private sealed class GatedModuleImport : JSRuntimeInvocationHandlerBase<IJSObjectReference>
    {
        // bUnit's JSObjectReferenceInvocationHandler answers 'import' with an internal
        // BunitJSObjectReference bound to the module — the gated equivalent must return the same
        // type so the component can drive open/update/close on it.
        private static readonly Type ObjectReferenceType = typeof(BunitJSInterop).Assembly
            .GetType("Bunit.BunitJSObjectReference")!;

        private readonly TaskCompletionSource<IJSObjectReference> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly BunitJSModuleInterop _module;

        public GatedModuleImport(BunitJSModuleInterop module)
            : base(
                invocation => invocation.Identifier == "import"
                    && invocation.Arguments is [string path] && path == ModulePath,
                false)
            => _module = module;

        /// <summary>How many import invocations have been dispatched to this handler.</summary>
        public int DispatchCount { get; private set; }

        public void Release() => _gate.TrySetResult(
            (IJSObjectReference)Activator.CreateInstance(ObjectReferenceType, _module)!);

        protected override Task<IJSObjectReference> HandleAsync(JSRuntimeInvocation invocation)
        {
            DispatchCount++;
            return _gate.Task;
        }
    }

    /// <summary>
    /// N198: a second OnAfterRenderAsync that starts while the first is still parked on the
    /// module import must not see a stale _jsOpen — on the unlocked code the first run resumed
    /// past its already-taken <c>if (IsVisible)</c> check and issued <c>open()</c> for a panel
    /// whose final state is CLOSED, leaving a tracked entry no later close() would ever release.
    /// The semaphore makes the second run re-read the CURRENT state: it sees the first run's
    /// open and answers the final state with exactly one close().
    /// </summary>
    [Fact]
    public async Task OverlayPanel_OverlappingRenders_BeforeImportCompletes_EndsInConsistentState()
    {
        var module = new BunitJSModuleInterop(JSInterop);
        module.SetupVoid("open", _ => true).SetVoidResult();
        module.SetupVoid("update", _ => true).SetVoidResult();
        module.SetupVoid("close", _ => true).SetVoidResult();
        var import = new GatedModuleImport(module);
        JSInterop.AddInvocationHandler(import);

        // Render OPEN: the first OnAfterRenderAsync parks inside the import gate.
        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .AddChildContent("<p>panel</p>"));

        // The first OnAfterRenderAsync must be parked on the gated import before the second
        // render is issued — otherwise the race is not being reproduced at all.
        cut.WaitForAssertion(() => import.DispatchCount.Should().Be(1));

        // A second render flips the panel closed while the first run is still parked on the
        // import — the open→close re-toggle behind N198.
        cut.Render(p => p.Add(x => x.IsOpen, false));

        import.Release();

        // The parked runs finish their JS calls on dispatcher continuations that produce no
        // renders — poll until both calls have landed instead of sampling mid-flight. The buggy
        // variant never sends close(), so it sits at a lone open() until the deadline and the
        // equality assert below is what fails.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (module.Invocations["open"].Count() + module.Invocations["close"].Count() < 2
            && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        // Final state is CLOSED: exactly one close() answering the one open() the first run
        // issued while the state was still open. A lone open() (1:0) is the leaked tracked
        // entry the old code produced — the second run had already passed without closing it.
        module.Invocations["open"].Count().Should().Be(1,
            "první run prošel za IsVisible==true kontrolou — jeho open() je legitimní");
        module.Invocations["close"].Count().Should().Be(1,
            "druhý run musí za semaforem vidět AKTUÁLNÍ _jsOpen a zavřít — ne osiřelé open()");
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
    }

    /// <summary>
    /// N172: a close() that fails with a non-JSDisconnected error (JSException — slow/stuck JS,
    /// not a torn-down circuit) must not escape DisposeAsync, and the module must still be
    /// released. The old code caught only JSDisconnectedException and had _module.DisposeAsync()
    /// OUTSIDE the try — the JSException skipped it entirely.
    /// </summary>
    [Fact]
    public async Task OverlayPanel_Dispose_WhenCloseThrows_StillDisposesModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("open", _ => true).SetVoidResult();
        module.SetupVoid("close", _ => true).SetException(new JSException("boom"));

        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .AddChildContent("<p>panel</p>"));

        var act = () => cut.Instance.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync(
            "DisposeAsync musí doběhnout i když close() vyhodí JSException — modul se uklidí " +
            "ve finally a chyba úklidu není pro renderer akční (N172)");
    }

    /// <summary>
    /// N167: a controlled consumer may VETO the dismissal — IsOpenChanged that leaves IsOpen
    /// true. NotifyDismissedAsync then answers false so overlay.js re-arms the entry.
    /// </summary>
    [Fact]
    public async Task OverlayPanel_JsDismissal_Vetoed_ReportsStillOpen()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            // Veto: the handler receives the request but never flips IsOpen to false.
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, _ => { }))
            .AddChildContent("<p>panel</p>"));

        var accepted = await cut.InvokeAsync(() => cut.Instance.NotifyDismissedAsync("escape"));

        accepted.Should().BeFalse(
            "konzument dismisi vetoval (IsOpen zůstalo true) — JS musí záznam re-armovat");
        cut.FindAll(".tm-overlay-panel").Should().HaveCount(1);
    }

    /// <summary>
    /// N167: the accepting direction — an uncontrolled panel actually closes, so the answer is
    /// true and the entry stays dismissed until close() releases it.
    /// </summary>
    [Fact]
    public async Task OverlayPanel_JsDismissal_Accepted_ReportsClosed()
    {
        SetupOverlayModule();
        var cut = Render<TmOverlayPanel>(p => p
            .AddChildContent("<p>panel</p>"));

        await cut.InvokeAsync(() => cut.Instance.SetOpenAsync(true));
        var accepted = await cut.InvokeAsync(() => cut.Instance.NotifyDismissedAsync("escape"));

        accepted.Should().BeTrue();
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
    }

    [Fact]
    public void OverlayPanel_Closed_RendersNothing()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .AddChildContent("<p>panel</p>"));

        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
    }

    [Fact]
    public void OverlayPanel_Open_RendersPopoverManualDiv()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .AddChildContent("<p>panel</p>"));

        var panel = cut.Find(".tm-overlay-panel");
        panel.GetAttribute("popover").Should().Be("manual");
        panel.InnerHtml.Should().Contain("panel");
    }

    [Fact]
    public void OverlayPanel_Role_IsApplied()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Role, "menu")
            .AddChildContent("<p>panel</p>"));

        cut.Find(".tm-overlay-panel").GetAttribute("role").Should().Be("menu");
    }

    [Fact]
    public void OverlayPanel_CustomClass_IsApplied()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Class, "tm-dropdown-menu")
            .AddChildContent("<p>panel</p>"));

        var panel = cut.Find(".tm-overlay-panel");
        panel.ClassList.Should().Contain("tm-overlay-panel");
        panel.ClassList.Should().Contain("tm-dropdown-menu");
    }

    [Fact]
    public void OverlayPanel_Id_IsApplied()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Id, "my-panel")
            .AddChildContent("<p>panel</p>"));

        cut.Find(".tm-overlay-panel").Id.Should().Be("my-panel");
    }

    [Fact]
    public void OverlayPanel_Open_InvokesJsOpen_WithSerializedOptions()
    {
        var module = SetupOverlayModule();

        Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Placement, OverlayPlacement.Top)
            .Add(x => x.Align, OverlayAlign.Center)
            .Add(x => x.Offset, 12)
            .Add(x => x.ViewportMargin, 16)
            .Add(x => x.Flip, false)
            .Add(x => x.Shift, false)
            .Add(x => x.MatchAnchorWidth, true)
            .Add(x => x.ConstrainHeight, true)
            .Add(x => x.CloseOnEscape, false)
            .Add(x => x.CloseOnOutsidePointerDown, false)
            .AddChildContent("<p>panel</p>"));

        var invocation = module.Invocations["open"].Should().ContainSingle().Subject;
        // open(key, panel, anchor, dotNetRef, options) — options is argument index 4.
        var options = invocation.Arguments[4]!;
        options.GetProperty("placement").Should().Be("top");
        options.GetProperty("align").Should().Be("center");
        options.GetProperty("offset").Should().Be(12);
        options.GetProperty("margin").Should().Be(16);
        options.GetProperty("flip").Should().Be(false);
        options.GetProperty("shift").Should().Be(false);
        options.GetProperty("matchAnchorWidth").Should().Be(true);
        options.GetProperty("constrainHeight").Should().Be(true);
        options.GetProperty("closeOnEscape").Should().Be(false);
        options.GetProperty("closeOnOutsidePointerDown").Should().Be(false);
    }

    [Fact]
    public void OverlayPanel_TurningIsOpenOff_InvokesJsClose()
    {
        var module = SetupOverlayModule();

        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .AddChildContent("<p>panel</p>"));

        cut.Render(p => p.Add(x => x.IsOpen, false));

        module.VerifyInvoke("close", 1);
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
    }

    [Fact]
    public void OverlayPanel_OptionsChangeWhileOpen_InvokesJsUpdate()
    {
        var module = SetupOverlayModule();

        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Placement, OverlayPlacement.Bottom)
            .AddChildContent("<p>panel</p>"));

        cut.Render(p => p.Add(x => x.Placement, OverlayPlacement.Top));

        module.VerifyInvoke("update", 1);
    }

    [Fact]
    public void OverlayPanel_Dispose_InvokesJsClose()
    {
        var module = SetupOverlayModule();

        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .AddChildContent("<p>panel</p>"));

        cut.Instance.DisposeAsync().AsTask().Wait();

        module.VerifyInvoke("close", 1);
    }

    [Fact]
    public async Task OverlayPanel_JsDismissal_FiresIsOpenChanged_And_OnDismissed()
    {
        bool? reported = null;
        string? reason = null;
        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => reported = v))
            .Add(x => x.OnDismissed, EventCallback.Factory.Create<string>(this, r => reason = r))
            .AddChildContent("<p>panel</p>"));

        await cut.InvokeAsync(() => cut.Instance.NotifyDismissedAsync("escape"));

        reported.Should().BeFalse();
        reason.Should().Be("escape");
    }

    [Fact]
    public async Task OverlayPanel_JsDismissal_Outside_ReportsReason()
    {
        string? reason = null;
        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, _ => { }))
            .Add(x => x.OnDismissed, EventCallback.Factory.Create<string>(this, r => reason = r))
            .AddChildContent("<p>panel</p>"));

        await cut.InvokeAsync(() => cut.Instance.NotifyDismissedAsync("outside"));

        reason.Should().Be("outside");
    }

    [Fact]
    public async Task OverlayPanel_ProgrammaticClose_DoesNotFireOnDismissed()
    {
        string? reason = null;
        var cut = Render<TmOverlayPanel>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, _ => { }))
            .Add(x => x.OnDismissed, EventCallback.Factory.Create<string>(this, r => reason = r))
            .AddChildContent("<p>panel</p>"));

        await cut.InvokeAsync(() => cut.Instance.CloseAsync());

        reason.Should().BeNull();
    }

    [Fact]
    public async Task OverlayPanel_Uncontrolled_Dismissal_ClosesInternally()
    {
        var cut = Render<TmOverlayPanel>(p => p
            .AddChildContent("<p>panel</p>"));

        await cut.InvokeAsync(() => cut.Instance.SetOpenAsync(true));
        cut.FindAll(".tm-overlay-panel").Should().HaveCount(1);

        await cut.InvokeAsync(() => cut.Instance.NotifyDismissedAsync("escape"));
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
    }
}

file static class ReflectionExtensions
{
    public static object? GetProperty(this object obj, string name)
        => obj.GetType().GetProperty(name)?.GetValue(obj);
}
