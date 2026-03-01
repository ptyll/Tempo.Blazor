using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>TDD tests for TmAlert.</summary>
public class TmAlertTests : LocalizationTestBase
{
    // ── Rendering ──────────────────────────────────────────

    [Fact]
    public void Alert_RendersWithRoleAlert()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Info)
            .AddChildContent("Test message"));

        cut.Find("[role='alert']").Should().NotBeNull();
    }

    [Fact]
    public void Alert_RendersChildContent()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Info)
            .AddChildContent("Something important"));

        cut.Markup.Should().Contain("Something important");
    }

    [Fact]
    public void Alert_RendersTitle()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Warning)
            .Add(x => x.Title, "Heads up!")
            .AddChildContent("Description text"));

        cut.Find(".tm-alert__title").TextContent.Should().Contain("Heads up!");
    }

    // ── Severity CSS classes ───────────────────────────────

    [Theory]
    [InlineData(AlertSeverity.Info, "tm-alert--info")]
    [InlineData(AlertSeverity.Success, "tm-alert--success")]
    [InlineData(AlertSeverity.Warning, "tm-alert--warning")]
    [InlineData(AlertSeverity.Error, "tm-alert--error")]
    public void Alert_Severity_AppliesCssClass(AlertSeverity severity, string expectedClass)
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, severity)
            .AddChildContent("Content"));

        cut.Find(".tm-alert").ClassList.Should().Contain(expectedClass);
    }

    // ── Variant CSS classes ────────────────────────────────

    [Theory]
    [InlineData(AlertVariant.Soft, "tm-alert--soft")]
    [InlineData(AlertVariant.Filled, "tm-alert--filled")]
    [InlineData(AlertVariant.Outlined, "tm-alert--outlined")]
    public void Alert_Variant_AppliesCssClass(AlertVariant variant, string expectedClass)
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Info)
            .Add(x => x.Variant, variant)
            .AddChildContent("Content"));

        cut.Find(".tm-alert").ClassList.Should().Contain(expectedClass);
    }

    // ── Icon ───────────────────────────────────────────────

    [Fact]
    public void Alert_AutoIcon_RendersIconForSeverity()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Success)
            .AddChildContent("Done"));

        cut.Find(".tm-alert__icon").Should().NotBeNull();
    }

    [Fact]
    public void Alert_CustomIcon_RendersSpecifiedIcon()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Info)
            .Add(x => x.Icon, "star")
            .AddChildContent("Custom icon"));

        cut.Find(".tm-alert__icon").Should().NotBeNull();
    }

    // ── Dismiss ────────────────────────────────────────────

    [Fact]
    public void Alert_Dismissable_ShowsDismissButton()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Info)
            .Add(x => x.Dismissable, true)
            .AddChildContent("Can dismiss"));

        cut.Find(".tm-alert__dismiss").Should().NotBeNull();
    }

    [Fact]
    public void Alert_NotDismissable_NoDismissButton()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Info)
            .AddChildContent("No dismiss"));

        cut.FindAll(".tm-alert__dismiss").Should().BeEmpty();
    }

    [Fact]
    public void Alert_DismissClick_FiresOnDismiss()
    {
        bool dismissed = false;
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Warning)
            .Add(x => x.Dismissable, true)
            .Add(x => x.OnDismiss, EventCallback.Factory.Create(this, () => dismissed = true))
            .AddChildContent("Dismiss me"));

        cut.Find(".tm-alert__dismiss").Click();

        dismissed.Should().BeTrue();
    }

    [Fact]
    public void Alert_DismissClick_HidesAlert()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Info)
            .Add(x => x.Dismissable, true)
            .AddChildContent("Going away"));

        cut.Find(".tm-alert__dismiss").Click();

        cut.FindAll(".tm-alert").Should().BeEmpty();
    }

    // ── Actions slot ───────────────────────────────────────

    [Fact]
    public void Alert_Actions_RendersActionsSlot()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Error)
            .Add(x => x.Actions, (RenderFragment)(b => { b.AddMarkupContent(0, "<button>Retry</button>"); }))
            .AddChildContent("Something failed"));

        cut.Find(".tm-alert__actions").InnerHtml.Should().Contain("Retry");
    }

    // ── Custom class ───────────────────────────────────────

    [Fact]
    public void Alert_CustomClass_IsApplied()
    {
        var cut = RenderComponent<TmAlert>(p => p
            .Add(x => x.Severity, AlertSeverity.Info)
            .Add(x => x.Class, "my-alert")
            .AddChildContent("Custom"));

        cut.Find(".tm-alert").ClassList.Should().Contain("my-alert");
    }
}
