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
        var observe = module.SetupVoid("observe", invocation => true);

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
        module.SetupVoid("observe", invocation => true);
        module.SetupVoid("disconnect", invocation => true);

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
        module.SetupVoid("observe", invocation => true);

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
        var observe = module.SetupVoid("observe", invocation => true);

        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Auto));
        var id = cut.Find(".tm-layout").Id;

        // Wait out the render that registers the observer, then dispose. Disposing earlier races
        // that render and the disconnect is never recorded.
        // The invocation is recorded before the render method resumes and sets _observing, so a
        // dispose issued on the first observation races that resume and skips the disconnect.
        cut.WaitForAssertion(() => observe.Invocations.Should().NotBeEmpty());

        // bUnit 2.7 records an awaited module call but cannot complete it, so the assertion is the
        // registration: dispose asks the module to disconnect this root.
        var disconnecting = cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());
        cut.WaitForAssertion(() => JSInterop.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "disconnect" && Equals(invocation.Arguments[0], id)));
        _ = disconnecting;
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
            .Add(p => p.ResolvedLayoutChanged, EventCallback.Factory.Create<TmLayoutMode>(this, mode => reported = mode)));

        reported.Should().BeNull("the initial forced value is the caller's own value, not a change");

        cut.Render(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.ResolvedLayoutChanged, EventCallback.Factory.Create<TmLayoutMode>(this, mode => reported = mode)));

        reported.Should().Be(TmLayoutMode.Mobile);
    }

    [Fact]
    public async Task AutoToForcedToAuto_ReturnsToMeasuredMode()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("observe", invocation => true);

        TmLayoutMode? reported = null;
        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Auto)
            .Add(p => p.ResolvedLayoutChanged, EventCallback.Factory.Create<TmLayoutMode>(this, mode => reported = mode)));

        await cut.InvokeAsync(() => cut.Instance.OnLayoutModeChanged("tablet"));
        reported.Should().Be(TmLayoutMode.Tablet);
        reported = null;

        cut.Render(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.ResolvedLayoutChanged, EventCallback.Factory.Create<TmLayoutMode>(this, mode => reported = mode)));
        reported.Should().Be(TmLayoutMode.Mobile);

        // A measurement taken while forced is recorded but does not change what is rendered.
        reported = null;
        await cut.InvokeAsync(() => cut.Instance.OnLayoutModeChanged("desktop"));
        reported.Should().BeNull("a forced mode hides the measurement, so the report is not a change");
        cut.Find(".tm-layout").GetAttribute("data-layout").Should().Be("mobile");

        cut.Render(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Auto)
            .Add(p => p.ResolvedLayoutChanged, EventCallback.Factory.Create<TmLayoutMode>(this, mode => reported = mode)));

        cut.Find(".tm-layout").GetAttribute("data-layout").Should().Be("desktop",
            "switching back to Auto returns to the mode measured while the layout was forced");
        reported.Should().Be(TmLayoutMode.Desktop);
    }

    [Fact]
    public void NestedAuto_UnderForcedMobile_ResolvesMobile_WithoutJs()
    {
        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent(NestedAuto.Fragment));

        cut.Find(".nested").GetAttribute("data-layout").Should().Be("mobile",
            "Auto under a forced ancestor adopts the ancestor and does not measure");
        JSInterop.VerifyNotInvoke("import");
    }

    [Fact]
    public void LayoutContent_ReceivesTheResolvedContext()
    {
        var cut = Render<LayoutContentHost>();

        cut.Find(".owned").TextContent.Should().Be("tablet");
    }

    [Fact]
    public void BothChildContentAndLayoutContent_Throw()
    {
        var act = () => Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.ChildContent, builder => builder.AddContent(0, "child"))
            .Add(p => p.LayoutContent, (TmLayoutContext _) => builder => builder.AddContent(0, "owned")));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsViewportScope_CascadesTheNamedContext()
    {
        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .Add(p => p.IsViewportScope, true)
            .AddChildContent(ViewportProbe.Fragment));

        cut.Find(".viewport-probe").TextContent.Should().Be("desktop");
    }

    /// <summary>Renders through LayoutContent, which the observer does not yet honour.</summary>
    private sealed class LayoutContentHost : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmLayoutObserver>(0);
            builder.AddAttribute(1, "LayoutMode", TmLayoutMode.Tablet);
            builder.AddAttribute(2, "LayoutContent", (RenderFragment<TmLayoutContext>)(layout => inner =>
            {
                inner.OpenElement(0, "span");
                inner.AddAttribute(1, "class", "owned");
                inner.AddContent(2, layout.CssModifier);
                inner.CloseElement();
            }));
            builder.CloseComponent();
        }
    }

    /// <summary>Reads the viewport-scoped cascade, which an ordinary cascade must not satisfy.</summary>
    private sealed class ViewportProbe : ComponentBase
    {
        public static RenderFragment Fragment => builder =>
        {
            builder.OpenComponent<ViewportProbe>(0);
            builder.CloseComponent();
        };

        [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Viewport { get; set; }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "viewport-probe");
            builder.AddContent(2, Viewport?.CssModifier ?? "missing");
            builder.CloseElement();
        }
    }

    [Fact]
    public void PlainChildContent_StillRenders()
    {
        var cut = Render<TmLayoutObserver>(parameters => parameters
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop)
            .AddChildContent("<span class=\"plain\">kept</span>"));

        cut.Find(".plain").TextContent.Should().Be("kept");
    }

    /// <summary>An Auto observer nested under the component under test.</summary>
    private sealed class NestedAuto : ComponentBase
    {
        public static RenderFragment Fragment => builder =>
        {
            builder.OpenComponent<NestedAuto>(0);
            builder.CloseComponent();
        };

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmLayoutObserver>(0);
            builder.AddComponentParameter(1, nameof(TmLayoutObserver.LayoutMode), TmLayoutMode.Auto);
            builder.AddComponentParameter(2, nameof(TmLayoutObserver.Class), "nested");
            builder.CloseComponent();
        }
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
