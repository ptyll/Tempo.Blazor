using Bunit;
using FluentAssertions;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Buttons;

/// <summary>TDD tests for TmCopyButton.</summary>
public class TmCopyButtonTests : LocalizationTestBase
{
    [Fact]
    public void CopyButton_Renders()
    {
        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me"));

        cut.Find(".tm-copy-button").Should().NotBeNull();
    }

    [Fact]
    public void CopyButton_HasAriaLabel()
    {
        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me"));

        cut.Find(".tm-copy-button").GetAttribute("aria-label").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CopyButton_HasCopyIcon()
    {
        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me"));

        // Should have an SVG icon
        cut.Find(".tm-copy-button svg").Should().NotBeNull();
    }

    [Fact]
    public void CopyButton_CustomClass_IsApplied()
    {
        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me")
            .Add(x => x.Class, "my-copy"));

        cut.Find(".tm-copy-button").ClassList.Should().Contain("my-copy");
    }

    [Fact]
    public void CopyButton_Default_ShowsNoVisibleLabel()
    {
        // Frozen default: the button stays icon-only. The label is the accessible name.
        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me"));

        cut.FindAll(".tm-copy-button-label").Should().BeEmpty();
        cut.Find(".tm-copy-button").GetAttribute("aria-label").Should().Be("Copy to clipboard");
    }

    [Fact]
    public void CopyButton_ShowLabel_RendersTheLocalizedLabel_AndSwapsItWhenCopied()
    {
        JSInterop.SetupVoid("navigator.clipboard.writeText", "copy me").SetVoidResult();

        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me")
            .Add(x => x.ShowLabel, true));

        VisibleLabel(cut).Should().Be("Copy");
        cut.Find(".tm-copy-button").ClassList.Should().Contain("tm-copy-button--labelled");

        cut.Find(".tm-copy-button").Click();

        VisibleLabel(cut).Should().Be("Copied");
        // The accessible name keeps the historical long form until the action completes.
        cut.Find(".tm-copy-button").GetAttribute("aria-label").Should().Be("Copied");

        // L10: the swap is visible, but a screen reader needs it announced. A polite live region
        // carries "Copied" and is visually hidden so it never changes the layout.
        var status = cut.Find("[role='status']");
        status.TextContent.Trim().Should().Be("Copied");
        status.ClassList.Should().Contain("tm-sr-only");
    }

    [Fact]
    public void CopyButton_Click_ShowsSuccessState()
    {
        // Use bUnit's JSInterop to mock clipboard call
        JSInterop.SetupVoid("navigator.clipboard.writeText", "copy me").SetVoidResult();

        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me"));

        cut.Find(".tm-copy-button").Click();

        // After click, should show success state
        cut.Find(".tm-copy-button").ClassList.Should().Contain("tm-copy-button--copied");
    }

    [Fact]
    public void ShowLabel_InactiveLabel_StaysInLayout_SoTheButtonDoesNotJump()
    {
        // tm-sr-only is position:absolute and would drop the inactive label out of the shared grid
        // cell; the inactive label must stay in flow (visibility:hidden) to reserve its width.
        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me")
            .Add(x => x.ShowLabel, true));

        var labels = cut.FindAll(".tm-copy-button-label");
        labels.Should().HaveCount(2);
        labels.Should().NotContain(label => label.ClassList.Contains("tm-sr-only"));
        var hidden = labels.Single(label => label.ClassList.Contains("tm-copy-button-label--hidden"));
        hidden.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void CopyButton_OnCopied_FiresAfterASuccessfulWrite()
    {
        // Fáze 10 review (UX 8): the host has to be able to announce the copy itself (a live region in the
        // dialog), which it cannot do from a click on the surrounding box — a click there is not a copy.
        JSInterop.SetupVoid("navigator.clipboard.writeText", "copy me").SetVoidResult();
        var invocations = 0;

        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me")
            .Add(x => x.OnCopied, () => invocations++));

        cut.Find(".tm-copy-button").Click();

        invocations.Should().Be(1);
        JSInterop.Invocations["navigator.clipboard.writeText"].Should().ContainSingle();
    }

    [Fact]
    public void CopyButton_OnCopied_DoesNotFireWhenTheClipboardRefuses()
    {
        // A denied clipboard must not announce "Copied to clipboard": there is nothing on the clipboard.
        JSInterop.SetupVoid("navigator.clipboard.writeText", "copy me")
            .SetException(new JSException("denied"));
        var invocations = 0;

        var cut = Render<TmCopyButton>(p => p
            .Add(x => x.Text, "copy me")
            .Add(x => x.OnCopied, () => invocations++));

        cut.Find(".tm-copy-button").Click();

        invocations.Should().Be(0);
    }

    // The labelled button renders both states so the wider one reserves the width; the visible label
    // is the one that is not hidden.
    private static string VisibleLabel(IRenderedComponent<TmCopyButton> cut) =>
        cut.FindAll(".tm-copy-button-label")
            .Single(label => !label.ClassList.Contains("tm-copy-button-label--hidden"))
            .TextContent.Trim();
}
