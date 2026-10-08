using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Helpers;

/// <summary>
/// An overlay without a viewport scope measures the viewport itself through the internal
/// <see cref="ViewportProbe"/>. Razor only binds a tag to a public component, so the probe must be
/// opened from C# in a render fragment: a literal <c>&lt;ViewportProbe /&gt;</c> tag on an internal
/// type compiles to an inert HTML element and nothing ever measures. These tests pin the component
/// tree, the JS contract and the measurement flow.
/// </summary>
public class ViewportProbeTests : LocalizationTestBase
{
    private const string ObserverModule = "./_content/Tempo.Blazor/js/layout-observer.js";

    public ViewportProbeTests() => OverlayLayout.ResetHint();

    [Fact]
    public void Modal_WithNoScope_RendersTheProbeAsAComponent_AndObservesTheViewport()
    {
        var module = JSInterop.SetupModule(ObserverModule);

        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .AddChildContent("<p>Body</p>"));

        cut.FindComponents<ViewportProbe>().Should().ContainSingle(
            "only a real component instance carries the OnMeasured callback");
        cut.Markup.Should().NotContain("<viewportprobe",
            "Razor cannot bind a tag to an internal component; a literal element in the markup means the probe is dead");
        module.VerifyInvoke("observe");
        cut.FindAll(".tm-viewport-probe").Should().ContainSingle(
            "the probe's own fixed, hidden box is the only element with the class");
    }

    [Fact]
    public void Dialog_WithNoScope_RendersTheProbeAsAComponent_AndObservesTheViewport()
    {
        var module = JSInterop.SetupModule(ObserverModule);

        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Delete dashboard")
            .Add(d => d.Message, "This cannot be undone."));

        cut.FindComponents<ViewportProbe>().Should().ContainSingle();
        cut.Markup.Should().NotContain("<viewportprobe");
        module.VerifyInvoke("observe");
    }

    [Fact]
    public void Drawer_WithNoScope_RendersTheProbeAsAComponent_AndObservesTheViewport()
    {
        var module = JSInterop.SetupModule(ObserverModule);

        var cut = Render<TmDrawer>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.Position, DrawerPosition.Bottom)
            .AddChildContent("Body"));

        cut.FindComponents<ViewportProbe>().Should().ContainSingle();
        cut.Markup.Should().NotContain("<viewportprobe");
        module.VerifyInvoke("observe");
    }

    [Fact]
    public async Task Modal_ProbeMeasuresMobile_RendersTheSheet()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .AddChildContent("<p>Body</p>"));

        await ReportMobileAsync(cut);

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("mobile",
            "the probe's measurement stands in for the missing viewport");
        overlay.ClassList.Should().Contain("tm-modal--sheet");
    }

    [Fact]
    public async Task Dialog_ProbeMeasuresMobile_RendersTheSheet()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Delete dashboard")
            .Add(d => d.Message, "This cannot be undone."));

        await ReportMobileAsync(cut);

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("mobile");
        overlay.ClassList.Should().Contain("tm-modal--sheet");
    }

    [Fact]
    public async Task Drawer_ProbeMeasuresMobile_RendersMobile()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.Position, DrawerPosition.Bottom)
            .AddChildContent("Body"));

        await ReportMobileAsync(cut);

        cut.Find(".tm-drawer").GetAttribute("data-layout").Should().Be("mobile",
            "a phone host gets the sheet layout even without a viewport scope");
    }

    [Fact]
    public void Modal_WithAViewportScope_RendersNoProbe()
    {
        var cut = Render<ViewportScopeHost>();

        cut.FindComponents<ViewportProbe>().Should().BeEmpty(
            "a cascaded viewport context is the measurement; the probe would double-observe");
    }

    [Fact]
    public void Modal_WithAForcedLayoutMode_RendersNoProbe()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent("<p>Body</p>"));

        cut.FindComponents<ViewportProbe>().Should().BeEmpty(
            "a forced mode never measures");
    }

    [Fact]
    public void Modal_WithAForcedPresentation_RendersNoProbe()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.MobilePresentation, MobilePresentation.Dialog)
            .AddChildContent("<p>Body</p>"));

        cut.FindComponents<ViewportProbe>().Should().BeEmpty();
    }

    [Fact]
    public void Drawer_ForcedLayoutMode_RendersNoProbe()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.Position, DrawerPosition.Bottom)
            .Add(d => d.LayoutMode, TmLayoutMode.Tablet)
            .AddChildContent("Body"));

        cut.FindComponents<ViewportProbe>().Should().BeEmpty();
    }

    [Fact]
    public void NonModalDrawer_RendersNoProbe()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.Position, DrawerPosition.Bottom)
            .Add(d => d.Modal, false)
            .AddChildContent("Body"));

        cut.FindComponents<ViewportProbe>().Should().BeEmpty(
            "an inline sheet is a fraction of its host, not of the viewport");
    }

    [Fact]
    public void HiddenClosedOverlay_RendersNoProbe()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, false)
            .Add(m => m.Title, "Create")
            .AddChildContent("<p>Body</p>"));

        cut.FindComponents<ViewportProbe>().Should().BeEmpty(
            "nothing observes while the overlay is not open");
    }

    [Fact]
    public async Task Hint_LogsOnceWhateverTheNumberOfProbes()
    {
        OverlayLayout.ResetHint();
        var logger = new ListLogger();
        Services.AddSingleton<ILogger<OverlayLayout>>(logger);

        var modal = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .AddChildContent("<p>Body</p>"));
        var dialog = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Delete")
            .Add(d => d.Message, "Message"));

        await ReportMobileAsync(modal);
        await ReportMobileAsync(dialog);

        logger.Levels.Should().OnlyContain(level => level == LogLevel.Information);
        logger.Levels.Should().ContainSingle("the hint is logged once per process, not per overlay");
    }

    [Fact]
    public async Task Probe_DisposedBeforeItsImportResolves_ReleasesWithoutThrowing()
    {
        var runtime = new DeferredImportRuntime();
        Services.AddSingleton<IJSRuntime>(runtime);

        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .AddChildContent("<p>Body</p>"));

        var module = new RecordingModule();
        await cut.Instance.DisposeAsync();

        // The import lands after the overlay is gone. The probe must notice _disposed and release
        // the dot-net reference instead of calling observe on a dead renderer.
        runtime.Import.TrySetResult(module);
        var released = await module.Disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        released.Should().BeTrue("a late import must still run the release path");
    }

    private static async Task ReportMobileAsync(IRenderedComponent<TmModal> cut)
    {
        var probe = cut.FindComponent<ViewportProbe>();
        await cut.InvokeAsync(() => probe.Instance.OnLayoutModeChanged("mobile"));
    }

    private static async Task ReportMobileAsync(IRenderedComponent<TmDialog> cut)
    {
        var probe = cut.FindComponent<ViewportProbe>();
        await cut.InvokeAsync(() => probe.Instance.OnLayoutModeChanged("mobile"));
    }

    private static async Task ReportMobileAsync(IRenderedComponent<TmDrawer> cut)
    {
        var probe = cut.FindComponent<ViewportProbe>();
        await cut.InvokeAsync(() => probe.Instance.OnLayoutModeChanged("mobile"));
    }

    /// <summary>Cascades a named viewport context above a modal, so no probe may render.</summary>
    private sealed class ViewportScopeHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Value", new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop));
            builder.AddAttribute(2, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(modal =>
            {
                modal.OpenComponent<TmModal>(0);
                modal.AddAttribute(1, "Show", true);
                modal.AddAttribute(2, "Title", "Create");
                modal.AddAttribute(3, "ChildContent", (RenderFragment)(b => b.AddMarkupContent(0, "<p>Body</p>")));
                modal.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    private sealed class ListLogger : ILogger<OverlayLayout>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }

    /// <summary>An IJSRuntime whose layout-observer import lands when the test completes it.</summary>
    private sealed class DeferredImportRuntime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import")
            {
                return Await();
                async ValueTask<TValue> Await()
                {
                    var module = await Import.Task.ConfigureAwait(false);
                    return (TValue)module;
                }
            }
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
    }

    private sealed class RecordingModule : IJSObjectReference
    {
        public TaskCompletionSource<bool> Disconnected { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "disconnect") Disconnected.TrySetResult(true);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => default;
    }
}
