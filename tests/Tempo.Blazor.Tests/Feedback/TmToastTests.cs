using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>TDD tests for TmToast and TmToastContainer.</summary>
public class TmToastTests : LocalizationTestBase
{
    public TmToastTests()
    {
        Services.AddScoped<ToastService>();
    }

    // ── TmToastContainer rendering ──

    [Fact]
    public void Container_Renders_WhenNoToasts_IsEmpty()
    {
        var cut = Render<TmToastContainer>();
        cut.FindAll(".tm-toast").Should().BeEmpty();
    }

    [Fact]
    public void Container_Renders_Toast_WhenServiceShowsCalled()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Hello!");
        cut.Render();

        cut.FindAll(".tm-toast").Should().HaveCount(1);
    }

    [Fact]
    public void Container_Renders_ToastMessage()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("Test message");
        cut.Render();

        cut.Find(".tm-toast-message").TextContent.Trim().Should().Be("Test message");
    }

    [Fact]
    public void Container_Renders_ToastTitle_WhenProvided()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Body", "Title Here");
        cut.Render();

        cut.Find(".tm-toast-title").TextContent.Trim().Should().Be("Title Here");
    }

    [Fact]
    public void Container_NoTitle_WhenNotProvided()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Body only");
        cut.Render();

        cut.FindAll(".tm-toast-title").Should().BeEmpty();
    }

    [Fact]
    public void Container_SeverityClass_Success()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("OK");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--success");
    }

    [Fact]
    public void Container_SeverityClass_Error()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowError("Fail");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--error");
    }

    [Fact]
    public void Container_SeverityClass_Warning()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowWarning("Careful");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--warning");
    }

    [Fact]
    public void Container_SeverityClass_Info()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("FYI");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--info");
    }

    [Fact]
    public void Container_PositionClass_TopRight()
    {
        var cut = Render<TmToastContainer>(p => p.Add(c => c.Position, ToastPosition.TopRight));
        cut.Find(".tm-toast-container").ClassList.Should().Contain("tm-toast-container--top-right");
    }

    [Fact]
    public void Container_PositionClass_BottomLeft()
    {
        var cut = Render<TmToastContainer>(p => p.Add(c => c.Position, ToastPosition.BottomLeft));
        cut.Find(".tm-toast-container").ClassList.Should().Contain("tm-toast-container--bottom-left");
    }

    [Fact]
    public void Container_DismissButton_RemovesToast()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Bye");
        cut.Render();
        cut.FindAll(".tm-toast").Should().HaveCount(1);

        cut.Find(".tm-toast-dismiss").Click();
        cut.Render();

        cut.FindAll(".tm-toast").Should().BeEmpty();
    }

    [Fact]
    public void Container_MaxVisible_LimitsRenderedToasts()
    {
        var cut = Render<TmToastContainer>(p => p.Add(c => c.MaxVisible, 2));
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        svc.ShowInfo("Two");
        svc.ShowWarning("Three");
        cut.Render();

        // Only 2 should be visible (most recent)
        cut.FindAll(".tm-toast").Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Container_MultipleToasts_RendersAll()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        svc.ShowError("Two");
        cut.Render();

        cut.FindAll(".tm-toast").Should().HaveCount(2);
    }

    [Fact]
    public void Container_HasIcon_PerSeverity()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("With icon");
        cut.Render();

        cut.FindAll(".tm-toast-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void Container_HasProgressBar()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("Progress");
        cut.Render();

        cut.FindAll(".tm-toast-progress").Should().NotBeEmpty();
    }

    [Fact]
    public void Container_HasAriaRole_Alert()
    {
        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowError("Alert!");
        cut.Render();

        cut.Find(".tm-toast").GetAttribute("role").Should().Be("alert");
    }

    // ── F3 review round 3 (V1b): the container re-raises on every push ─────────
    // The round-2 raise was keyed on the VISIBLE COUNT: a push at the MaxVisible cap (count
    // unchanged) or an expire+push in one render never re-raised, so the newest toast painted
    // UNDER a sheet or dialog opened after the first push. The container must track the newest
    // toast id instead and raise whenever an unseen id appears.

    [Fact]
    public void Container_Raises_OnEveryPush_EvenAtTheMaxVisibleCap()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        module.SetupVoid("pinRoot", _ => true).SetVoidResult();
        module.SetupVoid("raise", _ => true).SetVoidResult();

        var cut = Render<TmToastContainer>(p => p.Add(c => c.MaxVisible, 1));
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.Render();
        svc.ShowSuccess("Two");
        cut.Render();

        // MaxVisible=1: the visible count stays 1 across both pushes, yet each push must re-raise
        // the container above every surface promoted in between.
        module.Invocations.Where(i => i.Identifier == "raise").Should().HaveCount(2,
            "a push at the MaxVisible cap changes no count — only the newest-id tracking re-raises");
    }

    [Fact]
    public void Container_Raises_WhenAVisibleToastExpiresAndAnotherPushesInOneRender()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        module.SetupVoid("pinRoot", _ => true).SetVoidResult();
        module.SetupVoid("raise", _ => true).SetVoidResult();

        var cut = Render<TmToastContainer>(p => p.Add(c => c.MaxVisible, 2));
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.Render();
        svc.ShowSuccess("Two");
        cut.Render();

        // Expire the OLDEST and push a new one before a render runs: the count is unchanged (still
        // 2 of MaxVisible 2) but the newest id changed, so the container must re-raise.
        svc.Remove(svc.Toasts.OrderBy(t => t.CreatedAt).First().Id);
        svc.ShowInfo("Three");
        cut.Render();

        module.Invocations.Where(i => i.Identifier == "raise").Should().HaveCount(3,
            "an expire+push in one render changes no count — the newest-id tracking must still re-raise");
    }

    [Fact]
    public void Container_Demotes_WhenTheLastToastLeaves_AndUnpins()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        module.SetupVoid("pinRoot", _ => true).SetVoidResult();
        module.SetupVoid("unpinRoot", _ => true).SetVoidResult();
        module.SetupVoid("raise", _ => true).SetVoidResult();
        module.SetupVoid("demote", _ => true).SetVoidResult();

        var cut = Render<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        cut.Render();
        svc.Remove(svc.Toasts.Single().Id);
        cut.Render();

        module.Invocations.Where(i => i.Identifier == "demote").Should().HaveCount(1,
            "the persistent container demotes when the last toast leaves");
        module.Invocations.Where(i => i.Identifier == "unpinRoot").Should().HaveCount(1,
            "a demoted container leaves the pinned-on-top registry so promote() stops re-raising it");
    }
}
