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
        var cut = RenderComponent<TmToastContainer>();
        cut.FindAll(".tm-toast").Should().BeEmpty();
    }

    [Fact]
    public void Container_Renders_Toast_WhenServiceShowsCalled()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Hello!");
        cut.Render();

        cut.FindAll(".tm-toast").Should().HaveCount(1);
    }

    [Fact]
    public void Container_Renders_ToastMessage()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("Test message");
        cut.Render();

        cut.Find(".tm-toast-message").TextContent.Trim().Should().Be("Test message");
    }

    [Fact]
    public void Container_Renders_ToastTitle_WhenProvided()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Body", "Title Here");
        cut.Render();

        cut.Find(".tm-toast-title").TextContent.Trim().Should().Be("Title Here");
    }

    [Fact]
    public void Container_NoTitle_WhenNotProvided()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("Body only");
        cut.Render();

        cut.FindAll(".tm-toast-title").Should().BeEmpty();
    }

    [Fact]
    public void Container_SeverityClass_Success()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("OK");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--success");
    }

    [Fact]
    public void Container_SeverityClass_Error()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowError("Fail");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--error");
    }

    [Fact]
    public void Container_SeverityClass_Warning()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowWarning("Careful");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--warning");
    }

    [Fact]
    public void Container_SeverityClass_Info()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("FYI");
        cut.Render();

        cut.Find(".tm-toast").ClassList.Should().Contain("tm-toast--info");
    }

    [Fact]
    public void Container_PositionClass_TopRight()
    {
        var cut = RenderComponent<TmToastContainer>(p => p.Add(c => c.Position, ToastPosition.TopRight));
        cut.Find(".tm-toast-container").ClassList.Should().Contain("tm-toast-container--top-right");
    }

    [Fact]
    public void Container_PositionClass_BottomLeft()
    {
        var cut = RenderComponent<TmToastContainer>(p => p.Add(c => c.Position, ToastPosition.BottomLeft));
        cut.Find(".tm-toast-container").ClassList.Should().Contain("tm-toast-container--bottom-left");
    }

    [Fact]
    public void Container_DismissButton_RemovesToast()
    {
        var cut = RenderComponent<TmToastContainer>();
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
        var cut = RenderComponent<TmToastContainer>(p => p.Add(c => c.MaxVisible, 2));
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
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("One");
        svc.ShowError("Two");
        cut.Render();

        cut.FindAll(".tm-toast").Should().HaveCount(2);
    }

    [Fact]
    public void Container_HasIcon_PerSeverity()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowSuccess("With icon");
        cut.Render();

        cut.FindAll(".tm-toast-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void Container_HasProgressBar()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowInfo("Progress");
        cut.Render();

        cut.FindAll(".tm-toast-progress").Should().NotBeEmpty();
    }

    [Fact]
    public void Container_HasAriaRole_Alert()
    {
        var cut = RenderComponent<TmToastContainer>();
        var svc = Services.GetRequiredService<ToastService>();

        svc.ShowError("Alert!");
        cut.Render();

        cut.Find(".tm-toast").GetAttribute("role").Should().Be("alert");
    }
}
