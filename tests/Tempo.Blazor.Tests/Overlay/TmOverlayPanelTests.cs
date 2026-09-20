using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
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
