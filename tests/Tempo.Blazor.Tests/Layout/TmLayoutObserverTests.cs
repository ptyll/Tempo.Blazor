using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// bUnit has no container queries, so a forced <see cref="TmLayoutMode"/> must decide the render
/// without ever touching JS. <see cref="TmLayoutMode.Auto"/> is the only mode that registers the
/// shared ResizeObserver.
/// </summary>
public class TmLayoutObserverTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/layout-observer.js";

    public TmLayoutObserverTests()
    {
        // Strict: a call the component should not make must fail the test, not be swallowed.
        JSInterop.Mode = JSRuntimeMode.Strict;
    }

    [Theory]
    [InlineData(TmLayoutMode.Mobile, "mobile")]
    [InlineData(TmLayoutMode.Tablet, "tablet")]
    [InlineData(TmLayoutMode.Desktop, "desktop")]
    public void ForcedLayoutMode_RendersModifierClass_AndDoesNotImportTheObserver(TmLayoutMode mode, string modifier)
    {
        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, mode)
            .AddChildContent("<span class=\"probe\">content</span>"));

        var root = cut.Find(".tm-layout");
        root.ClassList.Should().Contain($"tm-layout--{modifier}");
        root.GetAttribute("data-layout").Should().Be(modifier);
        cut.Find(".probe").Should().NotBeNull();

        JSInterop.VerifyNotInvoke("import");
    }

    [Fact]
    public void ForcedMobile_CascadesResolvedContext_SoChildrenCanBranchWithoutDom()
    {
        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent(ContextProbe.Fragment));

        cut.Find("[data-testid='layout-probe']").TextContent.Should().Be("mobile");
    }

    [Fact]
    public void Auto_RegistersTheObserver_AgainstTheComponentRoot()
    {
        var module = JSInterop.SetupModule(ModulePath);
        var observe = module.SetupVoid("observe", _ => true);

        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Auto)
            .AddChildContent("<span>auto</span>"));

        observe.Invocations.Should().ContainSingle("Auto must register exactly one observer");
        var args = observe.Invocations.Single().Arguments;
        args[0].Should().BeAssignableTo<ElementReference>();
        args[1].Should().BeAssignableTo<DotNetObjectReference<TmLayoutObserver>>();
        args[2].Should().Be(cut.Find(".tm-layout").Id);

        cut.Find(".tm-layout").ClassList.Should().Contain("tm-layout--desktop",
            "prerender and the first frame have no measurement yet, so Auto paints the desktop branch");
    }

    [Fact]
    public async Task OnLayoutModeChanged_RerendersOnlyWhenTheModeChanges()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("observe", _ => true);
        module.SetupVoid("disconnect", _ => true);

        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Auto));
        var observer = cut.Instance;
        var rendersAfterAttach = cut.RenderCount;

        await cut.InvokeAsync(() => observer.OnLayoutModeChanged("tablet"));

        cut.RenderCount.Should().BeGreaterThan(rendersAfterAttach);
        cut.Find(".tm-layout").ClassList.Should().Contain("tm-layout--tablet");
        var rendersAfterChange = cut.RenderCount;

        await cut.InvokeAsync(() => observer.OnLayoutModeChanged("tablet"));

        cut.RenderCount.Should().Be(rendersAfterChange,
            "the observer reports every resize; an unchanged mode must not re-render");
    }

    [Fact]
    public async Task OnLayoutModeChanged_RejectsAnUnknownMode_AndDoesNotRerender()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("observe", _ => true);

        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Auto));
        var renders = cut.RenderCount;

        var act = async () => await cut.InvokeAsync(() => cut.Instance.OnLayoutModeChanged("phone"));

        await act.Should().ThrowAsync<ArgumentException>();
        cut.RenderCount.Should().Be(renders);
        cut.Find(".tm-layout").ClassList.Should().Contain("tm-layout--desktop");
    }

    [Fact]
    public async Task Dispose_DisconnectsTheObserver()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("observe", _ => true);
        var disconnect = module.SetupVoid("disconnect", _ => true);

        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Auto));
        var id = cut.Find(".tm-layout").Id;

        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync());

        disconnect.Invocations.Should().ContainSingle();
        disconnect.Invocations.Single().Arguments[0].Should().Be(id);
    }

    [Fact]
    public async Task Dispose_OfAForcedMode_DoesNotImportTheObserver()
    {
        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        await cut.InvokeAsync(() => ((IAsyncDisposable)cut.Instance).DisposeAsync());

        JSInterop.VerifyNotInvoke("import");
    }

    [Fact]
    public void LayoutModeChanged_FiresWhenTheResolvedModeChanges()
    {
        TmLayoutMode? reported = null;
        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.LayoutModeChanged, EventCallback.Factory.Create<TmLayoutMode>(this, mode => reported = mode)));

        reported.Should().BeNull("the initial forced value is the caller's own value, not a change");

        cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.LayoutModeChanged, EventCallback.Factory.Create<TmLayoutMode>(this, mode => reported = mode)));

        reported.Should().Be(TmLayoutMode.Mobile);
    }

    /// <summary>Renders the cascaded resolved mode, proving the context is consumable without a DOM measurement.</summary>
    private sealed class ContextProbe : ComponentBase
    {
        public static RenderFragment Fragment => builder =>
        {
            builder.OpenComponent<ContextProbe>(0);
            builder.CloseComponent();
        };

        [CascadingParameter] public TmLayoutContext? Layout { get; set; }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "data-testid", "layout-probe");
            builder.AddContent(2, Layout?.CssModifier ?? "none");
            builder.CloseElement();
        }
    }
}
